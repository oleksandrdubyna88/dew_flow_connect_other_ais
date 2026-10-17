using System.Collections.Concurrent;
using System.ComponentModel;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ONE Kimi launch control, over a fake <c>kimi</c> (E4.2 brief section 10, S2.25–S2.31, S2.34–S2.35 and S2.42 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): every check before anything starts, in order; the home's in-process turn and the
/// sign-in's gate held until the launch has been read; the version asked in the BUILT environment on the owned home; the shape
/// checked again after it; and everything given back on every way out.
/// </summary>
/// <remarks>Every fake person, junction and lock is inside this test's own <c>testsToDelete-kimi-</c> folders; the lock root is overridden per async flow.</remarks>
public sealed class KimiLaunchControlTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly List<IDisposable> _scopes = [];

    public KimiLaunchControlTests() => _scopes.Add(_person);

    public void Dispose() => _scopes.ForEach(scope => scope.Dispose());

    private string Locks => Path.Combine(_person.Folder, "locks");

    private IDisposable Rooted() => KimiCredentialsLock.RootFor(Locks);

    private static ProcessRequest Request(KimiPlaces places, string model = KimiModels.Default) =>
        KimiEnvironment.Built(
            new ProcessRequest(places.Executable, ["--ask-acp", "--profile", "kimi", "--model", model, "--home", places.OwnedRoot, "--persons-bin", places.PersonsBin], places.Home),
            places);

    /// <summary>A Kimi on this "machine": its version, and what asking it does.</summary>
    private sealed class FakeKimi(string version = "2.1.1", int exit = 0, Exception? throws = null) : IProcessLauncher
    {
        public ConcurrentQueue<ProcessRequest> Started { get; } = new();

        /// <summary>What the <c>--version</c> process does to the home while it runs — nothing, unless a test says so.</summary>
        public Action<ProcessRequest> WhileTheVersionIsAsked { get; set; } = _ => { };

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Started.Enqueue(request);
            WhileTheVersionIsAsked(request);
            if (throws is not null)
            {
                throw throws;
            }

            return Task.FromResult(new ProcessResult(exit, exit == 0 ? $"kimi {version}\r\n" : string.Empty, exit == 0 ? string.Empty : "boom\r\n", TimedOut: false));
        }
    }

    // ---------- S2.25 ----------

    public static TheoryData<string> Refusals => new()
    {
        "a Linux host", "the tools missing", "a model outside the plan", "an inherited request", "a config with no slot",
        "a credentials folder that is not there", "a credentials folder that is a link", "a block on moonshot-ai",
        "a UNC sign-in home",
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task TheLaunchControl_InOrder_StartsNothingOnARefusal(string row)
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var host = HostKind.Windows;
        var request = Request(places);
        var expected = "";
        switch (row)
        {
            case "a Linux host":
                host = HostKind.Linux;
                expected = "Kimi is refused on a linux host";
                break;
            case "the tools missing":
                places = places with { ToolsRefusal = KimiTools.Refusal(true, places.PersonsBin, string.Empty, ".EXE", _ => false) };
                expected = "rg and fd were not found";
                break;
            case "a model outside the plan":
                request = Request(places, "moonshot-ai/kimi-k2.6");
                expected = "is not on the Kimi Code plan";
                break;
            case "an inherited request":
                request = request with { InheritsEnvironment = true };
                expected = "was not built with coai's own environment";
                break;
            case "a config with no slot":
                File.WriteAllText(_person.PersonsConfig, "default_model = \"x\"\n");
                expected = "not signed in to Kimi Code";
                break;
            case "a credentials folder that is not there":
                GitScratch.DeleteEvenIfReadOnly(_person.Credentials);
                expected = "has no credentials folder";
                break;
            case "a credentials folder that is a link":
                var real = Path.Combine(_person.Folder, "real-credentials");
                Directory.Move(_person.Credentials, real);
                await DirectoryLink.MakeAsync(new ProcessLauncher(), _person.Credentials, real);
                expected = "is itself a link";
                break;
            case "a UNC sign-in home":
                places = places with { PersonsHome = @"\\kimi-test-host.invalid\share\.kimi-code" };
                request = Request(places);
                expected = "is not on a local drive";
                break;
            default:
                File.WriteAllText(_person.PersonsConfig, KimiFixture.OwnerConfig().Replace("default_model", "# d\ndefault_model")
                    + "\n[models.\"kimi-code/k3-256k\"]\nprovider = \"moonshot-ai\"\nmodel = \"k\"\nmax_context_size = 1\ncapabilities = [ \"a\" ]\n");
                expected = "which is not the Kimi Code plan";
                break;
        }

        var fake = new FakeKimi();

        var turn = await KimiLaunch.TakeAsync(host, request, places, fake, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain(expected, row);
        fake.Started.Should().BeEmpty("a refusal starts nothing");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse("no turn was left held");
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse("no gate was left held");
    }

    [Fact]
    public async Task AFolderTheKernelCannotName_IsRefusedWithTheKeySentence_AndStartsNothing()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var fake = new FakeKimi();
        KimiCredentialsLock.Final = _ => string.Empty;
        LaunchTurn turn;
        try
        {
            turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);
        }
        finally
        {
            KimiCredentialsLock.Final = null;
        }

        turn.Refusal.Should().Contain("cannot be named by its handle").And.Contain(places.Credentials).And.Contain("(D19)");
        fake.Started.Should().BeEmpty();
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse("the turn taken first was given back");
        Directory.Exists(Locks).Should().BeFalse("no lock file was made on a spelling");
    }

    private sealed class Blocking(string sentence) : IKimiSurvivors
    {
        public Task<string> BlockedAsync(string markerPath, CancellationToken ct) => Task.FromResult(sentence);

        public string KeepsHome(string ownedRoot) => string.Empty;
    }

    [Fact]
    public async Task AStandingSurvivorMarker_RefusesTheLaunch_WithItsSentence_HoldingNothing()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var fake = new FakeKimi();
        using var seam = KimiSurvivors.Using(new Blocking("a survivor marker stands: pid 4242 of an earlier Kimi tree is still running"));

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Be("a survivor marker stands: pid 4242 of an earlier Kimi tree is still running", "a free gate is not a free sign-in while a marker stands");
        fake.Started.Should().BeEmpty("a refusal starts nothing");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse("no turn was left held");
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse("no gate was left held");
    }

    /// <summary>A marker as the shim of an earlier turn left it: naming one agent that is long gone (no process has this number).</summary>
    private static KimiSurvivorMarkerDto AMarkerNamingAGoneMember(string ownedRoot) =>
        new(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, [new ProcessIdentity(2_000_000_011, DateTime.UtcNow.AddHours(-1))]), ownedRoot, "/people/sign-in");

    [Fact]
    public async Task AStandingMarkerWhoseMemberIsGone_IsClearedByTheLaunch_AndTheLaunchProceeds()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var marker = KimiCredentialsLock.MarkerPathFor(places.Credentials);
        KimiSurvivorMarker.Write(marker, AMarkerNamingAGoneMember(places.OwnedRoot));

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, new FakeKimi(), TestContext.Current.CancellationToken);

        turn.Refusal.Should().BeEmpty("a marker whose every member is confirmed gone is cleared by the launch that finds it, and the launch goes on");
        File.Exists(marker).Should().BeFalse("the reconcile deletes the marker it cleared");
        turn.Dispose();
    }

    private sealed class Machine(params ProcessEntry[] entries) : IProcessList
    {
        public ProcessListing Snapshot() => new ProcessListing.Listed([new ProcessEntry(100, 1, "coai-mcp"), .. entries]);
    }

    [Fact]
    public async Task AStandingMarkerThatCannotBeCleared_RefusesTheLaunch_NamingThePid_KeepsTheMarker_AndHoldsNothing()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var marker = KimiCredentialsLock.MarkerPathFor(places.Credentials);
        KimiSurvivorMarker.Write(marker, new KimiSurvivorMarkerDto(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, DateTime.UtcNow.AddHours(-1), DateTimeOffset.UtcNow), places.OwnedRoot, "/people/sign-in"));
        var fake = new FakeKimi();
        using var seam = KimiSurvivors.Using(new ReconcilingSurvivors(new Machine(new ProcessEntry(77, 1, "kimi")), () => Locks));

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain(marker).And.Contain("pid 77").And.Contain("safe to delete", "the way out is in the sentence, before any button");
        File.Exists(marker).Should().BeTrue("a marker the reconcile could not clear stands");
        fake.Started.Should().BeEmpty("a refusal starts nothing");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse("no turn was left held");
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse("no gate was left held");
    }

    [Fact]
    public async Task AKernelWithNoPidfd_RefusesTheLaunch_BeforeAnythingIsTaken()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var fake = new FakeKimi();
        LaunchTurn turn;
        using (PidfdLayer.Using(wrapperPresent: false, new AskAcpSubreaperTests.EnosysCalls()))
        {
            turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);
        }

        turn.Refusal.Should().Contain(PidfdLayer.NoPidfdSentence, "a launch on a kernel that cannot end the tree by identity was allowed");
        fake.Started.Should().BeEmpty("a refusal starts nothing");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse("no turn was left held");
        Directory.Exists(Locks).Should().BeFalse("no lock was taken for a launch that was refused first");
    }

    // ---------- S2.26 ----------

    [Fact]
    public async Task TheTurnHoldsTheHomeAndTheLock_UntilTheLaunchIsRead()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, new FakeKimi(), TestContext.Current.CancellationToken);

        turn.Refusal.Should().BeEmpty();
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeTrue();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeTrue("the gate is held across the whole launch");
        turn.Dispose();
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse();
        turn.Dispose();
    }

    // ---------- S2.27 ----------

    [Fact]
    public async Task TwoLaunchesOnOneSignIn_WaitOnEachOther_AndOnTwoSignInsDoNot()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var ct = TestContext.Current.CancellationToken;
        var one = _person.Places("kh-one");
        var two = _person.Places("kh-two");

        var first = await KimiLaunch.TakeAsync(HostKind.Windows, Request(one), one, new FakeKimi(), ct);
        var second = KimiLaunch.TakeAsync(HostKind.Windows, Request(two), two, new FakeKimi(), ct);

        (await Task.WhenAny(second, Task.Delay(1500, ct))).Should().NotBe(second, "the second launch on the one sign-in waits for the first");
        first.Dispose();
        var turn = await second.WaitAsync(TimeSpan.FromSeconds(10), ct);
        turn.Refusal.Should().BeEmpty();
        turn.Dispose();

        using var other = new KimiFixture();
        var third = other.Places("kh-three");
        var parallel = await KimiLaunch.TakeAsync(HostKind.Windows, Request(one), one, new FakeKimi(), ct);
        var elsewhere = await KimiLaunch.TakeAsync(HostKind.Windows, Request(third), third, new FakeKimi(), ct);
        elsewhere.Refusal.Should().BeEmpty("two sign-ins do not wait on each other");
        elsewhere.Dispose();
        parallel.Dispose();
    }

    [Fact]
    public async Task AWaitCutByTheToken_GivesTheHomesTurnBack()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        Directory.CreateDirectory(Locks);
        Directory.CreateDirectory(places.Credentials);
        using var holder = ((Hold.Taken)HeldFile.Take(KimiCredentialsLock.PathFor(places.Credentials))).Stream;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var wait = async () => await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, new FakeKimi(), cancel.Token);

        await wait.Should().ThrowAsync<OperationCanceledException>();
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse("the turn taken before the wait is given back when the wait is cut");
    }

    // ---------- S2.29 ----------

    [Fact]
    public async Task TheVersionIsAskedInTheBuiltEnvironmentOnTheOwnedHome()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var fake = new FakeKimi("2.1.1");

        using var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);

        var asked = fake.Started.Should().ContainSingle().Subject;
        asked.Executable.Should().Be(places.Executable).And.NotContain("coai-mcp", "the REAL kimi is asked, not the shim");
        asked.Arguments.Should().Equal("--version");
        asked.WorkingDirectory.Should().Be(places.Home);
        KimiEnvironment.Refusal(asked, places).Should().BeEmpty();
        asked.Timeout.Should().Be(KimiLaunch.VersionTimeout);
        turn.CliVersion.Should().Be("kimi 2.1.1");
        turn.Warning.Should().BeEmpty();
    }

    // ---------- M4 ----------

    [Fact]
    public async Task TheVersionProbe_GetsTheLaunchRequestsPath_NotThisProcesses()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var marker = Path.Combine(_person.Folder, "only-in-the-launch-request");
        var request = KimiEnvironment.Built(
            new ProcessRequest(places.Executable, ["--ask-acp", "--model", KimiModels.Default, "--home", places.OwnedRoot, "--persons-bin", places.PersonsBin], places.Home),
            places,
            string.Join(Path.PathSeparator, marker, places.PersonsBin));
        var fake = new FakeKimi();

        using var turn = await KimiLaunch.TakeAsync(HostKind.Windows, request, places, fake, TestContext.Current.CancellationToken);

        turn.Refusal.Should().BeEmpty();
        var asked = fake.Started.Should().ContainSingle().Subject;
        KimiEnvironment.PathOf(asked).Should().Be(KimiEnvironment.PathOf(request), "one PATH source feeds the probe and the launch").And.Contain(marker);
    }

    // ---------- S2.30 ----------

    [Fact]
    public async Task AnUnmeasuredKimi_LaunchesWithTheWarning()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();

        using var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, new FakeKimi("2.2.0"), TestContext.Current.CancellationToken);

        turn.Refusal.Should().BeEmpty("the owner's rule: warn and run");
        turn.Warning.Should().Contain("2.2.0").And.Contain("2.1.1").And.Contain("probe-kimi-confinement.mjs").And.Contain("probe-kimi-tree-hold.mjs").And.NotContain("re-plumbed");
        turn.CliVersion.Should().Be("kimi 2.2.0");
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("cannot start")]
    public async Task AVersionlessKimi_IsRefused_AndGivesEverythingBack(string row)
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var fake = row == "exit" ? new FakeKimi(exit: 1) : new FakeKimi(throws: new Win32Exception("The system cannot find the file specified"));

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain(row == "exit" ? "answered no version (exit 1)" : "could not be started").And.Contain(row == "exit" ? "boom" : InstallScript);
        turn.CliMissing.Should().Be(row == "cannot start", "a Kimi the OS cannot start is cli-not-found with the install cure (PLAN_vibe_live_round_fixes D5), one that started and answered nothing is the launch control's own verdict");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse();
    }

    /// <summary>The cure a launch that could not start names, for THIS machine's host: Kimi's own PowerShell installer on Windows, its shell script elsewhere.</summary>
    private static string InstallScript => OperatingSystem.IsWindows() ? "https://code.kimi.com/kimi-code/install.ps1" : "https://code.kimi.com/kimi-code/install.sh";

    // ---------- S2.31 ----------

    [Fact]
    public async Task TheShapeIsCheckedAgainAfterTheVersion_AndAThrowGivesEverythingBack()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.Places();
        var fake = new FakeKimi { WhileTheVersionIsAsked = _ => File.WriteAllText(Path.Combine(places.Home, "mcp.json"), "{}") };

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain("mcp.json");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse();

        File.Delete(Path.Combine(places.Home, "mcp.json"));
        var throwing = new FakeKimi(throws: new IOException("disk"));
        var again = async () => await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, throwing, TestContext.Current.CancellationToken);

        await again.Should().ThrowAsync<IOException>();
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse("a throw gives the turn back");
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse("and the gate");
    }

    // ---------- S2.34 / S2.35 ----------

    [Fact]
    public async Task AfterALaunch_TheHomeGoes_AndThePersonsFolderIsWhole_AndNothingOfTheirsIsEverWritten()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var before = _person.Hash();
        var places = _person.Places("kh-s");

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, new FakeKimi(), TestContext.Current.CancellationToken);
        turn.Refusal.Should().BeEmpty();
        File.WriteAllText(Path.Combine(places.Home, "session_index.jsonl"), "{}");
        turn.Dispose();

        GitScratch.DeleteEvenIfReadOnly(places.OwnedRoot);

        Directory.Exists(places.OwnedRoot).Should().BeFalse();
        _person.Hash().Should().Equal(before, "the fake person's folder: every name, byte and attribute as before");
    }

    // ---------- S2.31, the shape before the version ----------

    [Fact]
    public async Task AHomeTamperedWith_IsRefusedBeforeTheVersionIsAsked_AndGivesEverythingBack()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var places = _person.MadeHome("kh-tampered");
        File.WriteAllText(places.Config, "default_model = \"something-else\"\n");
        var fake = new FakeKimi();

        var turn = await KimiLaunch.TakeAsync(HostKind.Windows, Request(places), places, fake, TestContext.Current.CancellationToken);

        turn.Refusal.Should().Contain("config.toml is not what coai writes");
        fake.Started.Should().BeEmpty("the shape is judged before Kimi's own --version is run on the home");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(places.Credentials)).Should().BeFalse();
    }
}
