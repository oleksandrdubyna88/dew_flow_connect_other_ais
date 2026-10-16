using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
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

    [Fact]
    public async Task TheStandInGate_BlocksOnAMarkerFile_AndKeepsAllHomes_AndPassesWhenThereIsNone()
    {
        Directory.CreateDirectory(Locks);
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var marker = Path.Combine(Locks, "0123456789abcdef.survivors.json");

        (await MarkerPresence.Instance.BlockedAsync(marker, TestContext.Current.CancellationToken)).Should().BeEmpty();
        MarkerPresence.Instance.KeepsHome(Owned).Should().BeEmpty();

        File.WriteAllText(marker, "{}");

        (await MarkerPresence.Instance.BlockedAsync(marker, TestContext.Current.CancellationToken)).Should().Contain(marker).And.Contain("cannot yet verify");
        MarkerPresence.Instance.KeepsHome(Owned).Should().Contain(marker, "a marker coai cannot read keeps every home: cannot tell is never free");
    }
}
