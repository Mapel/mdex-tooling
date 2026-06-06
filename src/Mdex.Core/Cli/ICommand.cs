using Mdex.Core.Vault;

namespace Mdex.Core.Cli;

/// <summary>Expected, user-facing failure. Becomes a clean error (exit 1 / json error), not a stack trace.</summary>
/// <remarks><paramref name="details"/> rides along in the JSON envelope's <c>details</c> field so
/// machine consumers get structured context (e.g. ambiguous-slug candidates) without parsing the message.</remarks>
public sealed class CommandException(string message, object? details = null) : Exception(message)
{
    public object? Details { get; } = details;
}

public sealed record CommandResult(object? Data, string HumanText)
{
    public static CommandResult Of(object? data, string humanText) => new(data, humanText);
}

public sealed class CliContext
{
    public required string VaultRoot { get; init; }
    public required bool Json { get; init; }
    public required NoteRepository Repo { get; init; }
    public required PathGuard Guard { get; init; }
    public required TemplateService Templates { get; init; }
}

public interface ICommand
{
    string Name { get; }
    bool NeedsVault => true;          // version/help override to false
    string Usage => Name;             // argument signature shown in --help
    string Summary => "";             // one-line description shown in --help
    CommandResult Run(string[] args, CliContext ctx);
}
