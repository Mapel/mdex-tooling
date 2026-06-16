using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

public sealed class ListCommand : ICommand
{
    public string Name => "list";
    public string Usage => "list [path] [--depth N|all]";
    public string Summary => "File/dir tree under an optional subpath; default depth 1 (direct children).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var sub = reader.Positional(0) ?? ".";
        var depth = ParseDepth(reader.Flag("--depth"));
        var baseAbs = ctx.Guard.ResolveInsideOrRoot(sub);
        if (!Directory.Exists(baseAbs)) throw new CommandException($"not a directory: {sub}");

        var baseRel = ctx.Guard.ToRelative(baseAbs);
        var entries = new List<object>();
        var paths = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(baseAbs, "*", SearchOption.AllDirectories))
        {
            var rel = ctx.Guard.ToRelative(path);
            if (rel.StartsWith(".git/") || rel == ".git") continue;
            if (DepthFrom(baseRel, rel) > depth) continue;
            entries.Add(new { path = rel, kind = Directory.Exists(path) ? "dir" : "file" });
            paths.Add(rel);
        }
        var human = string.Join('\n', paths);
        return CommandResult.Of(new { root = baseRel, entries }, human);
    }

    // "all" → unlimited; integer ≥ 1; null/absent → 1.
    static int ParseDepth(string? flag)
    {
        if (flag is null) return 1;
        if (string.Equals(flag, "all", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
        if (int.TryParse(flag, out var n) && n >= 1) return n;
        throw new CommandException("--depth must be a positive integer or 'all'");
    }

    // Levels of the entry below the base dir: a direct child is depth 1.
    static int DepthFrom(string baseRel, string entryRel)
    {
        var tail = baseRel == "."
            ? entryRel
            : entryRel.Substring(baseRel.Length).TrimStart('/');
        return tail.Count(c => c == '/') + 1;
    }
}
