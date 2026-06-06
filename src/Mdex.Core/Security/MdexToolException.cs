namespace Mdex.Core.Security;

/// <summary>A user-facing tool error: a sanitised message (safe to return to the AI client)
/// plus optional structured details (e.g. candidate slugs). Thrown by <see cref="ToolAuthorizer"/>
/// and by the tool backend; never carries a stack trace or an absolute path.</summary>
public sealed class MdexToolException(string message, object? details = null) : Exception(message)
{
    public object? Details { get; } = details;
}
