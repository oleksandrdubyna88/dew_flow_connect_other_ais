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
public class SubmissionOrderTests
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

    private static string Worktree()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coai-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The vendors in the order this round would offer them, first appearance first.</summary>
    private static List<string> ProviderOrder(int seed)
    {
        var work = Service("alpha", "bravo", "charlie", "delta").BuildWork(
            [ReviewRole.Conventions, ReviewRole.Architecture, ReviewRole.SecurityReliability, ReviewRole.UxDxPerformance],
            Worktree(), "ctx", round: 1, isPlanStage: false, seed: seed);

        return [.. work.Select(w => w.Invocation.Provider).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    [Fact]
    public void TwoSessions_DoNotAskTheSameVendorFirst()
    {
        // Seeds as they actually arrive: PanelService.StableSeed over a session id and a round.
        var orders = new[] { "s-alpha", "s-bravo", "s-charlie", "s-delta", "s-echo", "s-foxtrot" }
            .Select(id => string.Join(",", ProviderOrder(PanelService.StableSeed(id, 1))))
            .ToList();

        orders.Distinct().Should().HaveCountGreaterThan(1,
            "every client shipping the same vendor list must not queue for the same accounts first");
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
        foreach (var id in (string[])["s-alpha", "s-bravo", "s-charlie", "s-delta", "s-echo"])
        {
            var work = Service("alpha", "bravo", "charlie").BuildWork(
                [ReviewRole.Conventions, ReviewRole.Architecture],
                Worktree(), "ctx", round: 1, isPlanStage: false, seed: PanelService.StableSeed(id, 1));

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
            [ReviewRole.Conventions, ReviewRole.Architecture],
            Worktree(), "ctx", round: 1, isPlanStage: false, seed: 12345);

        work.Select(w => w.Invocation.Role).Should().Equal(
            [ReviewRole.Conventions, ReviewRole.Architecture],
            "the roles of one vendor keep the order the round asked for them in");
    }
}
