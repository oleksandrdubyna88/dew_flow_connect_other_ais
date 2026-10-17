using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a standing survivor marker does to the next launch (DL9 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>; S-K14, S-K18, L-N3; A3 §6): reconciled first — terminate by identity, confirm, then
/// ask every member the tri-state question — cleared only when every member is GONE, with the way out in the Busy sentence. The process control is a seam (the <c>ReaderBarrierTests</c> precedent: no
/// test machine can safely make a process that refuses to die), so every arm runs on every platform.
/// </summary>
public sealed class KimiSurvivorGateTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    private readonly TempDir _dir = TempDir.For("testsToDelete-survivor-gate-");

    private string MarkerPath => _dir.At("0123456789abcdef.survivors.json");

    public void Dispose() => _dir.Dispose();

    private static ProcessIdentity Member(int pid) => new(pid, Started);

    private static KimiSurvivorMarkerDto Running(params ProcessIdentity[] tree) =>
        new(new ReaderMarkerDto(ReaderPhases.Running, 424242, Started, DateTimeOffset.UtcNow, tree), "/owned/kimi-root", "/people/sign-in");

    private static KimiSurvivorMarkerDto Spawning() =>
        new(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, Started, DateTimeOffset.UtcNow), "/owned/kimi-root", "/people/sign-in");

    private static ReconcileSeams Seams(
        Func<ProcessIdentity, bool>? terminate = null,
        Func<IReadOnlyList<ProcessIdentity>, Task<IReadOnlyList<ProcessIdentity>>>? confirmGone = null,
        Func<ProcessIdentity, Liveness>? liveness = null) =>
        new(
            terminate ?? (_ => true),
            confirmGone ?? (_ => Task.FromResult<IReadOnlyList<ProcessIdentity>>([])),
            _ => [],
            liveness ?? (_ => new Liveness.Gone("no such process")));

    private static Func<KimiPresence> NoKimiAnywhere => () => new KimiPresence.NoKimi();

    private Task<SurvivorVerdict> ReconcileAsync(ReconcileSeams seams, Func<KimiPresence>? presence = null) =>
        KimiSurvivorGate.ReconcileAsync(MarkerPath, presence ?? NoKimiAnywhere, seams);

    // ---------- the verdicts ----------

    [Fact]
    public async Task NoFile_IsNone() =>
        (await ReconcileAsync(Seams())).Should().BeOfType<SurvivorVerdict.None>();

    [Fact]
    public async Task AStandingMarkerNamingAGoneProcess_IsCleared_AndTheFileIsDeleted()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        var verdict = await ReconcileAsync(Seams());

        verdict.Should().BeOfType<SurvivorVerdict.Cleared>("a launch was refused over a marker whose member is gone");
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task AMemberThatIsAliveIsEndedByIdentity_ThenTheMarkerIsCleared()
    {
        var ended = new List<ProcessIdentity>();
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        var verdict = await ReconcileAsync(Seams(
            terminate: member => { ended.Add(member); return true; },
            liveness: member => member.Pid == 424242 || ended.Contains(member) ? new Liveness.Gone("exited") : new Liveness.Alive()));

        ended.Should().ContainSingle().Which.Should().Be(Member(424243), "the member the marker names is terminated by its identity — pid AND start");
        verdict.Should().BeOfType<SurvivorVerdict.Cleared>();
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task ASurvivorStillThereAtTheBound_KeepsTheMarker_AndIsBlockedNamingThePid()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        var verdict = await ReconcileAsync(Seams(terminate: _ => false, confirmGone: members => Task.FromResult(members)));

        var blocked = verdict.Should().BeOfType<SurvivorVerdict.Blocked>("a launch proceeded on a sign-in whose previous tree still has a survivor (pid 424243)").Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderTerminationFailed);
        File.Exists(MarkerPath).Should().BeTrue("a survivor keeps the marker, re-tried on every launch");
        KimiSurvivorGate.Sentence(blocked).Should().Contain("424243");
    }

    [Theory]
    [InlineData("EACCES")]
    [InlineData("EIO")]
    [InlineData("garbage")]
    public async Task TheNextLaunchsReconcile_KeepsAMarkerWhoseMemberItCouldNotRead(string failure)
    {
        var proc = new FakeProcReads();
        proc.Stats[424243] = () => failure switch
        {
            "EACCES" => throw new UnauthorizedAccessException("EACCES"),
            "EIO" => throw new IOException("EIO"),
            _ => "garbage",
        };
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        var verdict = await ReconcileAsync(Seams(liveness: member => LivenessCheck.Linux(member, proc, _ => Started)));

        var blocked = verdict.Should().BeOfType<SurvivorVerdict.Blocked>("the next launch's reconcile cleared a marker whose member it could not read").Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderMemberUnverifiable);
        blocked.Detail.Should().Contain("424243").And.Contain(failure == "garbage" ? "not a /proc stat line" : "/proc/424243/stat");
        File.Exists(MarkerPath).Should().BeTrue();
    }

    [Fact]
    public async Task AMarkerFileThatCannotBeParsed_IsKept_AndIsBlocked_NeverCleared()
    {
        File.WriteAllText(MarkerPath, "{ this is not json");

        var verdict = await ReconcileAsync(Seams());

        var blocked = verdict.Should().BeOfType<SurvivorVerdict.Blocked>().Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderMemberUnverifiable);
        File.Exists(MarkerPath).Should().BeTrue();
        KimiSurvivorGate.Sentence(blocked).Should().Contain(MarkerPath).And.Contain("could not be read");
    }

    [Fact]
    public void AMarkerWithNoPath_IsUnreadable_NeverAThrow() =>
        KimiSurvivorMarker.Read(string.Empty).Should().BeOfType<MarkerRead.Unreadable>("an empty path (the folder could not be keyed) was read as a file name").Which.Reason.Should().Contain("no path");

    // ---------- a marker is read without trusting its path (the coai code gate, E5) ----------

    [Fact]
    public void AnOversizedMarker_IsUnreadable_NeverParsed_NeverBuffered()
    {
        Directory.CreateDirectory(_dir.Path);
        var padded = "{\"marker\":{\"phase\":\"running\",\"ownerPid\":1,\"tree\":[]},\"ownedRoot\":\"/o\",\"signInFolder\":\"" + new string('x', 70 * 1024) + "\"}";
        File.WriteAllText(MarkerPath, padded);

        var read = KimiSurvivorMarker.Read(MarkerPath);

        read.Should().BeOfType<MarkerRead.Unreadable>("a 70 KiB marker was read whole: a file grown for a denial of service is not a marker").Which.Reason.Should().Contain("larger than 65536 bytes");
    }

    [Fact]
    public void AMarkerThatIsALink_IsUnreadable_NeverFollowed()
    {
        var elsewhere = _dir.At("elsewhere.json");
        KimiSurvivorMarker.Write(elsewhere, Running(Member(424243)));
        try
        {
            File.CreateSymbolicLink(MarkerPath, elsewhere);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"this account cannot make a symbolic link here ({e.GetType().Name})");
        }

        var read = KimiSurvivorMarker.Read(MarkerPath);

        read.Should().BeOfType<MarkerRead.Unreadable>("a symlinked marker was followed to a file the person never put there").Which.Reason.Should().Contain("link").And.NotContain("elsewhere", "the reason never carries a resolved path");
    }

    [Fact]
    public void AnUnreadableMarkersReason_NamesTheFailure_NeverAPath()
    {
        Directory.CreateDirectory(MarkerPath);

        var read = KimiSurvivorMarker.Read(MarkerPath);

        read.Should().BeOfType<MarkerRead.Unreadable>().Which.Reason.Should().NotContain(_dir.Path, "an exception's own message carries the path it was given").And.Contain("code 0x");
    }

    [Fact]
    public void AMarkerOfExactlyTheCeiling_IsStillRead()
    {
        var read = BoundedFile.ReadRegular(WriteBytes(BoundedFile.MaxBytes));

        read.Should().BeOfType<BoundedRead.Text>().Which.Value.Length.Should().Be(BoundedFile.MaxBytes);
        BoundedFile.ReadRegular(WriteBytes(BoundedFile.MaxBytes + 1)).Should().BeOfType<BoundedRead.Refused>();
    }

    private string WriteBytes(int count)
    {
        var path = _dir.At($"bytes-{count}.json");
        File.WriteAllText(path, new string('a', count));

        return path;
    }

    [Fact]
    public void AHeldStreamOverTheCeiling_IsRefused_AndOneUnderIsRead()
    {
        using var big = new FileStream(WriteBytes(BoundedFile.MaxBytes + 10), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var small = new FileStream(WriteBytes(10), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        BoundedFile.ReadStream(big).Should().BeOfType<BoundedRead.Refused>();
        BoundedFile.ReadStream(small).Should().BeOfType<BoundedRead.Text>().Which.Value.Should().Be("aaaaaaaaaa");
    }

    // ---------- the marker's OWNER is asked first: a live turn's marker is never an orphan's ----------

    private const int Owner = 424242;

    private static Func<ProcessIdentity, Liveness> OwnerIs(Liveness owner, Liveness others) => member => member.Pid == Owner ? owner : others;

    [Fact]
    public async Task AMarkerWhoseOwnerShimIsAlive_IsNeverTerminated_AndIsBlockedWithAWaitSentence()
    {
        var terminated = new List<ProcessIdentity>();
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));

        var verdict = await ReconcileAsync(Seams(terminate: member => { terminated.Add(member); return true; }, liveness: OwnerIs(new Liveness.Alive(), new Liveness.Alive())));

        var blocked = verdict.Should().BeOfType<SurvivorVerdict.Blocked>("a live turn's marker was reconciled as an orphan's").Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderOwnerAlive);
        terminated.Should().BeEmpty("the agent of a turn whose shim still runs was SIGKILLed by another launch");
        File.Exists(MarkerPath).Should().BeTrue();
        KimiSurvivorGate.Sentence(blocked).Should().Contain("a Kimi turn is still running under coai shim pid 424242").And.Contain("it ends when its agent does").And.Contain("the turn is given up");
    }

    [Fact]
    public async Task ANamelessMarkerWhoseOwnerShimIsAlive_IsBlocked_EvenOverAListingWithNoKimi()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning());

        var verdict = await ReconcileAsync(Seams(liveness: OwnerIs(new Liveness.Alive(), new Liveness.Gone("no such process"))), () => new KimiPresence.NoKimi());

        verdict.Should().BeOfType<SurvivorVerdict.Blocked>("the shim is between writing its marker and starting the agent — clearing it would let a second Kimi start").Which.Code.Should().Be(BlockedReasons.ReaderOwnerAlive);
        File.Exists(MarkerPath).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AMarkerWhoseOwnerCannotBeChecked_IsBlockedUnverifiable_AndNothingIsTerminated(bool nameless)
    {
        var terminated = new List<ProcessIdentity>();
        KimiSurvivorMarker.Write(MarkerPath, nameless ? Spawning() : Running(Member(424243)));

        var verdict = await ReconcileAsync(
            Seams(terminate: member => { terminated.Add(member); return true; }, liveness: OwnerIs(new Liveness.Unknown("EACCES reading /proc/424242/stat"), new Liveness.Gone("no such process"))),
            () => new KimiPresence.NoKimi());

        var blocked = verdict.Should().BeOfType<SurvivorVerdict.Blocked>("an owner that cannot be checked was taken as gone").Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderMemberUnverifiable);
        terminated.Should().BeEmpty();
        KimiSurvivorGate.Sentence(blocked).Should().Contain("424242").And.Contain("EACCES reading /proc/424242/stat").And.Contain(MarkerPath).And.Contain("safe to delete once");
    }

    // ---------- a marker that names nothing (spawning): cleared only when no kimi runs, and the listing is readable ----------

    [Fact]
    public async Task ASpawningMarker_IsClearedOnlyWhenNoKimiRuns()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning());

        (await ReconcileAsync(Seams(), () => new KimiPresence.NoKimi())).Should().BeOfType<SurvivorVerdict.Cleared>();
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task ASpawningMarker_IsKeptWhileAKimiRuns_NamingItsPids()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning());

        var verdict = await ReconcileAsync(Seams(), () => new KimiPresence.Running([4711, 4712]));

        var blocked = verdict.Should().BeOfType<SurvivorVerdict.Blocked>("a spawning marker was cleared while a kimi runs").Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderIdentityMissing);
        File.Exists(MarkerPath).Should().BeTrue();
        KimiSurvivorGate.Sentence(blocked).Should().Contain("4711, 4712");
    }

    [Fact]
    public async Task ASpawningMarker_IsKeptWhenTheListingCannotBeRead()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning());

        var verdict = await ReconcileAsync(Seams(), () => new KimiPresence.Unreadable("EACCES reading /proc/9/exe"));

        var blocked = verdict.Should().BeOfType<SurvivorVerdict.Blocked>().Subject;
        blocked.Code.Should().Be(BlockedReasons.ReaderMemberUnverifiable);
        KimiSurvivorGate.Sentence(blocked).Should().Contain("EACCES reading /proc/9/exe");
        File.Exists(MarkerPath).Should().BeTrue();
    }

    // ---------- the Busy sentence: the manual way out FIRST, the button as an addition (S-K18) ----------

    [Fact]
    public async Task TheBusySentence_CarriesTheMarkerPath_ThePids_AndTheManualWayOut_BeforeTheButton()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243), Member(424244)));
        var blocked = (SurvivorVerdict.Blocked)await ReconcileAsync(Seams(terminate: _ => false, confirmGone: members => Task.FromResult(members)));

        var sentence = KimiSurvivorGate.Sentence(blocked);

        sentence.Should().Contain(MarkerPath, "the Busy sentence gave no way out: no marker path");
        sentence.Should().Contain("424243").And.Contain("424244");
        sentence.Should().Contain("safe to delete once those processes are gone");
        sentence.IndexOf("safe to delete once", StringComparison.Ordinal).Should().BeLessThan(sentence.IndexOf("Clear the stuck Kimi marker", StringComparison.Ordinal), "the manual way out comes first; the button is an addition");
        sentence.Should().Contain("2026-10-16 08:00:00 UTC");
    }

    [Fact]
    public async Task TheUnverifiableSentence_NamesTheError_AndStillHoldsThePath_AndTheManualWayOut()
    {
        var proc = new FakeProcReads();
        proc.Stats[424243] = () => throw new UnauthorizedAccessException("EACCES");
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));
        var blocked = (SurvivorVerdict.Blocked)await ReconcileAsync(Seams(liveness: member => LivenessCheck.Linux(member, proc, _ => Started)));

        var sentence = KimiSurvivorGate.Sentence(blocked);

        sentence.Should().Contain(MarkerPath).And.Contain("424243").And.Contain("/proc/424243/stat").And.Contain("safe to delete once");
        sentence.IndexOf("safe to delete once", StringComparison.Ordinal).Should().BeLessThan(sentence.IndexOf("Clear the stuck Kimi marker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheUnidentifiedSentence_HoldsThePath_ThatItNamesNoProcess_AndTheManualWayOut()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning());
        var blocked = (SurvivorVerdict.Blocked)await ReconcileAsync(Seams(), () => new KimiPresence.Running([4711]));

        var sentence = KimiSurvivorGate.Sentence(blocked);

        sentence.Should().Contain(MarkerPath).And.Contain("names no process").And.Contain("4711").And.Contain("safe to delete once");
    }

    // ---------- what the home disposal looks for ----------

    [Fact]
    public void AStandingMarker_IsFoundByTheRootItNames_AndOnlyThere()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));
        KimiSurvivorMarker.Write(_dir.At("fedcba9876543210.survivors.json"), Running(Member(1)) with { OwnedRoot = "/some/other/root" });

        var standing = KimiSurvivorMarker.StandingFor(_dir, "/owned/kimi-root/").Should().BeOfType<StandingMarkers.Found>().Subject.Markers;

        standing.Should().ContainSingle().Which.Path.Should().Be(MarkerPath);
        standing[0].Marker.Marker.Tree.Single().Pid.Should().Be(424243);
        KimiSurvivorMarker.StandingFor(_dir, "/owned/kimi").Should().BeOfType<StandingMarkers.Found>().Which.Markers.Should().BeEmpty("a prefix of the root is not the root");
        KimiSurvivorMarker.StandingFor(_dir.At("no-such-folder"), "/owned/kimi-root").Should().BeOfType<StandingMarkers.Found>().Which.Markers.Should().BeEmpty("no lock root: no marker can exist");
    }

    [Fact]
    public void AnUnreadableMarkerUnderTheRoot_MakesTheAnswerUnverifiable_NeverNone()
    {
        KimiSurvivorMarker.Write(MarkerPath, Running(Member(424243)));
        File.WriteAllText(_dir.At("aaaaaaaaaaaaaaaa.survivors.json"), "not json");

        KimiSurvivorMarker.StandingFor(_dir, "/owned/kimi").Should().BeOfType<StandingMarkers.Unverifiable>("a marker that cannot be read may name any home: nothing can be said").Which.Reason.Should().Contain("aaaaaaaaaaaaaaaa.survivors.json");
        KimiSurvivorMarker.StandingFor(_dir, "/owned/kimi-root").Should().BeOfType<StandingMarkers.Found>("a marker that DOES name the home keeps it, whatever else is unreadable");
    }

    [Fact]
    public void TheMarker_RoundTripsThroughItsFile_PidAndStartIntact()
    {
        var written = Running(Member(424243));

        KimiSurvivorMarker.Write(MarkerPath, written);

        var read = KimiSurvivorMarker.Read(MarkerPath).Should().BeOfType<MarkerRead.Read>().Subject.Marker;
        read.Marker.Phase.Should().Be(ReaderPhases.Running);
        read.Marker.Tree.Should().Equal(Member(424243));
        read.OwnedRoot.Should().Be("/owned/kimi-root");
        read.SignInFolder.Should().Be("/people/sign-in");
    }

    // ---------- the process list as a listing: an EMPTY one is "cannot tell", never "nobody" ----------

    private static CoaiMcp.Runners.Kimi.ProcessEntry Entry(int pid, int parent, string name) => new(pid, parent, name);

    private sealed class FixedListing(ProcessListing listing) : IProcessList
    {
        public FixedListing(params CoaiMcp.Runners.Kimi.ProcessEntry[] entries)
            : this(new ProcessListing.Listed(entries))
        {
        }

        public ProcessListing Snapshot() => listing;
    }

    [Fact]
    public void AnEmptyListing_IsUnreadable_NotNoKimi() =>
        KimiPresence.Of(new FixedListing()).Should().BeOfType<KimiPresence.Unreadable>("an empty process list was read as 'no kimi runs here'");

    [Fact]
    public void AnUnreadableListing_IsUnreadable_WithItsReason_NeverNoKimi() =>
        KimiPresence.Of(new FixedListing(new ProcessListing.Unreadable("/proc/4242/exe: denied"))).Should().BeOfType<KimiPresence.Unreadable>().Which.Reason.Should().Contain("/proc/4242/exe");

    [Fact]
    public void AListingWithAKimi_NamesItsPid_AndOneWithout_IsNoKimi()
    {
        KimiPresence.Of(new FixedListing(Entry(10, 1, "bash"), Entry(11, 10, "kimi"))).Should().BeOfType<KimiPresence.Running>().Which.Pids.Should().Equal(11);
        KimiPresence.Of(new FixedListing(Entry(10, 1, "bash"))).Should().BeOfType<KimiPresence.NoKimi>();
    }
}
