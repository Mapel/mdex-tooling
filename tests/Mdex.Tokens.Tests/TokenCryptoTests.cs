using Mdex.Tokens;
namespace Mdex.Tokens.Tests;

public class TokenCryptoTests
{
    [Fact]
    public void New_token_has_prefix_and_is_unique()
    {
        var a = TokenGenerator.New();
        var b = TokenGenerator.New();
        Assert.StartsWith("mdex_", a);
        Assert.NotEqual(a, b);
        Assert.True(a.Length > 20);
    }

    [Fact]
    public void Hash_is_stable_64_hex_and_differs_per_input()
    {
        var hash = TokenHasher.Hash("mdex_abc");
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
        Assert.Equal(hash, TokenHasher.Hash("mdex_abc"));
        Assert.NotEqual(hash, TokenHasher.Hash("mdex_abd"));
    }
}
