using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi launch leaves one Debug line with what the shim ended with (decision 75 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): on SUCCESS the exit code and the
/// report's <c>end</c> were visible nowhere — not in the log, the ledger or the round record — so a live check could not confirm exit 0 and <c>answered</c>.
/// </summary>
public sealed class AKimiLaunchIsLoggedTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-logged-");

    public void Dispose() => _dir.Dispose();

    private (IReviewerRuntime Runtime, ReviewerInvocation Invocation, ListSink Sink) Fixture()
    {
        var sink = new ListSink();
        var log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        var runtime = new KimiRuntime("kimi", HostKind.Windows, new KimiSeams(root => KimiPlaces.ForShim(root, _dir.At("bin")), () => (_dir.At("coai-mcp.exe"), [])), log);
        var invocation = runtime.Build(RoleCatalog.ArchitectureRole, "review", _dir.At("work"), _dir.At("s.json"), _dir,
            new ReviewerSettings("kimi") { OwnedScratch = _dir.At("round"), Timeout = TimeSpan.FromMinutes(1) });

        return (runtime, invocation, sink);
    }

    private static string OneLine(ListSink sink)
    {
        sink.Lines.Should().HaveCount(1, "one Debug line per Kimi launch: its exit, its end, its failure if any, whether the usage was captured");

        return sink.Lines[0];
    }

    [Fact]
    public void AnAnsweredLaunch_LogsItsExitAndEnd_AtDebug()
    {
        var (runtime, invocation, sink) = Fixture();

        runtime.RateLimited(invocation, KimiShimScripts.Answered("""{"findings": []}"""));

        OneLine(sink);
        var said = sink.Events.Single(e => e.Properties.ContainsKey("End"));
        said.Level.Should().Be(LogEventLevel.Debug);
        said.Properties["Exit"].ToString().Should().Be("0");
        said.Properties["End"].ToString().Should().Be("\"answered\"");
        OneLine(sink).Should().Contain("exit 0").And.Contain("end answered").And.Contain("usage captured").And.Contain("kimi-code/k3-256k");
    }

    [Fact]
    public void AFailedLaunch_LogsTheFailureNameToo_AndUncapturedUsage()
    {
        var (runtime, invocation, sink) = Fixture();

        runtime.RateLimited(invocation, KimiShimScripts.Ended(75, AcpEnd.AgentFailed, "insufficient balance", failure: "balance_spent"));

        OneLine(sink).Should().Contain("exit 75").And.Contain("end agent_failed").And.Contain("failure balance_spent").And.Contain("usage not captured");
    }

    [Fact]
    public void AStdoutThatIsNoReport_IsLoggedAsSuch_NotSilently()
    {
        var (runtime, invocation, sink) = Fixture();

        runtime.RateLimited(invocation, new ProcessResult(70, "garbage", "boom", TimedOut: false));

        OneLine(sink).Should().Contain("exit 70").And.Contain("no report");
    }

    [Fact]
    public void TheLine_NamesNothingOfTheAnswerOrThePrompt()
    {
        var (runtime, invocation, sink) = Fixture();

        runtime.RateLimited(invocation, KimiShimScripts.Answered("""{"findings": [{"note": "SECRETANSWER"}]}"""));

        OneLine(sink).Should().NotContain("SECRETANSWER");
    }
}
