using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Mdex.Gateway.Auth;

[ApiController]
public sealed class AuthorizationController(IOpenIddictApplicationManager appManager) : ControllerBase
{
    /// <summary>Authorization endpoint — GET shows the consent page, POST processes the decision.</summary>
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict request not found.");

        if (HttpMethods.IsPost(HttpContext.Request.Method))
        {
            if (Request.Form["decision"] != "allow")
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.AccessDenied,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "The owner denied the request.",
                    }));
            }

            var identity = new ClaimsIdentity(
                authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.SetClaim(Claims.Subject, "mdex-owner");
            identity.SetScopes(request.GetScopes());

            return SignIn(new ClaimsPrincipal(identity),
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        // GET — show the consent form
        var app = await appManager.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException($"Unknown client: {request.ClientId}");
        var displayName = await appManager.GetDisplayNameAsync(app) ?? request.ClientId!;

        return Content(
            BuildConsentHtml(displayName, request.GetScopes(), Request.QueryString.Value ?? ""),
            "text/html");
    }

    static string BuildConsentHtml(string appName, IEnumerable<string> scopes, string queryString)
    {
        var scopeList = System.Net.WebUtility.HtmlEncode(string.Join(", ", scopes));
        var name = System.Net.WebUtility.HtmlEncode(appName);

        // POST body must contain all original OAuth params — OpenIddict reads only the body on POST
        var hiddenFields = string.Join("\n  ", queryString.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .Select(p => $"<input type=\"hidden\" name=\"{System.Net.WebUtility.HtmlEncode(Uri.UnescapeDataString(p[0]))}\" " +
                         $"value=\"{System.Net.WebUtility.HtmlEncode(Uri.UnescapeDataString(p[1]))}\">"));

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>mdex — Authorize</title></head>
            <body style="font-family:sans-serif;max-width:480px;margin:4rem auto;padding:1rem">
              <h1 style="font-size:1.2rem">Authorize {name}</h1>
              <p>Scopes requested: <strong>{scopeList}</strong></p>
              <form method="post" action="/connect/authorize">
                {hiddenFields}
                <button name="decision" value="allow"
                  style="background:#2563eb;color:#fff;border:none;padding:.5rem 1.5rem;cursor:pointer">
                  Allow
                </button>
                <button name="decision" value="deny"
                  style="background:#dc2626;color:#fff;border:none;padding:.5rem 1.5rem;cursor:pointer;margin-left:.5rem">
                  Deny
                </button>
              </form>
            </body>
            </html>
            """;
    }
}
