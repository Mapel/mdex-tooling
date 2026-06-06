using Mdex.Core.Commands;
namespace Mdex.Core.Tests;

public class HelpMetadataTests
{
    // Guards the --help / README contract: every command must carry its own
    // documentation so the generated help stays complete as commands are added.
    [Fact]
    public void Every_command_has_summary_and_consistent_usage()
    {
        foreach (var c in CommandFactory.BuildRegistry().All)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Summary), $"{c.Name} is missing a Summary");
            Assert.StartsWith(c.Name, c.Usage);
        }
    }
}
