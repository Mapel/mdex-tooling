using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Read;

public sealed class GetByDateRangeCommand : ICommand
{
    public string Name => "get-by-date-range";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var from = reader.Flag("--from") ?? throw new CommandException("get-by-date-range needs --from");
        var to = reader.Flag("--to") ?? throw new CommandException("get-by-date-range needs --to");
        var field = reader.Flag("--field") ?? "created";

        var notes = ctx.Repo.LoadAll().Where(n =>
        {
            var value = field == "modified" ? n.Frontmatter.Modified : n.Frontmatter.Created;
            if (string.IsNullOrEmpty(value)) return false;
            var day = value.Length >= 10 ? value[..10] : value; // YYYY-MM-DD prefix
            return string.CompareOrdinal(day, from) >= 0 && string.CompareOrdinal(day, to) <= 0;
        }).Select(n => n.ToSummary()).ToList();
        return Summaries.Result(notes);
    }
}
