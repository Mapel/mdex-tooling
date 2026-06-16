using System.Diagnostics;
using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Write;

/// <summary>
/// Atomic <c>add -A → commit → pull --rebase → push</c> for KI-initiated persistence (path 2).
/// One operation guards against push-rejections on multi-device sync. Local changes are committed
/// <em>before</em> the rebase so the working tree is clean when pulling (a dirty-tree rebase would
/// abort); a real merge conflict during the rebase fails hard and returns it (KI/human resolves).
/// Semantic message, no <c>auto:</c> prefix — that prefix belongs to the server-side sync script
/// (path 1).
/// </summary>
public sealed class CommitAndPushCommand : ICommand
{
    public string Name => "commit-and-push";
    public string Usage => "commit-and-push --message <msg>";
    public string Summary => "Atomically commit all changes, rebase on the remote, and push.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var message = reader.Flag("--message");
        if (string.IsNullOrWhiteSpace(message)) throw new CommandException("commit-and-push needs --message");

        var root = ctx.VaultRoot;

        Git(root, "add -A");

        // Nothing staged → don't fail the sync; report and stop (no empty commit, no push).
        var (statusCode, porcelain, statusErr) = Run(root, "status --porcelain");
        if (statusCode != 0) throw new CommandException($"git status failed: {statusErr.Trim()}");
        if (porcelain.Trim().Length == 0)
            return CommandResult.Of(new { committed = false, message }, "nothing to commit");

        Git(root, $"commit -m {Quote(message!)}");
        Git(root, "pull --rebase");      // tree is clean now; throws with the conflict on a collision
        Git(root, "push");

        return CommandResult.Of(new { committed = true, message }, "✓ committed and pushed");
    }

    /// <summary>Run git; throw a user-facing CommandException carrying stderr on a non-zero exit.</summary>
    static void Git(string workDir, string args)
    {
        var (code, _, err) = Run(workDir, args);
        if (code != 0)
            throw new CommandException($"git {args} failed: {err.Trim()}");
    }

    static (int code, string stdout, string stderr) Run(string workDir, string args)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)
            ?? throw new CommandException("could not start git");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderr);
    }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
