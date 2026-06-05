using Mdex.Cli.Cli;
namespace Mdex.Cli.Tests;

public class ArgReaderTests
{
    [Fact]
    public void Equals_form_keeps_leading_dashes_in_value()
    {
        var r = new ArgReader(new[] { "--content=--- a dashy line" });
        Assert.Equal("--- a dashy line", r.Flag("--content"));
    }

    [Fact]
    public void Equals_form_allows_empty_value()
    {
        var r = new ArgReader(new[] { "--content=" });
        Assert.Equal("", r.Flag("--content"));
    }

    [Fact]
    public void Equals_form_splits_on_first_equals_only()
    {
        var r = new ArgReader(new[] { "--content=a=b=c" });
        Assert.Equal("a=b=c", r.Flag("--content"));
    }

    [Fact]
    public void End_of_options_makes_dashy_token_positional()
    {
        var r = new ArgReader(new[] { "--", "--weird" });
        Assert.Equal("--weird", r.Positional(0));
        Assert.False(r.Has("--weird"));
    }

    [Fact]
    public void Flags_before_separator_then_positionals_after()
    {
        var r = new ArgReader(new[] { "--maxhops=3", "--recursive", "--", "--weird-slug" });
        Assert.Equal("3", r.Flag("--maxhops"));
        Assert.True(r.Has("--recursive"));
        Assert.Equal("--weird-slug", r.Positional(0));
    }

    [Fact]
    public void Space_form_and_boolean_flag_still_work()
    {
        var r = new ArgReader(new[] { "--content", "text", "--recursive" });
        Assert.Equal("text", r.Flag("--content"));
        Assert.True(r.Has("--recursive"));
        Assert.Null(r.Flag("--recursive"));
    }
}
