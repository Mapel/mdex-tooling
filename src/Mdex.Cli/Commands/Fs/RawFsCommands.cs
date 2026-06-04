using System.Text;
using Mdex.Cli.Cli;

namespace Mdex.Cli.Commands.Fs;

public sealed class WriteFileCommand : ICommand
{
    public string Name => "write-file";
    public string Usage => "write-file <path> (--content <text> | stdin)";
    public string Summary => "Create or overwrite a file.";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("write-file needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.WriteAllText(abs, reader.Content(), new UTF8Encoding(false));
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ wrote {path}");
    }
}

public sealed class AppendFileCommand : ICommand
{
    public string Name => "append-file";
    public string Usage => "append-file <path> (--content <text> | stdin)";
    public string Summary => "Append to a file.";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("append-file needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.AppendAllText(abs, reader.Content(), new UTF8Encoding(false));
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ appended {path}");
    }
}

public sealed class MoveCommand : ICommand
{
    public string Name => "move";
    public string Usage => "move <from> <to>";
    public string Summary => "Move a file or directory (no overwrite).";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var from = reader.Positional(0) ?? throw new CommandException("move needs <from> <to>");
        var to = reader.Positional(1) ?? throw new CommandException("move needs <from> <to>");
        var fromAbs = ctx.Guard.ResolveInside(from);
        var toAbs = ctx.Guard.ResolveInside(to);
        if (!File.Exists(fromAbs) && !Directory.Exists(fromAbs)) throw new CommandException($"not found: {from}");
        Directory.CreateDirectory(Path.GetDirectoryName(toAbs)!);
        if (Directory.Exists(fromAbs)) Directory.Move(fromAbs, toAbs);
        else File.Move(fromAbs, toAbs, overwrite: false);
        return CommandResult.Of(new { from = ctx.Guard.ToRelative(fromAbs), to = ctx.Guard.ToRelative(toAbs) },
            $"✓ {from} → {to}");
    }
}

public sealed class MkdirCommand : ICommand
{
    public string Name => "mkdir";
    public string Usage => "mkdir <path>";
    public string Summary => "Create a directory.";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("mkdir needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        Directory.CreateDirectory(abs);
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ mkdir {path}");
    }
}

public sealed class DeleteCommand : ICommand
{
    public string Name => "delete";
    public string Usage => "delete <path>";
    public string Summary => "Delete a file or directory (recursive).";
    public CommandResult Run(string[] args, CliContext ctx)
    {
        var reader = new ArgReader(args);
        var path = reader.Positional(0) ?? throw new CommandException("delete needs a path");
        var abs = ctx.Guard.ResolveInside(path);
        if (Directory.Exists(abs)) Directory.Delete(abs, recursive: true);
        else if (File.Exists(abs)) File.Delete(abs);
        else throw new CommandException($"not found: {path}");
        return CommandResult.Of(new { path = ctx.Guard.ToRelative(abs) }, $"✓ deleted {path}");
    }
}
