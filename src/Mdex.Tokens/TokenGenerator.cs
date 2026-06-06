using System.Security.Cryptography;

namespace Mdex.Tokens;

public static class TokenGenerator
{
    /// <summary>A fresh opaque token: <c>mdex_</c> + url-safe base64 of 32 random bytes (256-bit entropy).</summary>
    public static string New()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "mdex_" + Base64Url(bytes);
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
