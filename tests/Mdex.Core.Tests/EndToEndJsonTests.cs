using System.Diagnostics;
using System.Text.Json;
namespace Mdex.Core.Tests;

public class EndToEndJsonTests
{
    // Walk up from the test output dir to the repo root (the folder holding Mdex.sln),
    // then point at the exe project. Robust to build-output depth — no magic "../.." count.
    static string CliDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Mdex.sln")))
            dir = Path.GetDirectoryName(dir);
        if (dir is null)
            throw new InvalidOperationException("Could not locate Mdex.sln above the test output directory.");
        return Path.Combine(dir, "src", "Mdex.Cli");
    }

    [Fact]
    public void List_tags_json_envelope_is_valid()
    {
        using var v = new TestVault();
        v.WriteNote("zettel/a.md", "---\ntype: zettel\ntags: [x]\n---\n# A\n");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = CliDir(),
        };
        foreach (var a in new[] { "run", "--", "list-tags", "--root", v.Root, "--json" }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(60_000);

        using var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("list-tags", doc.RootElement.GetProperty("command").GetString());
    }
}
