using System.Collections.Concurrent;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi conversation's ONE owned home, kept across its turns (E5.3 decisions a–e, j, k and judgement R1, R2, R8 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>; S5.3.1–S5.3.4, S5.3.12, S5.3.13, S5.3.15, S5.3.16): the root is <c>&lt;folder&gt;\kh-&lt;10 hex&gt;</c>
/// and never the conversation folder whose turn the place holds; made at the first turn and written by nobody after; validated before
/// every turn, where the sign-in really is included; and serialised on the sign-in's gate with every other coai Kimi launch.
/// </summary>
/// <remarks>
/// The homes root is a <c>testsToDelete-</c> folder of this test's; the person is <c>KimiFixture</c>'s, in another; the lock root is overridden
/// per async flow. A network path is a SEAM's answer, never a real target. The arms that drive <c>ConsultationService</c> (S5.3.4's record state,
/// S5.3.16 (i) through two consultations) wait for E5.4's <c>KimiConsultant</c>; what they pin is pinned here at the launch control.
/// </remarks>
public sealed class AConversationsKimiHomeTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _homes = TempDir.For("testsToDelete-kimi-homes-");
    private readonly List<string> _notes = [];

    public void Dispose()
    {
        _homes.Dispose();
        _person.Dispose();
    }

    private string Locks => Path.Combine(_person.Folder, "locks");

    private IDisposable Rooted() => KimiCredentialsLock.RootFor(Locks);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The shape E5.4's consultant declares to the places: a home under <c>coai-kimi</c>, no copy asked here (the lock order is the point, not the snapshot).</summary>
    private sealed class KimiShaped : IConsultantRuntime
    {
        public string Vendor => "kimi";

        public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

        public string HomesFolder => ConsultantHomes.Kimi;

        public ReviewerInvocation Build(ConsultantLaunch launch) => throw new NotSupportedException("never launched here");

        public string ReadHandle(ProcessResult result) => string.Empty;

        public bool DroppedTheConversation(ProcessResult result) => false;
    }

    /// <summary>A Kimi on this "machine": its version, recording what was asked of it.</summary>
    private sealed class FakeKimi : IProcessLauncher
    {
        public ConcurrentQueue<ProcessRequest> Started { get; } = new();

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Started.Enqueue(request);

            return Task.FromResult(new ProcessResult(0, "kimi 2.1.1\r\n", string.Empty, TimedOut: false));
        }
    }

    /// <summary>The conversation's place: <c>&lt;homes&gt;\coai-kimi\&lt;id&gt;</c>, held, stamped and locked for the turn.</summary>
    private async Task<ConsultantPlace> PlaceAsync(string id)
    {
        var places = new ConsultantPlaces(_homes, ConversationFolder.StoreOf(_homes.At("data")), new ProcessLauncher(), _notes.Add);
        var place = await places.ForConversationAsync(new KimiShaped(), id, _homes.At("repo"), TimeSpan.FromMinutes(12), Ct);
        place.Refusal.Should().BeEmpty();

        return place;
    }

    /// <summary>The adapter E5.4 composes over, on this fake person: the launch rooted at <c>KimiHome.HomeFor(OwnedScratch, …)</c>.</summary>
    private KimiRuntime Runtime() => new("kimi", HostKind.Windows, new KimiSeams(root => _person.PlacesAt(root), () => ("coai-mcp", [])));

    private ReviewerInvocation Invocation(KimiRuntime runtime, string ownedScratch) =>
        runtime.Build(ConsultantRoles.Consult, "Reply with exactly: OK", _homes.At("repo"), string.Empty, _homes.At("answers"), new ReviewerSettings("kimi") { OwnedScratch = ownedScratch });

    private static string HomeOf(ReviewerInvocation invocation) => KimiEnvironment.FlagOf(invocation.Request, Runners.Acp.AcpWords.HomeFlag);

    /// <summary>One turn through the ONE control, as the executor asks it — bounded, so a launch that waits for its own caller is a sentence and not a hang.</summary>
    private static async Task<LaunchTurn> TurnAsync(KimiRuntime runtime, ReviewerInvocation invocation, FakeKimi kimi, TimeSpan within)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        bounded.CancelAfter(within);
        try
        {
            return await runtime.TakeTurnAsync(invocation, kimi, bounded.Token);
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
            Assert.Fail($"Expected the launch turn to be taken within {within.TotalSeconds:0}s, but TakeTurnAsync was cancelled waiting for the turn of {HomeOf(invocation)}");
            throw;
        }
    }

    // ---------- S5.3.1 ----------

    [Fact]
    public async Task AConversationTurn_StartsOnItsKhRoot_AndNeverWaitsForItself()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var id = ConsultationStore.NewId();
        using var place = await PlaceAsync(id);
        VibeHomeTurns.Held(place.Folder).Should().BeTrue("the place holds the conversation folder's turn for the whole turn");
        var runtime = Runtime();
        var invocation = Invocation(runtime, place.Folder);

        var turn = await TurnAsync(runtime, invocation, new FakeKimi(), TimeSpan.FromSeconds(5));

        turn.Refusal.Should().BeEmpty();
        var home = HomeOf(invocation);
        Path.GetDirectoryName(home).Should().Be(place.Folder, "the root is a CHILD of the conversation folder");
        Path.GetFileName(home).Should().MatchRegex("^kh-[0-9a-f]{10}$");
        VibeHomeTurns.Held(home).Should().BeTrue("the kh root's own turn is what the launch holds");
        turn.Dispose();
        VibeHomeTurns.Held(home).Should().BeFalse();
    }

    // ---------- S5.3.2 ----------

    /// <summary>Every entry of <c>home/</c> and <c>os/</c>: a file as its bytes' hash, a link as its target, a folder by name — the junction never entered.</summary>
    private static List<string> Listing(KimiPlaces places) =>
    [
        .. new[] { places.Home, places.Os }.SelectMany(folder => Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal))
            .Select(entry => OwnedPaths.IsLinkIfThere(entry) ? $"{entry} -> {Junctions.Target(entry)}"
                : File.Exists(entry) ? $"{entry} {Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(entry)))}"
                : $"{entry}/"),
    ];

    [Fact]
    public async Task TheFirstTurnMakesTheHome_TheSecondWritesNothing()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var place = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var invocation = Invocation(runtime, place.Folder);
        var places = _person.PlacesAt(HomeOf(invocation));

        (await TurnAsync(runtime, invocation, new FakeKimi(), TimeSpan.FromSeconds(5))).Dispose();

        OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("turn 1 made the junction");
        Junctions.SameFolder(Junctions.Target(places.Link), _person.Credentials).Should().BeTrue();
        File.ReadAllBytes(places.Config).Should().Equal(_person.ExpectedConfig());
        Directory.EnumerateFileSystemEntries(places.Os).Should().BeEmpty();
        var after1 = Listing(places);

        var turn2 = await TurnAsync(runtime, invocation, new FakeKimi(), TimeSpan.FromSeconds(5));

        turn2.Refusal.Should().BeEmpty();
        Listing(places).Should().Equal(after1, "Made writes nothing on a home that has its junction and its config");
        turn2.Dispose();
    }

    // ---------- S5.3.3 ----------

    [Fact]
    public async Task AJunctionRetargetedBetweenTurns_RefusesTurnTwo_BeforeAnyProcess()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var place = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var invocation = Invocation(runtime, place.Folder);
        var places = _person.PlacesAt(HomeOf(invocation));
        (await TurnAsync(runtime, invocation, new FakeKimi(), TimeSpan.FromSeconds(5))).Dispose();
        var persons2 = Path.Combine(_person.Folder, "persons2", "credentials");
        Directory.CreateDirectory(persons2);
        File.WriteAllText(Path.Combine(persons2, "kimi-code.json"), "{}");
        DirectoryLink.Remove(places.Link);
        await DirectoryLink.MakeAsync(new ProcessLauncher(), places.Link, persons2);
        var before = _person.Hash();
        var kimi = new FakeKimi();

        var turn2 = await TurnAsync(runtime, invocation, kimi, TimeSpan.FromSeconds(5));

        turn2.Refusal.Should().Contain("credentials points at").And.Contain("persons2");
        kimi.Started.Should().BeEmpty("a refused shape starts nothing");
        _person.Hash().Should().Equal(before);
        File.Exists(Path.Combine(persons2, "kimi-code.json")).Should().BeTrue("the other folder keeps its file too");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(_person.Credentials)).Should().BeFalse();
    }

    // ---------- S5.3.4 ----------

    [Fact]
    public async Task AReLoginBetweenTurns_RefusesWithTheCure_AndStartsNothing()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var place = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var invocation = Invocation(runtime, place.Folder);
        var places = _person.PlacesAt(HomeOf(invocation));
        (await TurnAsync(runtime, invocation, new FakeKimi(), TimeSpan.FromSeconds(5))).Dispose();
        File.WriteAllText(_person.PersonsConfig, KimiFixture.OwnerConfig().Replace(KimiFixture.SlotKey, "oauth/kimi-code-env-y"));
        var kimi = new FakeKimi();

        var turn2 = await TurnAsync(runtime, invocation, kimi, TimeSpan.FromSeconds(5));

        turn2.Refusal.Should().Contain("config.toml is not what coai writes").And.Contain("re-login").And.Contain("delete " + places.OwnedRoot, "the cure");
        kimi.Started.Should().BeEmpty();
        OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("the home is kept as it is: a refusal writes nothing");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(_person.Credentials)).Should().BeFalse();
    }

    // ---------- S5.3.12 ----------

    [Fact]
    public async Task AConsultTurnAndAReviewerLaunch_OnOneSignIn_NeverOverlap()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var place = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var conversation = Invocation(runtime, place.Folder);
        var round = Invocation(runtime, Directory.CreateDirectory(_homes.At("coai-answers-round")).FullName);
        var gate = KimiCredentialsLock.PathFor(_person.Credentials);

        var first = await TurnAsync(runtime, conversation, new FakeKimi(), TimeSpan.FromSeconds(5));
        var second = runtime.TakeTurnAsync(round, new FakeKimi(), Ct);

        first.Refusal.Should().BeEmpty();
        HeldFile.IsHeld(gate).Should().BeTrue("both launches key the same gate file: the fake person's credentials folder");
        (await Task.WhenAny(second, Task.Delay(1500, Ct))).Should().NotBe(second, "the round's launch on the one sign-in waits for the conversation's turn");
        first.Dispose();
        var turn = await second.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        turn.Refusal.Should().BeEmpty();
        HeldFile.IsHeld(gate).Should().BeTrue();
        turn.Dispose();
        HeldFile.IsHeld(gate).Should().BeFalse();
    }

    // ---------- S5.3.13 ----------

    [Fact]
    public void ThePathArithmetic_IsTheNumbersTheDocumentStates()
    {
        const string root = @"C:\Users\u\AppData\Local";
        var id = new string('a', 32);

        var conversation = Path.Combine(KimiHome.HomeFor(ConsultantHomes.ConversationFolder(root, ConsultantHomes.Kimi, id), "kimi", ConsultantRoles.Consult), "home");
        var oneShot = Path.Combine(KimiHome.HomeFor(VibeHomes.OneShotIn(root, "0ddba11c"), "kimi", ConsultantRoles.Question), "home");

        conversation.Length.Should().Be(root.Length + 62, "J3: 85 + u at the default root");
        oneShot.Length.Should().Be(root.Length + 36, "J3: 59 + u at the default root");
    }

    // ---------- S5.3.15 ----------

    [Fact]
    public async Task AnAncestorOfTheSignIn_ResolvingOffTheLocalDrives_RefusesTurnTwo_BeforeAnyLock()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var place = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var invocation = Invocation(runtime, place.Folder);
        var places = _person.PlacesAt(HomeOf(invocation));
        (await TurnAsync(runtime, invocation, new FakeKimi(), TimeSpan.FromSeconds(5))).Dispose();
        var lockFiles = Directory.EnumerateFiles(Locks).Count();
        var kimi = new FakeKimi();
        var before = Junctions.Shell;
        Junctions.StandIn(new ResolvingJunctionShell(before, _person.Credentials, @"\\server\share\.kimi-code\credentials"));
        LaunchTurn turn2;
        try
        {
            turn2 = await TurnAsync(runtime, invocation, kimi, TimeSpan.FromSeconds(5));
        }
        finally
        {
            Junctions.StandIn(before);
        }

        turn2.Refusal.Should().NotBeEmpty("the launch must be refused because the sign-in resolves off the local drives, but it was Holding");
        turn2.Refusal.Should().Contain("resolves to").And.Contain("not a local drive path").And.Contain("D19").And.Contain(@"\\server\share\.kimi-code\credentials");
        kimi.Started.Should().BeEmpty();
        Directory.EnumerateFiles(Locks).Count().Should().Be(lockFiles, "a refused turn makes no file under kimi-locks: the check runs BEFORE any lock");
        VibeHomeTurns.Held(places.OwnedRoot).Should().BeFalse();
        HeldFile.IsHeld(KimiCredentialsLock.PathFor(_person.Credentials)).Should().BeFalse();
    }

    /// <summary>
    /// The E5.3 review's M1: the person's HOME is where their <c>config.toml</c> is read from, so where ITS ancestors lead is asked before
    /// that read — a swapped ancestor gets no read from the share, not one. The recorder is the config held exclusively: a read that ran
    /// would answer its own failure, never decision j's sentence.
    /// </summary>
    [Fact]
    public async Task ThePersonsHomeResolvingOffTheLocalDrives_IsRefusedBeforeTheirConfigIsRead()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var place = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var invocation = Invocation(runtime, place.Folder);
        var kimi = new FakeKimi();
        using var configHeld = File.Open(_person.PersonsConfig, FileMode.Open, FileAccess.Read, FileShare.None);
        var before = Junctions.Shell;
        Junctions.StandIn(new ResolvingJunctionShell(before, _person.PersonsHome, @"\\server\share\.kimi-code"));
        LaunchTurn turn;
        try
        {
            turn = await TurnAsync(runtime, invocation, kimi, TimeSpan.FromSeconds(5));
        }
        finally
        {
            Junctions.StandIn(before);
        }

        turn.Refusal.Should().Contain("resolves to", "the refusal that wins says which check ran first: the resolved check, never the config read")
            .And.Contain(@"\\server\share\.kimi-code").And.Contain("D19");
        kimi.Started.Should().BeEmpty();
        Directory.Exists(Locks).Should().BeFalse("no lock file: the check runs before any lock");
        VibeHomeTurns.Held(HomeOf(invocation)).Should().BeFalse();
    }

    // ---------- S5.3.16 ----------

    [Fact]
    public async Task TwoConversationsOnOneSignIn_RunOneKimiAtATime()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var one = await PlaceAsync(ConsultationStore.NewId());
        using var two = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var live = 0;
        var peak = 0;
        var gate = KimiCredentialsLock.PathFor(_person.Credentials);
        var gateHeldWhileLive = true;
        var windows = new ConcurrentBag<(DateTime Start, DateTime End)>();

        async Task TurnOnAsync(ConsultantPlace place)
        {
            var turn = await runtime.TakeTurnAsync(Invocation(runtime, place.Folder), new FakeKimi(), Ct);
            turn.Refusal.Should().BeEmpty();
            var start = DateTime.UtcNow;
            var now = Interlocked.Increment(ref live);
            InterlockedMax(ref peak, now);
            gateHeldWhileLive &= HeldFile.IsHeld(gate);
            await Task.Delay(300, Ct);
            Interlocked.Decrement(ref live);
            windows.Add((start, DateTime.UtcNow));
            turn.Dispose();
        }

        await Task.WhenAll(TurnOnAsync(one), TurnOnAsync(two));

        peak.Should().Be(1, "the peak of live Kimi launches on one sign-in is 1");
        var ordered = windows.OrderBy(window => window.Start).ToList();
        ordered[1].Start.Should().BeOnOrAfter(ordered[0].End, "the second's start is not before the first's end");
        gateHeldWhileLive.Should().BeTrue("both used ONE gate path, held for the life of each turn");
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    [Fact]
    public async Task ATurnWaitsForAGateAnotherProcessHolds_AndStartsWithinTwoSecondsOfItsRelease()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        using var place = await PlaceAsync(ConsultationStore.NewId());
        var runtime = Runtime();
        var invocation = Invocation(runtime, place.Folder);
        Directory.CreateDirectory(Locks);
        // Another process's gate: a plain non-inheriting take in this host stands in for it.
        var elsewhere = ((Hold.Taken)HeldFile.Take(KimiCredentialsLock.PathFor(_person.Credentials))).Stream;
        var released = DateTime.MaxValue;
        var releasing = Task.Run(async () =>
        {
            await Task.Delay(1000, Ct);
            // Stamped BEFORE the release: a launch that starts in the instant after the dispose must still read as after it.
            released = DateTime.UtcNow;
            elsewhere.Dispose();
        }, Ct);

        var turn = await TurnAsync(runtime, invocation, new FakeKimi(), TimeSpan.FromSeconds(10));
        var started = DateTime.UtcNow;
        await releasing;

        turn.Refusal.Should().BeEmpty();
        started.Should().BeOnOrAfter(released, "the launch must start after the gate was released, not while the gate was held");
        (started - released).Should().BeLessThan(TimeSpan.FromSeconds(2), "the gate is waited for in one-second slices");
        turn.Dispose();
    }
}
