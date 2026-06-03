using Mdex.Cli.Vault;

namespace Mdex.Cli.Cli;

/// <summary>Expected, user-facing failure. Becomes a clean error (exit 1 / json error), not a stack trace.</summary>
public sealed class CommandException(string message) : Exception(message);

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
    CommandResult Run(string[] args, CliContext ctx);
}
