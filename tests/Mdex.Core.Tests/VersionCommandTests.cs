using System.Text.Json;
using Mdex.Core.Commands;

namespace Mdex.Core.Tests;

public class VersionCommandTests
{
    [Theory]
    [InlineData("0.2.0+9f3c1ab", "0.2.0")]
    [InlineData("0.2.0", "0.2.0")]
    [InlineData("1.0.0-rc.1+build.5", "1.0.0-rc.1")]
    public void Strips_build_metadata(string raw, string expected)
        => Assert.Equal(expected, VersionCommand.StripBuildMetadata(raw));

    [Fact]
    public void Reports_a_real_version_and_keeps_data_and_text_in_sync()
    {
        // NeedsVault is false, so the context is never touched.
        var result = new VersionCommand().Run([], null!);

        Assert.StartsWith("mdex-cli ", result.HumanText);
        var version = result.HumanText["mdex-cli ".Length..];

        Assert.Matches(@"^\d+\.\d+\.\d+", version);
        Assert.DoesNotContain("+", version);
        Assert.Contains($"\"version\":\"{version}\"", JsonSerializer.Serialize(result.Data));
    }
}
