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
/// <para><b>Why only ONE local row leads.</b> `BoundedScheduler` takes a machine-wide slot BEFORE the
/// engine (`global → per-provider → shared resource`, deliberately — taking the engine first once
/// made a local reviewer hold an idle card while queued behind hosted vendors). So a second local row
/// inside the first `MaxConcurrency` rows would occupy a slot and then block on a card it cannot
/// have. Every local row after the first goes to the TAIL, where it takes a slot only once everything
/// else has been served. The plan round caught that the first draft left them in place.</para>
///
/// <para><b>What this promises.</b> The order reviewers are SUBMITTED in — not a wall-clock start
/// time. If another round holds the engine lease, the promoted row waits for the card like anything
/// else; what changes is that it is asked first rather than after the hosted vendors.</para>
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
    private static PanelService Service(params string[] vendors) =>
        new(new PanelSettings
        {
            DataDir = Path.Combine(Path.GetTempPath(), $"coai-local-first-{Guid.NewGuid():N}"),
            CodeWorkspace = "none",
            Providers = [.. vendors.Select(v => v.StartsWith("local", StringComparison.Ordinal)
                ? new ProviderSettings(v) { Enabled = true, Runtime = "local", Model = "m", BaseUrl = "http://127.0.0.1:11434/v1" }
                : new ProviderSettings(v) { Enabled = true, Runtime = "codex", Model = "m" })],
        }, VaultKeys.None("no vault"), default,
        new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None);

    /// <summary>The vendors in the order this round offers them, first appearance first.</summary>
    private List<string> Order(PanelService service, int seed) =>
        [.. service.BuildWork(
                [RoleCatalog.ConventionsRole, RoleCatalog.ArchitectureRole, RoleCatalog.SecurityRole, RoleCatalog.UxDxRole],
                Worktree(), "ctx", round: 1, isPlanStage: false, seed: seed)
            .Reviewers
            .Select(w => w.Invocation.Provider)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

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

    [Fact]
    public void ASecondLocalReviewer_GoesToTheTail_NotIntoTheFirstSlots()
    {
        // The trap this change would otherwise open: a machine slot held by a reviewer that cannot
        // have the card. With LocalConcurrency = 1 the second local row can only wait, so it waits
        // at the BACK, where waiting costs nothing.
        var service = Service("local", "local-two", "alpha", "bravo");

        foreach (var seed in Seeds)
        {
            var order = Order(service, seed);

            order[0].Should().StartWith("local", $"one local row leads (seed {seed})");
            order[^1].Should().StartWith("local", $"and the other one is last (seed {seed})");
            order[1..^1].Should().OnlyContain(v => !v.StartsWith("local", StringComparison.Ordinal),
                $"nothing local sits between them, where it would hold a slot it cannot use (seed {seed})");
        }
    }

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
        // Asserted AFTER the transform rather than before, which is the point: promoting one row and
        // demoting another must not reorder the hosted vendors between them, or the fairness the
        // shuffle buys is spent by this change. Raised on the plan round.
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
