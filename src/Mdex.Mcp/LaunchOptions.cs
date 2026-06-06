using Mdex.Core.Security;

namespace Mdex.Mcp;

public sealed class LaunchOptionsException(string message) : Exception(message);

/// <summary>Parsed launch flags for the local stdio host. <c>--role</c> is required (fail-closed):
/// permission must never be implicit, so the host refuses to start without it (design §4).</summary>
public sealed record LaunchOptions(Role Role, string? Root)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? roleArg = null, rootArg = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--role" && i + 1 < args.Length) roleArg = args[++i];
            else if (args[i] == "--root" && i + 1 < args.Length) rootArg = args[++i];
        }

        if (roleArg is null)
            throw new LaunchOptionsException(
                "missing required --role <read|write-intent|fullwrite> (no default — set it explicitly)");
        if (!Roles.TryParse(roleArg, out var role))
            throw new LaunchOptionsException($"invalid --role '{roleArg}' (expected read|write-intent|fullwrite)");

        return new LaunchOptions(role, rootArg);
    }
}
