using System.Reflection;
using Mdex.Core.Cli;
using Mdex.Core.Commands.Read;
using Mdex.Core.Commands.Write;
using Mdex.Core.Commands.Fs;

namespace Mdex.Core.Commands;

public static class CommandFactory
{
    public static CommandRegistry BuildRegistry()
    {
        var r = new CommandRegistry();
        r.Add(new VersionCommand());

        // Read commands
        r.Add(new SearchCommand());
        r.Add(new ReadNoteCommand());
        r.Add(new FindNotesCommand());
        r.Add(new ListTagsCommand());
        r.Add(new ListCommand());
        r.Add(new FindRelatedCommand());
        r.Add(new ListTodosCommand());
        r.Add(new ListTemplatesCommand());

        // Write / intent commands (convention-preserving)
        r.Add(new CaptureInboxCommand());
        r.Add(new CreateCommand());
        r.Add(new AppendToNoteCommand());
        r.Add(new AppendToLogCommand());
        r.Add(new PromoteInboxItemCommand());
        r.Add(new MoveCommand());
        r.Add(new ReplaceBodyCommand());
        r.Add(new SetTagsCommand());
        r.Add(new MkdirCommand());
        r.Add(new SetTodoCommand());
        r.Add(new CommitAndPushCommand());

        // Raw FS commands (guarded notausgang — convention-breaking)
        r.Add(new WriteFileCommand());
        r.Add(new DeleteCommand());

        return r;
    }
}

public sealed class VersionCommand : ICommand
{
    public string Name => "version";
    public bool NeedsVault => false;
    public string Summary => "Print the mdex-cli version.";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var version = Resolve();
        return CommandResult.Of(new { version }, $"mdex-cli {version}");
    }

    /// <summary>Read from the running assembly instead of a literal: the release workflow passes
    /// <c>-p:Version</c> from the tag, so a hardcoded string silently reports the wrong version
    /// after every release. InformationalVersion looks like "0.2.0+&lt;sha&gt;" – the build metadata
    /// after '+' is dropped. Entry assembly first (that is the actual tool), falling back to this
    /// one when there is none (test host).</summary>
    static string Resolve()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(VersionCommand).Assembly;
        return StripBuildMetadata(
            asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "0.0.0");
    }

    /// <summary>Drops the SemVer build metadata: "0.2.0+9f3c1ab" -> "0.2.0".</summary>
    public static string StripBuildMetadata(string raw)
    {
        var plus = raw.IndexOf('+');
        return plus < 0 ? raw : raw[..plus];
    }
}
