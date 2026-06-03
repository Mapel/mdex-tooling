using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class ListCommand : ICommand
{
    public string Name => "list";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var sub = reader.Positional(0) ?? ".";
        var baseAbs = ctx.Guard.ResolveInside(sub);
        if (!Directory.Exists(baseAbs)) throw new CommandException($"not a directory: {sub}");

        var entries = new List<object>();
        var paths = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(baseAbs, "*", SearchOption.AllDirectories))
        {
            var rel = ctx.Guard.ToRelative(path);
            if (rel.StartsWith(".git/") || rel == ".git") continue;
            entries.Add(new { path = rel, kind = Directory.Exists(path) ? "dir" : "file" });
            paths.Add(rel);
        }
        var human = string.Join('\n', paths);
        return CommandResult.Of(new { root = ctx.Guard.ToRelative(baseAbs), entries }, human);
    }
}
