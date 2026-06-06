using Mdex.Core.Security;

namespace Mdex.Tokens;

/// <summary>Admin operations over a <see cref="TokenStore"/>. Used by <c>mdex-token</c>.</summary>
public sealed class TokenAdmin(TokenStore store)
{
    /// <summary>Creates a token, stores its hash, and returns the raw token (show once).</summary>
    public string New(Role role, string label)
    {
        var raw = TokenGenerator.New();
        store.Add(new TokenEntry(TokenHasher.Hash(raw), Roles.ToWire(role), label,
            DateTime.Now.ToString("yyyy-MM-dd")));
        return raw;
    }

    public IReadOnlyList<TokenEntry> List() => store.Entries;

    public bool Revoke(string label) => store.Remove(label);
}
