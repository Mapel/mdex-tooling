namespace Mdex.Tokens;

/// <summary>One stored credential. <see cref="Role"/> is the wire string ("read"/"write-intent"/"fullwrite").</summary>
public sealed record TokenEntry(string Hash, string Role, string Label, string Created);
