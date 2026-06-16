using System.Diagnostics;
using Mdex.Core.Cli;
using Mdex.Core.Commands.Write;
using Mdex.Core.Vault;
namespace Mdex.Core.Tests;

public class CommitAndPushTests
{
    static CliContext Ctx(string root)
    {
        var g = new PathGuard(root);
        return new CliContext { VaultRoot = root,
            Repo = new NoteRepository(root, g), Guard = g, Templates = new TemplateService(root) };
    }

    static (int code, string outp, string err) Git(string dir, string args)
    {
        var psi = new ProcessStartInfo("git")
        {
            Arguments = args, WorkingDirectory = dir,
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o, e);
    }

    static bool GitAvailable()
    {
        try { return Git(Path.GetTempPath(), "--version").code == 0; }
        catch { return false; }
    }

    [Fact]
    public void Commits_and_pushes_to_the_remote()
    {
        if (!GitAvailable()) return;   // environment without git — nothing to assert

        var tmp = Path.Combine(Path.GetTempPath(), "mdex-cap-" + Guid.NewGuid().ToString("N"));
        var remote = Path.Combine(tmp, "remote.git");
        var work = Path.Combine(tmp, "work");
        Directory.CreateDirectory(remote);
        Directory.CreateDirectory(work);
        try
        {
            Git(remote, "init --bare -b main");
            Git(work, "init -b main");
            Git(work, "config user.email test@example.com");
            Git(work, "config user.name Test");
            Git(work, $"remote add origin \"{remote}\"");
            File.WriteAllText(Path.Combine(work, "seed.md"), "seed\n");
            Git(work, "add -A");
            Git(work, "commit -m initial");
            Git(work, "push -u origin main");

            // New change made by the "KI"; commit-and-push should sync it to the remote.
            File.WriteAllText(Path.Combine(work, "note.md"), "hello\n");
            var data = new CommitAndPushCommand().Run(new[] { "--message", "add note" }, Ctx(work)).Data();
            Assert.True(data.GetProperty("committed").GetBoolean());

            var log = Git(remote, "log --oneline");
            Assert.Contains("add note", log.outp);
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    [Fact]
    public void Nothing_to_commit_is_reported_not_failed()
    {
        if (!GitAvailable()) return;

        var work = Path.Combine(Path.GetTempPath(), "mdex-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            Git(work, "init -b main");
            Git(work, "config user.email test@example.com");
            Git(work, "config user.name Test");
            File.WriteAllText(Path.Combine(work, "seed.md"), "seed\n");
            Git(work, "add -A");
            Git(work, "commit -m initial");

            var data = new CommitAndPushCommand().Run(new[] { "--message", "noop" }, Ctx(work)).Data();
            Assert.False(data.GetProperty("committed").GetBoolean());
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    [Fact]
    public void Requires_a_message()
    {
        var work = Path.Combine(Path.GetTempPath(), "mdex-cap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            Assert.Throws<CommandException>(() =>
                new CommitAndPushCommand().Run(Array.Empty<string>(), Ctx(work)));
        }
        finally { try { Directory.Delete(work, true); } catch { } }
    }
}
