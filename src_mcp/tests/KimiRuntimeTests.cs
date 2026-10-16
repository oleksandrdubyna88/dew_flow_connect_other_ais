using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The Kimi adapter (E4.4, S4.1–S4.4, S4.10, S4.11's card and the registration of S4.2 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): the request it
/// builds for the shim, what it reads from the shim's REPORT, the version probe that never touches the person's home, and where it is registered.
/// </summary>
/// <remarks>
/// Hand-built places (no fake person's home, no junction): these scenarios are pure and run on every OS. The ones that need the launch control
/// — a fake person, a junction, the lock — are <c>AKimiReviewerRunsThroughTheShimTests</c>' (Windows).
/// </remarks>
public sealed class KimiRuntimeTests : IDisposable
{
    private static readonly string ShimExe = Path.Combine(Path.GetTempPath(), "coai", "coai-mcp.exe");

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-runtime-");

    public void Dispose() => _dir.Dispose();

    private string PersonsBin => _dir.At("persons", ".kimi-code", "bin");

    private KimiPlaces Places(string root) => new(root, _dir.At("persons", ".kimi-code"), PersonsBin, [], string.Empty, Path.Combine(PersonsBin, "kimi.exe"));

    private KimiRuntime Runtime(IReadOnlyList<string>? prefix = null) =>
        new("kimi", HostKind.Windows, new KimiSeams(Places, () => (ShimExe, prefix ?? [])));

    private ReviewerSettings Settings(string model = "kimi-code/k3", string executable = "") =>
        new("kimi") { Model = model, ExecutablePath = executable, OwnedScratch = _dir.At("round"), Timeout = TimeSpan.FromMinutes(10) };

    private ReviewerInvocation Built(string prompt = "review this", ReviewerSettings? settings = null, IReadOnlyList<string>? prefix = null) =>
        Runtime(prefix).Build(RoleCatalog.ArchitectureRole, prompt, _dir.At("worktree"), _dir.At("schema.json"), _dir, settings ?? Settings());

    // ---------- S4.1 ----------

    [Fact]
    public void TheShimRequest_CarriesTheFlagsAndNeverThePrompt()
    {
        using var rooted = KimiCredentialsLock.RootFor(_dir.At("locks"));
        const string prompt = "review THIS\nSECRETWORD in the second line";

        var invocation = Built(prompt);

        var request = invocation.Request;
        var root = KimiHome.HomeFor(_dir.At("round"), "kimi", RoleCatalog.ArchitectureRole);
        request.Executable.Should().Be(ShimExe, "the shim is THIS binary, never the kimi");
        request.Arguments.Take(3).Should().Equal("--ask-acp", "--profile", "kimi");
        KimiEnvironment.FlagOf(request, "--model").Should().Be("kimi-code/k3");
        KimiEnvironment.FlagOf(request, "--cwd").Should().Be(_dir.At("worktree"));
        KimiEnvironment.FlagOf(request, "--home").Should().Be(root, "the owned ROOT, kh-<hash> inside the round's scratch, holding home/ and os/");
        KimiEnvironment.FlagOf(request, "--persons-bin").Should().Be(PersonsBin);
        KimiEnvironment.FlagOf(request, "--locks").Should().Be(KimiCredentialsLock.Root);
        KimiEnvironment.FlagOf(request, "--deadline-seconds").Should().Be("590", "the executor's ten minutes, less the shim's margin");
        KimiEnvironment.FlagOf(request, "--executable").Should().Be(Path.Combine(PersonsBin, "kimi.exe"), "the absolute kimi, resolved here in the unredirected parent");
        request.Arguments.Should().NotContain(argument => argument.Contains("SECRETWORD") || argument.Contains('\n'));
        request.StdIn.Should().Be(prompt);
        request.Timeout.Should().Be(TimeSpan.FromMinutes(10));
        request.WorkingDirectory.Should().Be(_dir.At("worktree"));
        invocation.Model.Should().Be("kimi-code/k3");
        invocation.Provider.Should().Be("kimi");
        invocation.Adapter.Should().BeOfType<KimiRuntime>();
        request.InheritsEnvironment.Should().BeFalse("the shim's own hop is built too (D19: on both hops)");
        KimiEnvironment.Refusal(request, Places(root)).Should().BeEmpty("what Build makes is exactly what the launch control admits");
    }

    [Fact]
    public void AModelNobodyNamed_IsTheDefault()
    {
        KimiEnvironment.FlagOf(Built(settings: Settings(model: string.Empty)).Request, "--model").Should().Be(KimiModels.Default);
    }

    [Fact]
    public void AConfiguredPath_IsTheKimiTheShimStarts_NeverTheShimItself()
    {
        var custom = Path.Combine(_dir, "custom", "kimi.exe");

        var request = Built(settings: Settings(executable: custom), prefix: ["coai-mcp.dll"]).Request;

        request.Executable.Should().Be(ShimExe);
        request.Arguments[0].Should().Be("coai-mcp.dll", "a framework-dependent host's dll leads, as for the other shims");
        KimiEnvironment.FlagOf(request, "--executable").Should().Be(custom);
    }

    [Fact]
    public void TwoRoles_GetTwoHomes_AndARetryOfOneGetsItsOwnAgain()
    {
        var runtime = Runtime();
        var settings = Settings();
        string Home(string role) => KimiEnvironment.FlagOf(runtime.Build(role, "p", _dir.At("worktree"), _dir.At("s.json"), _dir, settings).Request, "--home");

        Home("architecture").Should().NotBe(Home("security")).And.Be(Home("architecture"));
    }

    [Fact]
    public void TheLaunchesDeadline_IsTheShimsToo_WhenARetryIsGivenLess()
    {
        var invocation = Built();
        var shorter = invocation with { Request = invocation.Request with { Timeout = TimeSpan.FromSeconds(100) } };

        var launched = ((IReviewerRuntime)Runtime()).ForLaunch(shorter);

        KimiEnvironment.FlagOf(launched.Request, "--deadline-seconds").Should().Be("90", "the shim ends 10 s before the executor would kill it, so its report arrives");
        KimiEnvironment.FlagOf(invocation.Request, "--deadline-seconds").Should().Be("590", "the original is untouched");
    }

    [Fact]
    public void TheDefaultExecutable_IsTheKimiCli_NotTheShim()
    {
        ((IReviewerRuntime)Runtime()).DefaultExecutable.Should().Be(Path.Combine(PersonsBin, "kimi.exe"),
            "what `providers` probes and what the card's version is about");
    }

    [Fact]
    public void TheCapabilitiesItDeclares()
    {
        var runtime = (IReviewerRuntime)Runtime();

        runtime.NeedsASanitisedSnapshot.Should().BeTrue("D6: Kimi loads configuration from its working folder");
        runtime.LaunchesOneAtATime.Should().BeTrue("the refresh token rotates (D19, until M-K14)");
        runtime.ReadsTheCheckout.Should().BeTrue();
    }

    // ---------- S4.2 ----------

    [Fact]
    public void KimiIsRegistered_AndMachineOnly()
    {
        ReviewerRuntimeSelector.Default.Find("kimi").Should().BeOfType<KimiRuntime>();
        ReviewerRuntimeSelector.Named("kimi", "my-kimi").Should().BeOfType<KimiRuntime>().Which.Provider.Should().Be("my-kimi");
        ReviewerRuntimeSelector.RuntimeNames.Should().Contain("kimi");
        ReviewerRuntimeSelector.MachineOnlyRuntimes.Should().Contain("kimi");
        ReviewerRuntimeSelector.RuntimeNames.Except(ReviewerRuntimeSelector.MachineOnlyRuntimes).Should().NotContain("kimi",
            "the Team server accepts RuntimeNames minus the machine-only set, so it refuses a kimi row by name");
    }

    // ---------- S4.3 / S4.4 ----------

    [Fact]
    public void AnAnsweredReport_IsTheAnswer_AndItsUsage()
    {
        var invocation = Built();
        var result = KimiShimScripts.Answered("""{"findings": [{"note": "an answer"}]}""");
        var runtime = (IReviewerRuntime)Runtime();

        runtime.ReadAnswer(invocation, result).Should().Be("""{"findings": [{"note": "an answer"}]}""");
        runtime.ReadUsage(invocation, result).Should().Be(new Usage(19973, 31, null), "the session's totals from /usage, and never a cost (D9)");
    }

    [Fact]
    public void AReportThatEndedAnyOtherWay_IsNoAnswer()
    {
        var runtime = (IReviewerRuntime)Runtime();
        var ended = KimiShimScripts.Ended(69, AcpEnd.Deadline, "the agent did not answer within the 590s this turn was given");

        runtime.ReadAnswer(Built(), ended).Should().BeNull("only `answered` is an answer");
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1, 2]")]
    [InlineData("{}")]
    public void AStdoutThatIsNoReport_HasNoAnswer_AndUnknownUsage_NeverZero(string stdout)
    {
        var runtime = (IReviewerRuntime)Runtime();
        var result = new ProcessResult(70, stdout, "boom", TimedOut: false);

        runtime.ReadAnswer(Built(), result).Should().BeNull();
        runtime.ReadUsage(Built(), result).Should().Be(Usage.Unknown);
    }

    [Fact]
    public void ANotCapturedReport_IsUnknownUsage_NeverZero()
    {
        var spent = KimiShimScripts.Ended(75, AcpEnd.AgentFailed, "insufficient balance", failure: "balance_spent");

        ((IReviewerRuntime)Runtime()).ReadUsage(Built(), spent).Should().Be(Usage.Unknown);
    }

    [Theory]
    [InlineData(AcpEnd.NotStarted)]
    [InlineData(AcpEnd.ModelNotOffered)]
    public void ALaunchThatNeverReachedTheModel_ConsumedNothing_AFactAndNotAnUnknown(AcpEnd end)
    {
        var refused = KimiShimScripts.Ended(65, end, "never started");

        ((IReviewerRuntime)Runtime()).ReadUsage(Built(), refused).Should().Be(Usage.None);
    }

    [Fact]
    public void TheReportsReason_IsWhyItFailed_NamedByTheShimsOwnFailureWord()
    {
        var runtime = (IReviewerRuntime)Runtime();
        var named = KimiShimScripts.Ended(77, AcpEnd.AgentFailed, "Authentication required", failure: "not_signed_in");
        var plain = KimiShimScripts.Ended(69, AcpEnd.NotStarted, "another Kimi started on this sign-in is still running");

        runtime.WhyItFailed(Built(), named).Should().StartWith("Kimi is not signed in").And.Contain("(the shim said: not_signed_in: Authentication required)");
        runtime.WhyItFailed(Built(), plain).Should().Be("another Kimi started on this sign-in is still running");
        runtime.WhyItFailed(Built(), new ProcessResult(1, "not a report", "x", TimedOut: false)).Should().BeEmpty("an unreadable stdout adds nothing; the stderr tail says it");
    }

    // ---------- S4.10 ----------

    [Fact]
    public async Task TheProbe_NeverTouchesThePersonsHome()
    {
        var launcher = new ScriptedKimi();
        var persons = _dir.At("persons", ".kimi-code");
        Directory.CreateDirectory(persons);

        var result = await ((IReviewerRuntime)Runtime()).ProbeVersionAsync(launcher, Path.Combine(PersonsBin, "kimi.exe"), TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(0);
        var request = launcher.Requests.Should().ContainSingle().Which;
        request.Arguments.Should().Equal("--version");
        request.InheritsEnvironment.Should().BeFalse("the built environment, never this process's");
        var home = request.Environment[KimiEnvironment.HomeVariable]!;
        home.Should().NotStartWith(persons, "not the person's home");
        request.Environment["USERPROFILE"].Should().NotBe(Environment.GetEnvironmentVariable("USERPROFILE"), "the OS home is redirected too");
        Path.GetFullPath(home).Should().StartWith(Path.GetFullPath(Path.GetTempPath()), "a fresh folder under temp");
        Directory.Exists(home).Should().BeFalse("deleted when the probe has been read");
        Directory.EnumerateFileSystemEntries(persons).Should().BeEmpty("nothing was written to the person's folder");
        request.WorkingDirectory.Should().NotBe(Environment.CurrentDirectory);
    }

    // ---------- S4.11, the card ----------

    [Fact]
    public void TheCardsWarning_IsTheLaunchesSentence_ForAnUnmeasuredKimi_AndEmptyForAMeasuredOne()
    {
        var runtime = (IReviewerRuntime)Runtime();

        runtime.VersionWarning("kimi, version 2.2.0").Should().Be(KimiLaunch.VersionWarning("kimi 2.2.0"))
            .And.Contain("2.2.0").And.Contain("2.1.1").And.StartWith("Kimi 2.2.0 is not a version coai has measured");
        runtime.VersionWarning("kimi, version 2.1.1").Should().BeEmpty();
        runtime.VersionWarning("nothing printed").Should().BeEmpty();
    }

    // ---------- the launch as it starts ----------

    [Theory]
    [InlineData("no-flag")]
    [InlineData("flag-is-last")]
    public void ALaunchWhoseArgvHasNoDeadlineToSet_IsLeftAsItWas(string row)
    {
        var invocation = Built();
        var arguments = row == "no-flag" ? ["--ask-acp", "--profile", "kimi"] : new[] { "--ask-acp", "--deadline-seconds" };
        var odd = invocation with { Request = invocation.Request with { Arguments = arguments } };

        var launched = ((IReviewerRuntime)Runtime()).ForLaunch(odd);

        launched.Should().BeSameAs(odd, "there is no value to replace, so nothing is invented");
    }

    [Fact]
    public async Task ALaunchThatNamesNoOwnedHome_IsRefusedByTheLaunchControl_BeforeAnythingIsLooked()
    {
        var invocation = Built();
        var without = invocation with { Request = invocation.Request with { Arguments = [.. invocation.Request.Arguments.Where(a => a != "--home" && a != KimiEnvironment.FlagOf(invocation.Request, "--home"))] } };

        var turn = await Runtime().TakeTurnAsync(without, new ScriptedKimi(), TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain("names no owned home (--home)").And.Contain("(D19)");
    }

    [Fact]
    public async Task ALaunchThatNamesItsHome_GoesThroughTheLaunchControl_WhichReadsThePersonsSignIn()
    {
        using var rooted = KimiCredentialsLock.RootFor(_dir.At("locks"));
        Directory.CreateDirectory(Path.Combine(PersonsBin, ".."));
        var launcher = new ScriptedKimi();

        var turn = await Runtime().TakeTurnAsync(Built(), launcher, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain("not signed in to Kimi Code", "the person's config.toml is the first thing the control reads after the host, the tools, the model and the environment pass");
        launcher.Requests.Should().BeEmpty("nothing was started: this is a refusal before the version is asked");
    }
}
