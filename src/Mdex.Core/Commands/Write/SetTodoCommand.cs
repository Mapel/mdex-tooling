using System.Text;
using System.Text.RegularExpressions;
using Mdex.Core.Cli;
using Mdex.Core.Vault;

namespace Mdex.Core.Commands.Write;

/// <summary>
/// Set a checkbox's status by text-match (not line number, not synthetic id — both are fragile
/// across the read→write gap). Ambiguity (0 or 2+ matches) is reported, not guessed: word-identical
/// todos in the same note are an accepted residual edge — the KI flags, the human resolves.
/// </summary>
public sealed class SetTodoCommand : ICommand
{
    public string Name => "set-todo";
    public string Usage => "set-todo <slug|path> --match <text> --status open|done";
    public string Summary => "Set a todo checkbox's status by text-match (fails on 0 or 2+ matches).";

    static readonly Regex Checkbox = new(@"^(\s*[-*]\s+\[)( |x|X)(\]\s*)(.*)$");

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var key = reader.Positional(0) ?? throw new CommandException("set-todo needs a slug or path");
        var match = reader.Flag("--match") ?? throw new CommandException("set-todo needs --match <text>");
        var status = (reader.Flag("--status") ?? throw new CommandException("set-todo needs --status open|done")).ToLowerInvariant();
        if (status is not ("open" or "done")) throw new CommandException("--status must be open or done");

        var note = Resolver.Single(ctx, key, $"note not found: {key}");
        var lines = File.ReadAllText(note.Path).Replace("\r\n", "\n").Split('\n');

        var hits = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = Checkbox.Match(lines[i]);
            if (m.Success && m.Groups[4].Value.Contains(match, StringComparison.OrdinalIgnoreCase))
                hits.Add(i);
        }

        if (hits.Count == 0) throw new CommandException($"no todo matching '{match}' in {note.RelativePath}");
        if (hits.Count > 1)
            throw new CommandException(
                $"'{match}' matches {hits.Count} todos in {note.RelativePath} — refine the text",
                new { candidates = hits.Select(i => lines[i].Trim()).ToArray() });

        var idx = hits[0];
        var box = status == "done" ? "x" : " ";
        lines[idx] = Checkbox.Replace(lines[idx], $"${{1}}{box}${{3}}${{4}}");

        var text = string.Join('\n', lines);
        text = FrontmatterEditor.SetField(text, "modified", DateTime.Now.ToString("yyyy-MM-dd"));
        File.WriteAllText(note.Path, text, new UTF8Encoding(false));

        return CommandResult.Of(new { path = note.RelativePath, line = lines[idx].Trim(), status },
            $"✓ {note.RelativePath}  {lines[idx].Trim()}");
    }
}
