using Xunit;
using FluentAssertions;
using CoaiBench;
using CoaiBench.Cli;
using CoaiBench.Model;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// The bench runs against the machine the person actually has.
/// </summary>
/// <remarks>
/// Asked for in as many words: run against the really installed MCP, and let the rounds show up in
/// the panel. Both are the same principle the vendor fix established — a bench that supplies its own
/// environment measures a machine nobody has.
/// </remarks>
public sealed class RealEnvironmentTests
{
    private static readonly Case Plan = new("plan-a", "todo/PLAN_a.md");

    private static readonly VendorConfig[] Configured = [new("codex", "codex", "gpt-5.6-luna")];

    private static readonly IReadOnlyDictionary<string, string> NoSettings =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static Bench BenchOf(Options options) =>
        new(options, [Plan], Configured, NoSettings, _ => { });

    [Fact]
    public void TheServerDefaultsToTheINSTALLEDOne()
    {
        var chosen = Program.WhichServer(new Options());

        chosen.Should().Be(Program.InstalledServer);
        chosen.Should().Contain("remsoftdev.connect-other-ais", "that is the binary the panel spawns");
    }

    [Fact]
    public void AndAnExplicitOneStillWins() =>
        Program.WhichServer(new Options { Executable = "C:/built/coai-mcp.exe" })
            .Should().Be("C:/built/coai-mcp.exe");

    [Fact]
    public void RoundsGoToTheREALDataDirectory_SoThePanelShowsThem()
    {
        // The whole of "and it should appear here": the panel reads one directory, and a bench that
        // writes somewhere else is invisible while it runs.
        var dir = BenchOf(new Options { Arms = ["codex"], OutDir = "C:/out" }).DataDirOf(Plan, "codex", 1, 1);

        dir.Should().Be(Bench.RealDataDir);
    }

    [Fact]
    public void IsolationIsAChoice_ForComparingThingsThatMustNotSeeEachOther()
    {
        var dir = BenchOf(new Options { Arms = ["codex"], OutDir = "C:/out", Isolate = true })
            .DataDirOf(Plan, "codex", 1, 1);

        dir.Should().StartWith("C:/out");
        dir.Should().NotBe(Bench.RealDataDir);
    }

    [Fact]
    public void IsolatedAndParallel_ShareOneDirectory_BecauseTheInterferenceIsThePoint()
    {
        var options = new Options { Arms = ["codex"], OutDir = "C:/out", Isolate = true, Parallel = 5 };

        BenchOf(options).DataDirOf(Plan, "codex", 1, 1)
            .Should().Be(BenchOf(options).DataDirOf(Plan, "codex", 2, 4));
    }

    [Fact]
    public void ASwitchDoesNotSwallowTheFlagAfterIt()
    {
        // `--isolate` takes no value, and a parser that demands one for everything ate the next flag.
        var (options, refusal) = OptionsParser.Parse(["run", "--isolate", "--repeat", "3"]);

        options.Should().NotBeNull(refusal);
        options!.Isolate.Should().BeTrue();
        options.Repeat.Should().Be(3);
    }
}
