using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands.Read;

public static class Summaries
{
    public static CommandResult Result(List<NoteSummary> notes)
    {
        var human = notes.Count == 0
            ? "No matches."
            : string.Join('\n', notes.Select(n => $"{n.Path}  [{n.Type}]  {n.Title}"));
        return CommandResult.Of(new { count = notes.Count, notes }, human);
    }
}
