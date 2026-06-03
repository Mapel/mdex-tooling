namespace Mdex.Cli.Cli;

/// <summary>Expected, user-facing failure. Becomes a clean error (exit 1 / json error), not a stack trace.</summary>
public sealed class CommandException(string message) : Exception(message);
