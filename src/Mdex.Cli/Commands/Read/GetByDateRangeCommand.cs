using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class GetByDateRangeCommand : ICommand
{
    public string Name => "get-by-date-range";
    public string Usage => "get-by-date-range (--from <date> | --to <date>) [--field created|modified]";
    public string Summary => "Notes in a date range; one bound may be omitted (open-ended).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var from = reader.Flag("--from");
        var to = reader.Flag("--to");
        if (from is null && to is null)
            throw new CommandException("get-by-date-range needs --from or --to");
        var field = reader.Flag("--field") ?? "created";

        var notes = ctx.Repo.LoadAll().Where(n =>
        {
            var value = field == "modified" ? n.Frontmatter.Modified : n.Frontmatter.Created;
            if (string.IsNullOrEmpty(value)) return false;          // undated notes are out of range
            var day = value.Length >= 10 ? value[..10] : value; // YYYY-MM-DD prefix
            return (from is null || string.CompareOrdinal(day, from) >= 0)   // open-ended start
                && (to is null || string.CompareOrdinal(day, to) <= 0);      // open-ended end
        }).Select(n => n.ToSummary()).ToList();
        return Summaries.Result(notes);
    }
}
