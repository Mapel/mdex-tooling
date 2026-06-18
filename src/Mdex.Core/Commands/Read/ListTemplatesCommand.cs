using Mdex.Core.Cli;

namespace Mdex.Core.Commands.Read;

/// <summary>
/// Lists the available body templates (name + content). Lets a KI discover the valid
/// <c>bodyTemplate</c> values for <c>create</c> and read a note-type's convention so it can follow
/// it when writing the body. Read-only.
/// </summary>
public sealed class ListTemplatesCommand : ICommand
{
    public string Name => "list-templates";
    public string Usage => "list-templates";
    public string Summary => "List available body templates (name + content).";

    public CommandResult Run(string[] args, CliContext ctx)
    {
        var templates = ctx.Templates.AvailableBodyTemplates()
            .Select(name => new { name, content = ctx.Templates.ReadBodyTemplate(name) })
            .ToArray();
        return CommandResult.Of(new { templates },
            templates.Length == 0 ? "no body templates" : $"{templates.Length} body template(s)");
    }
}
