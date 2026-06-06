using System.Security.Cryptography;
using System.Text;

namespace Mdex.Tokens;

public static class TokenHasher
{
    /// <summary>Lower-case hex SHA-256 of the raw token. Only the hash is ever stored.</summary>
    public static string Hash(string rawToken)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexStringLower(digest);
    }
}
