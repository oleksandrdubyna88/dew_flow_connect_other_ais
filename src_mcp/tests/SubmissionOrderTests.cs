using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Two clients do not offer their reviewers to a Team server in the same order.
/// </summary>
/// <remarks>
/// <para><b>The symptom.</b> `BuildWork` builds a round vendor-major, in the order the vendors sit
/// in the settings, and `BoundedScheduler` starts one task per row against a single semaphore —
/// which hands out slots in the order they were asked for. So the list's order is the order
/// reviewers reach a Team server, and every client ships the same default vendor list. Ten people
/// starting a round at nine in the morning all queue for the FIRST vendor's shared accounts, wait,
/// and only then ask for the second — whose accounts were idle throughout.</para>
/// <para><b>Where the fix does not go.</b> The server's queue is fair already: `JobStore.TryClaim`
/// is FIFO and a job it never received cannot be claimed early. A fair queue fed in a biased order
/// is fixed at the feeding end, so nothing on the server changes.</para>
/// <para><b>Seeded, not random.</b> The seed is the one the round already has — FNV over the
/// session id and the round number — so two sessions differ while one session replays. A second
/// source of randomness would have made a round unreproducible to save nothing.</para>
/// </remarks>
public sealed class SubmissionOrderTests : IDisposable
{
    private static PanelService Service(params string[] vendors) =>
        new(new PanelSettings
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"coai-order-{Guid.NewGuid():N}"),
            CodeWorkspace = "none",
            Providers = [.. vendors.Select(v =>
                new ProviderSettings(v) { Enabled = true, Runtime = "local", Model = "m" })],
        }, VaultKeys.None("no vault"), default,
        new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);

    /// <summary>
    /// One root for the whole class, swept when it ends.
    /// </summary>
    /// <remarks>
    /// The distribution test builds a hundred rounds, and a directory per round left a hundred
    /// folders in the temp directory on every run — found on the code round. `IDisposable` on the
    /// class is xUnit's own teardown, so the sweep happens whether the assertions pass or throw.
    /// </remarks>
    private readonly string _root = Directory.CreateTempSubdirectory("coai-order-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A temp directory that outlives one run is litter, not a failed test.
        }
    }

    private string Worktree()
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A handful of session ids, shared rather than rebuilt per call.</summary>
    private static readonly string[] Sessions =
        ["s-alpha", "s-bravo", "s-charlie", "s-delta", "s-echo", "s-foxtrot"];

    /// <summary>The vendors in the order this round would offer them, first appearance first.</summary>
    private List<string> ProviderOrder(int seed)
    {
        var work = Service("alpha", "bravo", "charlie", "delta").BuildWork(
            [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole],
            Worktree(), "ctx", round: 1, isPlanStage: false, seed: seed).Reviewers;

        return [.. work.Select(w => w.Invocation.Provider).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    [Fact]
    public void TwoSessions_DoNotAskTheSameVendorFirst()
    {
        // Seeds as they actually arrive: PanelService.StableSeed over a session id and a round.
        var orders = Sessions
            .Select(id => string.Join(",", ProviderOrder(PanelService.StableSeed(id, 1))))
            .ToList();

        orders.Distinct().Should().HaveCountGreaterThan(1,
            "every client shipping the same vendor list must not queue for the same accounts first");
    }

    /// <summary>
    /// The load is SPREAD, which is a weaker claim than "no two clients collide" — and the true one.
    /// </summary>
    /// <remarks>
    /// <para>Three reviewers on the plan round said the same thing from three angles, and they were
    /// right: a seeded shuffle cannot guarantee distinct orders. With two vendors there are two
    /// possible orders, so half of all client pairs collide however good the hash is; with three
    /// there are six. The plan claimed clients "get different orders" and that overstated it.</para>
    /// <para>What the change actually buys is a distribution instead of a constant, and two vendors
    /// is the case worth measuring because it is the common one and the least favourable. So the
    /// assertion is that no single order takes more than 70 % of a hundred sessions — comfortably
    /// above the 50 % a fair coin gives and far below the 100 % of today's behaviour, which is what
    /// this test would have caught.</para>
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void AcrossManySessions_NoSingleOrderDominates(int vendorCount)
    {
        string[] pool = ["alpha", "bravo", "charlie"];
        var vendors = pool.Take(vendorCount).ToArray();

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < 100; i++)
        {
            var work = Service(vendors).BuildWork(
                [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole],
                Worktree(), "ctx", round: 1, isPlanStage: false,
                seed: PanelService.StableSeed($"session-{i}", 1)).Reviewers;

            var order = string.Join(",", work.Select(w => w.Invocation.Provider).Distinct(StringComparer.Ordinal));
            counts[order] = counts.GetValueOrDefault(order) + 1;
        }

        counts.Keys.Should().HaveCountGreaterThan(1, "one order for every session is the defect itself");
        counts.Values.Max().Should().BeLessThan(70,
            $"with {vendorCount} vendors the orders must be spread rather than concentrated");
    }

    [Fact]
    public void OneSession_ReplaysToTheSameOrder()
    {
        // The property the deal already has and this must not cost: a round is reproducible, and an
        // audit log that names a seed must name one somebody can reuse.
        var seed = PanelService.StableSeed("s-alpha", 1);

        ProviderOrder(seed).Should().Equal(ProviderOrder(seed));
    }

    [Fact]
    public void NoVendorIsStarvedByTheShuffle_WhateverTheSeed()
    {
        // Reordering that drops or duplicates a row would be a far worse bug than the one being
        // fixed, and it is the failure mode a hand-written shuffle actually has.
        foreach (var id in Sessions)
        {
            var work = Service("alpha", "bravo", "charlie").BuildWork(
                [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole],
                Worktree(), "ctx", round: 1, isPlanStage: false, seed: PanelService.StableSeed(id, 1)).Reviewers;

            work.Select(w => $"{w.Invocation.Provider}|{w.Invocation.Role}")
                .Order(StringComparer.Ordinal)
                .Should().Equal(
                    "alpha|Architecture", "alpha|Conventions",
                    "bravo|Architecture", "bravo|Conventions",
                    "charlie|Architecture", "charlie|Conventions");
        }
    }

    [Fact]
    public void ARoundWithOneVendor_IsNotReorderedIntoAnything()
    {
        var work = Service("only").BuildWork(
            [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole],
            Worktree(), "ctx", round: 1, isPlanStage: false, seed: 12345).Reviewers;

        work.Select(w => w.Invocation.Role).Should().Equal(
            [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole],
            "the roles of one vendor keep the order the round asked for them in");
    }
}
