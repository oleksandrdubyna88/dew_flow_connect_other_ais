using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the shim says about the lock's answer (decision 10 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>): a tree held, another tree still alive, or a hold that cannot
/// be taken at all — the last two are refusals, because the alternative to a refused turn is two refreshers on a rotating token.
/// </summary>
public sealed class KimiShimHoldTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimishimhold-");

    public void Dispose() => _dir.Dispose();

    private TreeHold AHold() => new(
        new FileStream(_dir.At("a.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None),
        new FileStream(_dir.At("b.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None));

    [Fact]
    public void ATakenHold_IsHeld_AndTheShimGivesItBackByDisposingIt()
    {
        var hold = AHold();

        var held = KimiShimHold.Held(new TreeHolding.Taken(hold), @"C:\persons\credentials");

        held.Should().BeOfType<AcpHolding.Held>().Subject.Hold.Dispose();
        new FileInfo(_dir.At("a.lock")).Invoking(file => file.Delete()).Should().NotThrow("the hold's files are free once it is disposed");
    }

    [Fact]
    public void ABusyHold_NamesTheFileAndSaysTheTurnIsGivenUp()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(_dir.At("locks"));
        Directory.CreateDirectory(_dir.At("persons", "credentials"));
        var held = KimiShimHold.Held(new TreeHolding.Busy(), _dir.At("persons", "credentials"));

        var sentence = held.Should().BeOfType<AcpHolding.Busy>().Subject.Sentence;

        sentence.Should().Contain("another Kimi started on this sign-in is still running").And.Contain(KimiCredentialsLock.TreePathFor(_dir.At("persons", "credentials"))).And.Contain("the turn is given up");
    }

    [Fact]
    public void AHoldThatCannotBeTaken_IsRefused_WithTheCauseAndTheReason()
    {
        var held = KimiShimHold.Held(new TreeHolding.Unusable(new IOException("the disk said no")), @"C:\persons\credentials");

        var sentence = held.Should().BeOfType<AcpHolding.Unusable>().Subject.Sentence;

        sentence.Should().Contain("the disk said no").And.Contain("two refreshers could race (D19)");
    }

    [Fact]
    public void AHoldingNobodyNamed_IsRefusedToo_NeverTreatedAsHeld()
    {
        KimiShimHold.Held(null!, @"C:\persons\credentials").Should().BeOfType<AcpHolding.Unusable>().Which.Sentence.Should().Contain("cannot be taken");
    }

    // ---------- the A2-A3 seam in the shim's hold (§0 of both cornerstone specs) ----------

    private const string Sentence = "a survivor marker stands: pid 4242 of an earlier Kimi tree is still running";

    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(80);

    private string Locks => _dir.At("locks");

    private string Persons => _dir.At("persons", "credentials");

    private string Owned => _dir.At("owned", "kh-1");

    private sealed class Standing(string sentence) : IKimiSurvivors
    {
        public string MarkerPathAsked { get; private set; } = string.Empty;

        public Task<string> BlockedAsync(string markerPath, CancellationToken ct)
        {
            MarkerPathAsked = markerPath;

            return Task.FromResult(sentence);
        }

        public string KeepsHome(string ownedRoot) => sentence;
    }

    private sealed class FixedList : IProcessList
    {
        public ProcessListing Snapshot() => new ProcessListing.Listed([new ProcessEntry(100, 1, "coai-mcp")]);
    }

    private static ForeignKimiWatch NobodyForeign() => new(new FixedList(), 100, TimeSpan.FromSeconds(1), _ => { });

    private KimiPlaces MadePlaces()
    {
        Directory.CreateDirectory(Persons);
        Directory.CreateDirectory(Path.Combine(Owned, "home"));
        var places = KimiPlaces.ForShim(Owned, Path.GetTempPath());
        Junctions.Make(HostKind.Windows, places.Link, Persons).Should().BeEmpty("the owned home's sign-in link is made first");

        return places;
    }

    /// <summary>The tree hold's take, NON-inheriting: no test holds an inheritable handle in the test host.</summary>
    private static TreeHolding NonInheriting(string target, string root, TimeSpan within) => KimiCredentialsLock.TakeTreeThrough(target, root, within, (path, wait) => HeldFile.TakeWithin(path, wait));

    [Fact]
    public void AStandingMarker_MakesTheShimsHoldBusy_WithTheMarkerSentence_AndGivesTheHoldBack()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var places = MadePlaces();
        var gate = new Standing(Sentence);
        using var seam = KimiSurvivors.Using(gate);

        var holding = KimiShimHold.Taken(places, Brief, NobodyForeign(), NonInheriting);

        holding.Should().BeOfType<AcpHolding.Busy>("the shim's hold was Held over a standing survivor marker").Which.Sentence.Should().Be(Sentence);
        gate.MarkerPathAsked.Should().Be(KimiCredentialsLock.MarkerPathFor(Persons), "the marker is found through the sign-in's own identity key");
        HeldFile.Probe(KimiCredentialsLock.TreePathFor(Persons)).Should().Be(LockProbe.Free, "the hold is given back when the turn is refused");
        KimiCredentialsLock.HomeProbe(Owned).Should().Be(LockProbe.Free);
    }

    [Fact]
    public void WithNoMarker_TheHoldIsHeld_AsBefore()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var places = MadePlaces();

        var holding = KimiShimHold.Taken(places, Brief, NobodyForeign(), NonInheriting);

        var held = holding.Should().BeOfType<AcpHolding.Held>().Subject;
        HeldFile.Probe(KimiCredentialsLock.TreePathFor(Persons)).Should().Be(LockProbe.Held);
        held.Hold.Dispose();
    }

    // ---------- the joined seam: the product gate and the Kimi supervisor in the shim's hold (A2 and A3) ----------

    private sealed class Listing(params ProcessEntry[] entries) : IProcessList
    {
        public ProcessListing Snapshot() => new ProcessListing.Listed(entries);
    }

    private static KimiSurvivorMarkerDto MarkerNamingAGoneAgent(string ownedRoot) =>
        new(new ReaderMarkerDto(ReaderPhases.Running, 424242, DateTime.UtcNow.AddHours(-1), DateTimeOffset.UtcNow, [new ProcessIdentity(2_000_000_011, DateTime.UtcNow.AddHours(-1))]), ownedRoot, "/people/sign-in");

    private static KimiSurvivorMarkerDto MarkerNamingNothing(string ownedRoot) =>
        new(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, DateTime.UtcNow.AddHours(-1), DateTimeOffset.UtcNow), ownedRoot, "/people/sign-in");

    [Fact]
    public void AMarkerWhoseMemberIsGone_IsCleared_AndTheHoldIsHeld_WithTheKimiSupervisor()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var places = MadePlaces();
        var marker = KimiCredentialsLock.MarkerPathFor(Persons);
        KimiSurvivorMarker.Write(marker, MarkerNamingAGoneAgent(Owned));

        var holding = KimiShimHold.Taken(places, Brief, NobodyForeign(), NonInheriting, supervised: true);

        var held = holding.Should().BeOfType<AcpHolding.Held>("a marker whose member is confirmed gone was refused as if it stood").Subject;
        held.Supervisor.Should().BeOfType<KimiTreeSupervisor>("on Linux the agent's tree is ended under the Kimi supervisor, whose marker is the next launch's evidence");
        var written = KimiSurvivorMarker.Read(marker).Should().BeOfType<MarkerRead.Read>("this turn's own marker replaced the cleared one").Subject.Marker;
        written.Marker.Phase.Should().Be(ReaderPhases.Spawning, "written before the agent starts");
        written.OwnedRoot.Should().Be(Owned, "the home a later disposal asks about");
        held.Hold.Dispose();
    }

    [Fact]
    public void OffLinux_TheHoldIsHeldUnsupervised_AndNoMarkerIsWritten()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var places = MadePlaces();

        var holding = KimiShimHold.Taken(places, Brief, NobodyForeign(), NonInheriting, supervised: false);

        var held = holding.Should().BeOfType<AcpHolding.Held>().Subject;
        held.Supervisor.Should().BeSameAs(UnsupervisedTree.Instance, "Windows and macOS end the tree as they always did");
        File.Exists(KimiCredentialsLock.MarkerPathFor(Persons)).Should().BeFalse("no survivor marker is written where nothing is supervised");
        held.Hold.Dispose();
    }

    [Fact]
    public void AMarkerThatCannotBeWritten_GivesTheHoldBack_AndRefuses_NothingIsStarted()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var places = MadePlaces();
        using var seam = KimiSurvivors.Using(new Standing(string.Empty));
        Directory.CreateDirectory(KimiCredentialsLock.MarkerPathFor(Persons));

        var holding = KimiShimHold.Taken(places, Brief, NobodyForeign(), NonInheriting, supervised: true);

        holding.Should().BeOfType<AcpHolding.Unusable>("the shim started Kimi without the marker that makes a crash safe").Which.Sentence.Should().Contain("could not be written").And.Contain("does not start Kimi without it");
        HeldFile.Probe(KimiCredentialsLock.TreePathFor(Persons)).Should().Be(LockProbe.Free, "the hold is given back when the shim refuses");
    }

    [Fact]
    public void AMarkerThatNamesNothing_WhileAKimiRuns_MakesTheHoldBusy_NamingThePid_AndKeepsTheMarker()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var places = MadePlaces();
        var marker = KimiCredentialsLock.MarkerPathFor(Persons);
        KimiSurvivorMarker.Write(marker, MarkerNamingNothing(Owned));
        using var seam = KimiSurvivors.Using(new ReconcilingSurvivors(new Listing(new ProcessEntry(100, 1, "coai-mcp"), new ProcessEntry(77, 1, "kimi")), () => Locks));

        var holding = KimiShimHold.Taken(places, Brief, NobodyForeign(), NonInheriting, supervised: true);

        var sentence = holding.Should().BeOfType<AcpHolding.Busy>("a marker that names nothing is only cleared over a listing with no kimi in it").Subject.Sentence;
        sentence.Should().Contain(marker).And.Contain("pid 77").And.Contain("safe to delete");
        File.Exists(marker).Should().BeTrue("a marker that could not be cleared stands");
        HeldFile.Probe(KimiCredentialsLock.TreePathFor(Persons)).Should().Be(LockProbe.Free);
    }

    [Fact]
    public void OnLinux_TheShimsHold_IsSupervised_WithoutAnyFlag()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the survivor supervision is Linux's");
        using var standIn = WindowsStandIn.Begin();
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var places = MadePlaces();

        var holding = KimiShimHold.Taken(places, Brief, NobodyForeign(), NonInheriting);

        var held = holding.Should().BeOfType<AcpHolding.Held>().Subject;
        held.Supervisor.Should().BeOfType<KimiTreeSupervisor>("the product's shim on Linux supervises its agent's tree");
        File.Exists(KimiCredentialsLock.MarkerPathFor(Persons)).Should().BeTrue();
        held.Hold.Dispose();
    }
}
