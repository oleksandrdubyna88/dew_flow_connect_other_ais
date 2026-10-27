using System.Runtime.InteropServices;
using System.Text.Json;
using CoaiMcp.Acp;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shim's first statement and the kernel's refusals (DL9, S-K21; A3 §3, §4.5): the shim becomes a child subreaper before the profile is even looked up, a failure is exit 65 with the written
/// sentence and nothing started, and a Linux kernel with no pidfd refuses the launch before anything is held. In-process, with the reaper a seam — a test host must NEVER become a subreaper.
/// </summary>
public sealed class AskAcpSubreaperTests
{
    private readonly List<string> _stderr = [];
    private readonly StringWriter _stdout = new();

    private string Stderr => string.Join("\n", _stderr);

    private Task<int> RunAsync(FakeAcpAgent agent, Func<SubreaperResult> becomeReaper, IAcpProfile? profile = null) =>
        AskAcpMode.RunAsync(
            ["--ask-acp", "--profile", "fake", "--model", "m-1", "--cwd", agent.Cwd, "--deadline-seconds", "30"],
            new StringReader("say hello"), _stderr.Add, _stdout, becomeReaper, [profile ?? agent.Profile()]);

    [Fact]
    public async Task AFailedPrctl_RefusesWithTheSentence_65_AndStartsNothing()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);

        var code = await RunAsync(agent, () => new SubreaperResult.Failed(1));

        code.Should().Be(65);
        Stderr.Should().Contain("errno 1").And.Contain("prctl(PR_SET_CHILD_SUBREAPER)").And.Contain("does not launch it");
        agent.Entries().Should().BeEmpty("nothing was started, and never a silent fallback");
        _stdout.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task TheReaperIsAskedFirst_BeforeTheProfileIsLookedUp()
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("x")]);
        var asked = 0;

        var code = await AskAcpMode.RunAsync(["--ask-acp", "--profile", "nobody"], new StringReader("x"), _stderr.Add, _stdout, () => { asked++; return new SubreaperResult.Failed(12); }, [agent.Profile()]);

        code.Should().Be(65);
        asked.Should().Be(1);
        Stderr.Should().Contain("errno 12").And.NotContain("no profile", "the reaper is the first statement, before the profile is looked up");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ANormalTurn_RunsUnderEitherAnswerThatIsNotAFailure(bool became)
    {
        using var agent = new FakeAcpAgent([FakeAcpAgent.Chunk("hello")]);

        var code = await RunAsync(agent, () => became ? new SubreaperResult.Became() : new SubreaperResult.NotLinux());

        code.Should().Be(0, Stderr);
        var report = JsonDocument.Parse(_stdout.ToString().Trim()).RootElement;
        report.GetProperty("answer").GetString().Should().Be("hello");
        report.GetProperty("survivors").GetArrayLength().Should().Be(0, "the report carries the survivors, and a clean end has none");
    }

    [Fact]
    public void TheSubreaperSentence_IsThePlansWording()
    {
        LinuxSubreaper.Sentence(13).Should().Be(
            "coai could not make this process the reaper of its own tree (prctl(PR_SET_CHILD_SUBREAPER) answered errno 13), so it cannot verify Kimi's teardown and does not launch it (DL9 of research/PLAN_kimi_and_vibe_on_linux.md)");
        // The real Become() is NEVER called here on Linux: this host would become a subreaper and collect every other test's orphans (as zombies it never reaps).
        // Its Linux arm is the supervise-tree / shim-acp children of SupervisedTeardownLinuxTests, which exit 65 unless it answered Became.
        if (!OperatingSystem.IsLinux())
        {
            LinuxSubreaper.Become().Should().BeOfType<SubreaperResult.NotLinux>("off Linux there is no subreaper to become");
        }
    }

    // ---------- S-K21: a kernel without pidfd refuses the Kimi launch, before anything starts ----------

    internal sealed class EnosysCalls : IPidfdCalls
    {
        public int Open(int pid) => -38;

        public int Signal(int fd, int signal) => 38;

        public int Poll(IReadOnlyList<int> fds, bool[] ready, int milliseconds) => -38;

        public void Close(int fd)
        {
        }
    }

    [Fact]
    public void AKernelWithoutPidfd_RefusesTheKimiAgentRequest_WithTheSentence()
    {
        using var agent = new FakeAcpAgent([]);
        var profile = new KimiAcpProfile();
        var ask = new AcpAsk(FakeAcpAgent.FakeCliExe, agent.Cwd, "m", "p", TimeSpan.FromSeconds(30), "/owned", "/bin");

        using (PidfdLayer.Using(wrapperPresent: false, new EnosysCalls()))
        {
            profile.AgentRefusal(profile.AgentRequest(ask), ask).Should().Contain(PidfdLayer.NoPidfdSentence, "a kernel without pidfd launched Kimi");
        }
    }

    [Fact]
    public void WithAPidfdKernel_TheProfilesRefusalIsTheEnvironmentsAlone()
    {
        using var agent = new FakeAcpAgent([]);
        var profile = new KimiAcpProfile();
        var ask = new AcpAsk(FakeAcpAgent.FakeCliExe, agent.Cwd, "m", "p", TimeSpan.FromSeconds(30), "/owned", "/bin");

        using (PidfdLayer.Using(wrapperPresent: true, new LibcAnswering()))
        {
            profile.AgentRefusal(profile.AgentRequest(ask), ask).Should().NotContain(PidfdLayer.NoPidfdSentence);
        }
    }

    private sealed class LibcAnswering : IPidfdCalls
    {
        public int Open(int pid) => 7;

        public int Signal(int fd, int signal) => 0;

        public int Poll(IReadOnlyList<int> fds, bool[] ready, int milliseconds) => 0;

        public void Close(int fd)
        {
        }
    }

    [Fact]
    public void WhenKimiIsRefusedThere_NoCallIsEverMade()
    {
        // The Calls of a kernel with no pidfd answer ENOSYS to everything: a teardown that reached them signals nothing, never a kill by number.
        using (PidfdLayer.Using(wrapperPresent: false, new EnosysCalls()))
        {
            PidfdLayer.Calls.Open(1).Should().Be(-38);
            PidfdLayer.Calls.Signal(3, 9).Should().Be(38);
        }
    }

    // ---------- StopSignals: SIGTERM and SIGINT reach the token (S-K16) ----------

    [DllImport("libc", EntryPoint = "kill")]
    private static extern int Kill(int pid, int signal);

    [Fact]
    public void ASigterm_CancelsTheToken_AndTheProcessLives()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "SIGTERM to this very process is a Linux test");
        using var stopping = new CancellationTokenSource();

        using (StopSignals.Into(stopping))
        {
            _ = Kill(Environment.ProcessId, 15);
            SpinWait.SpinUntil(() => stopping.IsCancellationRequested, TimeSpan.FromSeconds(10)).Should().BeTrue("the signal did not reach the token");
        }
    }
}
