using System.Buffers.Text;
using System.Security.Cryptography;

namespace Mdex.Tokens;

public static class TokenGenerator
{
    /// <summary>A fresh opaque token: <c>mdex_</c> + url-safe base64 of 32 random bytes (256-bit entropy).</summary>
    public static string New() => "mdex_" + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
}
