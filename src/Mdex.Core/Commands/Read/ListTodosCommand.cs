using System.Text.RegularExpressions;
using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

/// <summary>
/// Aggregates checkbox items (<c>- [ ]</c> / <c>- [x]</c>) across all notes — todos are
/// embedded checklists, not a dedicated type. Default lists open items; <c>--in</c> scopes
/// the search to a vault-relative subtree.
/// </summary>
public sealed class ListTodosCommand : ICommand
{
    public string Name => "list-todos";
    public string Usage => "list-todos [--status open|done|all] [--in <path>]";
    public string Summary => "Collect checkboxes (- [ ] / - [x]) from all notes; default open only.";

    static readonly Regex Checkbox = new(@"^\s*[-*]\s+\[( |x|X)\]\s*(.*)$");

    sealed record Todo(string Text, string Status, string NotePath, string NoteSlug);

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var status = (reader.Flag("--status") ?? "open").ToLowerInvariant();
        if (status is not ("open" or "done" or "all"))
            throw new CommandException("--status must be open, done, or all");
        var scope = reader.Flag("--in");

        var found = new List<Todo>();
        foreach (var note in ctx.Repo.LoadAll())
        {
            if (scope is not null &&
                !note.RelativePath.StartsWith(scope.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(note.RelativePath, scope, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var line in note.Body.Replace("\r\n", "\n").Split('\n'))
            {
                var m = Checkbox.Match(line);
                if (!m.Success) continue;
                var itemStatus = m.Groups[1].Value is "x" or "X" ? "done" : "open";
                if (status != "all" && status != itemStatus) continue;
                found.Add(new Todo(m.Groups[2].Value.Trim(), itemStatus, note.RelativePath, note.Slug));
            }
        }

        var todos = found.Select(t => new { text = t.Text, status = t.Status, notePath = t.NotePath, noteSlug = t.NoteSlug }).ToList();
        var human = found.Count == 0
            ? "No todos."
            : string.Join('\n', found.Select(t => $"[{(t.Status == "done" ? "x" : " ")}]  {t.NotePath}  {t.Text}"));
        return CommandResult.Of(new { count = todos.Count, todos }, human);
    }
}
