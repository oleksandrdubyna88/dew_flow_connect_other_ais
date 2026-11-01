using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The product's side of the A2-A3 seam (<c>ReconcilingSurvivors</c>): the three callers ask ONE rule. A marker blocks only when its reconcile cannot clear it — a confirmed-empty tree clears it and the launch goes on — and a home
/// is kept only by a marker that NAMES it. Everything runs on every platform over a fake process list and a marker written into this test's own lock root.
/// </summary>
public sealed class KimiSurvivorSeamTests : IDisposable
{
    private static readonly DateTime Started = new(2026, 10, 17, 8, 0, 0, DateTimeKind.Utc);

    private readonly TempDir _dir = TempDir.For("testsToDelete-survivor-seam-");

    private string Locks => _dir.At("locks");

    private string MarkerPath => Path.Combine(Locks, "0123456789abcdef.survivors.json");

    public void Dispose() => _dir.Dispose();

    private sealed class Listing(ProcessListing listing) : IProcessList
    {
        public ProcessListing Snapshot() => listing;
    }

    private ReconcilingSurvivors Over(ProcessListing listing) => new(new Listing(listing), () => Locks);

    private static ProcessListing Machine(params ProcessEntry[] entries) => new ProcessListing.Listed([new ProcessEntry(100, 1, "coai-mcp"), .. entries]);

    private static KimiSurvivorMarkerDto Spawning(string ownedRoot) =>
        new(new ReaderMarkerDto(ReaderPhases.Spawning, 424242, Started, DateTimeOffset.UtcNow), ownedRoot, "/people/sign-in");

    private static KimiSurvivorMarkerDto NamingAGoneAgent(string ownedRoot) =>
        new(new ReaderMarkerDto(ReaderPhases.Running, 424242, Started, DateTimeOffset.UtcNow, [new ProcessIdentity(2_000_000_011, Started)]), ownedRoot, "/people/sign-in");

    [Fact]
    public void TheProductsGate_IsTheReconcilingOne_NotAStandIn() =>
        KimiSurvivors.Gate.Should().BeOfType<ReconcilingSurvivors>("the seam still held its fail-closed stand-in");

    [Fact]
    public async Task NoMarker_IsNotBlocked() =>
        (await Over(Machine()).BlockedAsync(MarkerPath, TestContext.Current.CancellationToken)).Should().BeEmpty();

    [Fact]
    public async Task AMarkerNamingAGoneAgent_IsCleared_NotBlocked_AndItsFileIsDeleted()
    {
        KimiSurvivorMarker.Write(MarkerPath, NamingAGoneAgent("/owned/kh-1"));

        var blocked = await Over(Machine()).BlockedAsync(MarkerPath, TestContext.Current.CancellationToken);

        blocked.Should().BeEmpty("a marker whose member is confirmed gone blocked a launch");
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task ANamelessMarker_OverAListingWithNoKimi_IsCleared()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning("/owned/kh-1"));

        (await Over(Machine()).BlockedAsync(MarkerPath, TestContext.Current.CancellationToken)).Should().BeEmpty();
        File.Exists(MarkerPath).Should().BeFalse();
    }

    [Fact]
    public async Task ANamelessMarker_WhileAKimiRuns_IsBlocked_WithThePid_AndTheMarkerStands()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning("/owned/kh-1"));

        var blocked = await Over(Machine(new ProcessEntry(77, 1, "kimi"))).BlockedAsync(MarkerPath, TestContext.Current.CancellationToken);

        blocked.Should().Contain(MarkerPath).And.Contain("pid 77").And.Contain("safe to delete");
        File.Exists(MarkerPath).Should().BeTrue();
    }

    [Fact]
    public async Task ANamelessMarker_WhileTheListingCannotBeRead_IsBlocked_WithTheReason()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning("/owned/kh-1"));

        var blocked = await Over(new ProcessListing.Unreadable("/proc/4242: could not read exe or cmdline")).BlockedAsync(MarkerPath, TestContext.Current.CancellationToken);

        blocked.Should().Contain("/proc/4242").And.Contain(MarkerPath, "an unreadable list is never 'nobody runs'");
        File.Exists(MarkerPath).Should().BeTrue();
    }

    [Fact]
    public async Task ACancelledAsk_Throws_AndTouchesNothing()
    {
        KimiSurvivorMarker.Write(MarkerPath, NamingAGoneAgent("/owned/kh-1"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var ask = () => Over(Machine()).BlockedAsync(MarkerPath, cancelled.Token);

        await ask.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(MarkerPath).Should().BeTrue("a cancelled launch reconciles nothing");
    }

    [Fact]
    public void AHome_IsKeptByAMarkerThatNamesIt_WithThePidAndThePath()
    {
        KimiSurvivorMarker.Write(MarkerPath, NamingAGoneAgent("/owned/kh-1"));

        var kept = Over(Machine()).KeepsHome("/owned/kh-1");

        kept.Should().Contain("/owned/kh-1").And.Contain(MarkerPath).And.Contain("pid 2000000011").And.Contain("kept until the marker is cleared");
        File.Exists(MarkerPath).Should().BeTrue("asking whether a home is kept signals nothing and deletes nothing");
    }

    [Fact]
    public void AHome_IsNotKeptByAMarkerNamingAnotherHome_OrByNone()
    {
        Over(Machine()).KeepsHome("/owned/kh-1").Should().BeEmpty("no marker stands");
        KimiSurvivorMarker.Write(MarkerPath, NamingAGoneAgent("/owned/kh-2"));

        Over(Machine()).KeepsHome("/owned/kh-1").Should().BeEmpty("a marker keeps the home it names");
    }

    [Fact]
    public void AHome_IsKept_WhenAMarkerUnderTheRootCannotBeRead_WhateverHomeItNames()
    {
        Directory.CreateDirectory(Locks);
        File.WriteAllText(MarkerPath, "{ this is not json");

        var kept = Over(Machine()).KeepsHome("/owned/kh-1");

        kept.Should().Contain("/owned/kh-1").And.Contain("cannot tell").And.Contain("kept", "a marker coai cannot read may name any home; the gate's refusal of that sign-in does not protect the home from a cleanup");
    }

    [Fact]
    public void AHome_IsKept_WhenAnOversizedMarkerSitsUnderTheRoot()
    {
        Directory.CreateDirectory(Locks);
        File.WriteAllText(MarkerPath, new string('x', 70 * 1024));

        Over(Machine()).KeepsHome("/owned/kh-1").Should().Contain("cannot tell");
    }

    [Fact]
    public void AHome_IsNotKept_WhenTheLockRootIsAbsent_OrHoldsOnlyReadableMarkersOfOtherHomes()
    {
        Over(Machine()).KeepsHome("/owned/kh-1").Should().BeEmpty("no lock root: no marker can exist");
        KimiSurvivorMarker.Write(MarkerPath, NamingAGoneAgent("/owned/kh-2"));

        Over(Machine()).KeepsHome("/owned/kh-1").Should().BeEmpty();
    }

    [Fact]
    public void ANamelessMarker_KeepsItsHome_AndSaysThatNoPidIsNamed()
    {
        KimiSurvivorMarker.Write(MarkerPath, Spawning("/owned/kh-1"));

        Over(Machine()).KeepsHome("/owned/kh-1").Should().Contain("written before the agent started");
    }
}
