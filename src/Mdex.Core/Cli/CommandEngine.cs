using Mdex.Core.Commands;
using Mdex.Core.Vault;

namespace Mdex.Core.Cli;

/// <summary>
/// The single in-process entry point for running a verb. The CLI (Program.cs) and —
/// in later phases — the MCP tool layer both go through here, so there is exactly one
/// implementation of "resolve vault, build context, dispatch". Returns the operation's
/// <see cref="CommandResult"/> (the Data object); the caller decides how to render it.
/// </summary>
public sealed class CommandEngine
{
    readonly CommandRegistry _registry;

    public CommandEngine(CommandRegistry registry) => _registry = registry;

    /// <summary>Engine over the standard command set.</summary>
    public static CommandEngine Default() => new(CommandFactory.BuildRegistry());

    /// <summary>Registered commands in registration order (for help/tool generation).</summary>
    public IReadOnlyList<ICommand> Commands => _registry.All;

    /// <summary>Look up a verb without running it (null if unknown).</summary>
    public ICommand? Find(string verb) => _registry.Get(verb);

    /// <summary>
    /// Run <paramref name="verb"/> with <paramref name="args"/>. Resolves the vault from
    /// <paramref name="rootFlag"/> (else MDEX_ROOT/config) for commands that need it.
    /// Throws <see cref="CommandException"/> for an unknown verb or a user-facing failure;
    /// other exceptions propagate (callers sanitise — CLI prints, MCP layer maps).
    /// </summary>
    public CommandResult Run(string verb, string[] args, string? rootFlag)
    {
        var command = _registry.Get(verb)
            ?? throw new CommandException($"unknown command '{verb}'");
        var ctx = BuildContext(rootFlag, command.NeedsVault);
        return command.Run(args, ctx);
    }

    /// <summary>Build the execution context; vault services are only wired when needed.</summary>
    public CliContext BuildContext(string? rootFlag, bool needsVault)
    {
        if (!needsVault)
            return new CliContext { VaultRoot = "", Repo = null!, Guard = null!, Templates = null! };

        var root = VaultResolver.Resolve(rootFlag);
        var guard = new PathGuard(root);
        return new CliContext
        {
            VaultRoot = root,
            Repo = new NoteRepository(root, guard),
            Guard = guard,
            Templates = new TemplateService(root),
        };
    }
}
