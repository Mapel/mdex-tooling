using Mdex.Core.Cli;
using Mdex.Core.Security;

namespace Mdex.Mcp.Tools;

/// <summary>
/// The single in-process seam every MCP tool calls. Pins the vault root, runs a verb through
/// <see cref="CommandEngine"/>, and returns the operation's <c>Data</c> object (the MCP SDK
/// serialises it — no intermediate JSON). Error mapping is the tool layer's D-4 boundary:
/// a <see cref="CommandException"/> surfaces as a user-facing <see cref="MdexToolException"/>
/// (message + details); any other exception is logged and replaced by a generic message so no
/// stack trace or absolute path ever reaches the (untrusted) AI caller.
/// </summary>
public sealed class MdexBackend(CommandEngine engine, string? vaultRoot, Action<string>? logError = null)
{
    public object? Call(string verb, params string[] args)
    {
        try
        {
            return engine.Run(verb, args, vaultRoot).Data;
        }
        catch (CommandException ex)
        {
            throw new MdexToolException(ex.Message, ex.Details);
        }
        catch (Exception ex)
        {
            (logError ?? (_ => { }))($"[mdex] unexpected error in '{verb}': {ex}");
            throw new MdexToolException("internal error");
        }
    }
}
