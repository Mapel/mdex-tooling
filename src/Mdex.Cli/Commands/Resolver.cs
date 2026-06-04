using Mdex.Cli.Cli;
using Mdex.Cli.Vault;

namespace Mdex.Cli.Commands;

/// <summary>
/// Resolves a slug-or-path argument to exactly one note. Failures are user-facing:
/// nothing found, or a bare slug that is shared by several notes (in which case the
/// caller must disambiguate with an explicit path). Used by both read and write
/// commands so neither silently acts on the wrong file.
/// </summary>
public static class Resolver
{
    public static Note Single(CliContext ctx, string key, string notFound)
    {
        var matches = ctx.Repo.FindBySlugOrPath(key);
        return matches.Count switch
        {
            0 => throw new CommandException(notFound),
            1 => matches[0],
            _ => throw new CommandException(Ambiguous(key, matches),
                    new { candidates = matches.Select(m => m.RelativePath).ToArray() }),
        };
    }

    static string Ambiguous(string key, IReadOnlyList<Note> matches) =>
        $"'{key}' is ambiguous — {matches.Count} notes share this slug. Pass an explicit path:\n"
        + string.Join('\n', matches.Select(m => $"  - {m.RelativePath}"));
}
