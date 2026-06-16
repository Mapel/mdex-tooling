using System.Text;
using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Fs;

public sealed class WriteFileCommand : ICommand
{
    public string Name => "write-file";
    public string Usage => "write-file <path> (--content <text> | stdin)";
    public string Summary => "Create or overwrite a file (convention escape hatch).";
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
