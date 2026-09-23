using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round with a local reviewer asks it FIRST, because it is the slowest thing in the round.
/// </summary>
/// <remarks>
/// <para><b>The symptom (issue #155).</b> `BuildWork` orders vendors with a seeded shuffle, and that
/// order is the order reviewers reach the scheduler — the slots free when a round opens are taken in
/// list order. The shuffle is there for Team-account fairness, so it puts the local engine anywhere;
/// started last, the round's wall-clock becomes the hosted reviewers finishing quickly and then
/// everybody waiting for the local one to begin.</para>
///
/// <para><b>Every local row leads, as one group (2026-09-23).</b> Until then only ONE did, and the
/// rest went to the tail: `BoundedScheduler` made a local reviewer take a machine slot before its
/// engine, so a second local row near the front would have held a slot while blocked on the card.
/// That same tail is what made `local/2..4` wait behind every hosted reviewer although the card was
/// idle — the half of issue #155 the operator restated. Local reviewers now have their own lane,
/// bounded only by their engine (`SharedEngineTests`), so there is no machine slot to hold and no
/// reason to keep them apart; at the head they meet the engine queue in order, with no hosted
/// launch in between. See `research/PLAN_the_local_reviewers_have_their_own_lane.md`.</para>
///
/// <para><b>What this promises.</b> The order reviewers are SUBMITTED in. The start times are the
/// scheduler's, and pinned there.</para>
/// </remarks>
public sealed class TheLocalReviewerIsAskedFirstTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("coai-local-first-").FullName;

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

    /// <summary>A vendor list where `local*` names are local engines and the rest are hosted.</summary>
    /// <remarks>
    /// The data directory lives under this class's own <c>_root</c>, so the teardown sweeps it. A
    /// fresh GUID directory under the system temp path — which is what this took first, copying a
    /// sibling test — is a new store per service that nothing ever deletes, and the rule against
    /// minting a per-run identity in a store runs share is about exactly that litter. Raised on the
    /// code round.
    /// </remarks>
    private PanelService Service(params string[] vendors) =>
        new(new PanelSettings
        {
            DataDir = Path.Combine(_root, $"data-{Guid.NewGuid():N}"),
            CodeWorkspace = "none",
            Providers = [.. vendors.Select(v => v.StartsWith("local", StringComparison.Ordinal)
                ? new ProviderSettings(v) { Enabled = true, Runtime = "local", Model = "m", BaseUrl = "http://127.0.0.1:11434/v1" }
                : new ProviderSettings(v) { Enabled = true, Runtime = "codex", Model = "m" })],
        }, VaultKeys.None("no vault"), default,
        new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);

    /// <summary>
    /// Every reviewer ROW in the order this round submits them — one per (vendor, role).
    /// </summary>
    /// <remarks>
    /// Rows, not distinct vendors. The first version of this helper deduplicated by provider, and
    /// that is precisely what hid the defect the code round found: reordering the PROVIDER list puts
    /// a local vendor's four roles at the head as four consecutive rows, which fills every machine
    /// slot with reviewers that cannot run. Through <c>Distinct()</c> that reads as "local is first",
    /// which is true and useless.
    /// </remarks>
    private List<string> Rows(PanelService service, int seed) =>
        [.. service.BuildWork(
                [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole],
                Worktree(), "ctx", round: 1, stage: Stage.CodeReview, readsCheckout: true, seed: seed)
            .Reviewers
            .Select(w => w.Invocation.Provider)];

    /// <summary>The vendors in the order this round first mentions them.</summary>
    private List<string> Order(PanelService service, int seed) =>
        [.. Rows(service, seed).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Several seeds, so a pass cannot be the shuffle agreeing by accident.</summary>
    private static readonly int[] Seeds =
        [.. new[] { "s-alpha", "s-bravo", "s-charlie", "s-delta", "s-echo", "s-foxtrot" }
            .Select(id => PanelService.StableSeed(id, 1))];

    [Fact]
    public void AMixedRound_AsksTheLocalReviewerFirst_WhateverTheSeed()
    {
        var service = Service("local", "alpha", "bravo", "charlie");

        foreach (var seed in Seeds)
        {
            Order(service, seed)[0].Should().Be("local",
                $"the slowest reviewer starts the round (seed {seed})");
        }
    }

    /// <summary>
    /// Every ROW of a local vendor leads, however many roles it serves.
    /// </summary>
    /// <remarks>
    /// Rows, as the helper above explains: one local vendor serving four roles is four rows, and all
    /// four meet the engine before any hosted row is launched.
    /// </remarks>
    [Fact]
    public void EveryLocalRow_LeadsTheRound()
    {
        var service = Service("local", "alpha", "bravo");

        foreach (var seed in Seeds)
        {
            var rows = Rows(service, seed);
            var locals = rows.Count(IsLocal);

            locals.Should().Be(4, "one local vendor, four roles");
            rows.Take(locals).Should().OnlyContain(v => IsLocal(v),
                $"every local row is ahead of every hosted one (seed {seed})");
        }
    }

    [Fact]
    public void TwoLocalVendors_BothLead_InTheShufflesOrder()
    {
        // The order they meet the ONE engine in is the order they run in, so it is still the
        // shuffle's — two local vendors are the Team-account case in miniature.
        var service = Service("local", "local-two", "alpha", "bravo");

        foreach (var seed in Seeds)
        {
            var order = Order(service, seed);
            var shuffledLocals = SeededShuffle
                .Of<string>(["local", "local-two", "alpha", "bravo"], seed)
                .Where(IsLocal)
                .ToList();

            order.Take(2).Should().Equal(shuffledLocals, $"both local vendors lead, as shuffled (seed {seed})");
            order.Skip(2).Should().OnlyContain(v => !IsLocal(v), $"the hosted ones follow (seed {seed})");
        }
    }

    private static bool IsLocal(string vendor) => vendor.StartsWith("local", StringComparison.Ordinal);

    [Fact]
    public void ARoundWithNoLocalReviewer_IsOrderedExactlyAsItWasBefore()
    {
        // The Team-account fairness this ordering exists for, asserted to be untouched: with no local
        // vendor the list must be the raw seeded shuffle, element for element.
        var service = Service("alpha", "bravo", "charlie", "delta");

        foreach (var seed in Seeds)
        {
            var expected = SeededShuffle.Of<string>(["alpha", "bravo", "charlie", "delta"], seed);

            Order(service, seed).Should().Equal(expected, $"the shuffle decides on its own (seed {seed})");
        }
    }

    [Fact]
    public void TheHostedVendorsKeepTheirShuffledOrderRelativeToEachOther()
    {
        // Asserted AFTER the transform rather than before, which is the point: moving the local rows
        // to the head must not reorder the hosted vendors behind them, or the fairness the shuffle
        // buys is spent by this change. Raised on the 2026-09-12 plan round.
        var service = Service("local", "alpha", "bravo", "charlie");

        foreach (var seed in Seeds)
        {
            var hosted = Order(service, seed).Where(v => v != "local").ToList();
            var shuffledHosted = SeededShuffle
                .Of<string>(["local", "alpha", "bravo", "charlie"], seed)
                .Where(v => v != "local")
                .ToList();

            hosted.Should().Equal(shuffledHosted, $"the hosted order is the shuffle's, still (seed {seed})");
        }
    }
}
