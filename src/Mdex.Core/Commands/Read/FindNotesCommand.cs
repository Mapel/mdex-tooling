using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

/// <summary>
/// Unified note filter (merges the former get-by-tag / get-by-type / get-by-date-range).
/// Different filter dimensions are always AND; `--match` only governs how multiple `--tag`
/// values combine (all = default, any). A date range is inherently AND on its bounds.
/// </summary>
public sealed class FindNotesCommand : ICommand
{
    public string Name => "find-notes";
    public string Usage => "find-notes [--tag X...] [--type T] [--from D] [--to D] [--field created|modified] [--match all|any]";
    public string Summary => "Find notes by tag, type, and/or date range (filters AND; --match governs tags).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var tags = reader.Multi("--tag");
        var type = reader.Flag("--type");
        var from = reader.Flag("--from");
        var to = reader.Flag("--to");
        var field = reader.Flag("--field") ?? "created";
        var matchAny = string.Equals(reader.Flag("--match"), "any", StringComparison.OrdinalIgnoreCase);

        if (tags.Count == 0 && type is null && from is null && to is null)
            throw new CommandException("find-notes needs at least one filter (--tag, --type, --from, or --to)");

        var notes = ctx.Repo.LoadAll().Where(n =>
        {
            if (type is not null && !string.Equals(n.Frontmatter.Type, type, StringComparison.OrdinalIgnoreCase))
                return false;

            if (tags.Count > 0)
            {
                bool TagHit(string t) => n.Frontmatter.Tags.Contains(t, StringComparer.OrdinalIgnoreCase);
                if (!(matchAny ? tags.Any(TagHit) : tags.All(TagHit))) return false;
            }

            if (from is not null || to is not null)
            {
                var value = field == "modified" ? n.Frontmatter.Modified : n.Frontmatter.Created;
                if (string.IsNullOrEmpty(value)) return false;          // undated notes are out of range
                var day = value.Length >= 10 ? value[..10] : value;     // YYYY-MM-DD prefix
                if (from is not null && string.CompareOrdinal(day, from) < 0) return false;
                if (to is not null && string.CompareOrdinal(day, to) > 0) return false;
            }

            return true;
        }).Select(n => n.ToSummary()).ToList();

        return Summaries.Result(notes);
    }
}
