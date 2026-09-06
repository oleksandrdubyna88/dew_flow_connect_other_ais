using Xunit;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A vendor reviews the stages it is set to review, and a stage with nobody left is refused.
/// </summary>
/// <remarks>
/// <para>Asked for by a measurement rather than by taste. Fourteen judged runs over two cases
/// (<c>research/RESULTS_vendor_overlap_2026-09-06.md</c>): the local model was <b>19 % useful on a
/// plan and 3 % on code</b>, while writing more findings than codex and gemini together. So the
/// setting somebody wants is not "local on or off" — it is on for the plan and off for the code, and
/// until these flags existed there was nowhere to say it.</para>
/// <para>The dangerous half is the second test. A stage that no vendor serves used to build an empty
/// work list, run no reviewer, merge no findings, and pass the gate — a round that reported
/// <c>proceed</c> having reviewed nothing at all.</para>
/// </remarks>
public sealed class VendorStagesTests
{
    private static PanelService Service(params ProviderSettings[] providers) =>
        new(
            new PanelSettings
            {
                DataDir = Path.Combine(Path.GetTempPath(), $"coai-stages-{Guid.NewGuid():N}"),
                Providers = providers,
            },
            VaultKeys.None("no vault"),
            default,
            new Runners.Processes.ProcessLauncher(),
            Serilog.Core.Logger.None);

    private static string Scratch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coai-stages-wt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        return path;
    }

    private static ProviderSettings Local(bool plan, bool code) =>
        new("local") { Enabled = true, Runtime = "local", Model = "qwen", Plan = plan, Code = code };

    [Fact]
    public void AVendorNarrowedToPlans_IsNotLaunchedForACodeRound()
    {
        var work = Service(Local(plan: true, code: false))
            .BuildWork([ReviewRole.Architecture], Scratch(), "ctx", round: 1);

        work.Should().BeEmpty("the only vendor configured does not review code");
    }

    [Fact]
    public void TheSameVendor_IsLaunchedForAPlanRound()
    {
        var work = Service(Local(plan: true, code: false))
            .BuildWork([ReviewRole.PlanCritique], Scratch(), "ctx", round: 1, planPrompts: ["universal"]);

        work.Should().NotBeEmpty("it reviews plans, which is the whole point of the setting");
    }

    [Fact]
    public void AVendorNarrowedToCode_IsNotLaunchedForAPlanRound()
    {
        var work = Service(Local(plan: false, code: true))
            .BuildWork([ReviewRole.PlanCritique], Scratch(), "ctx", round: 1, planPrompts: ["universal"]);

        work.Should().BeEmpty();
    }

    [Fact]
    public void AVendorWithNoFlagsSet_ReviewsBothStages()
    {
        // The update path: a settings file written before this feature existed must keep the gate it
        // had. `ProviderSettings` defaults both to true and `ParseVendors` reads an absent flag as
        // true, so an old configuration reviews exactly what it reviewed yesterday.
        var both = new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "qwen" };

        Service(both).BuildWork([ReviewRole.Architecture], Scratch(), "ctx", round: 1)
            .Should().NotBeEmpty();
        Service(both).BuildWork([ReviewRole.PlanCritique], Scratch(), "ctx", round: 1, planPrompts: ["universal"])
            .Should().NotBeEmpty();
    }

    [Fact]
    public async Task AStageNoVendorServes_IsRefused_NotPassedWithNothingReviewed()
    {
        // The dangerous half. An empty work list ran no reviewer, merged no findings and passed the
        // gate, so the round answered `proceed` having reviewed nothing at all. That was unreachable
        // until a vendor could be narrowed to one stage; now it is one checkbox away. Asked of the
        // PLAN stage, because that is the one a fresh session can run.
        var repo = await Repository();
        var service = Service(Local(plan: false, code: true));
        await service.OpenAsync(repo, "feature");

        var answer = await service.ReviewPlanAsync(repo, "feature", "PLAN - something to judge");

        answer.Should().Contain("no reviewer serves").And.Contain("reviewed nothing");
        answer.Should().NotContain("proceed");
    }

    /// <summary>A real git repository with a branch, which is the least a round will accept.</summary>
    private static async Task<string> Repository()
    {
        var path = Directory.CreateTempSubdirectory("coai-stages-repo-").FullName;
        await Git(path, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(path, "app.cs"), "v1" + Environment.NewLine);
        await Git(path, "add", ".");
        await Git(path, "-c", "user.email=t@t", "-c", "user.name=t", "commit", "-m", "base");
        await Git(path, "checkout", "-b", "feature");

        return path;
    }

    private static async Task Git(string repo, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in args)
        {
            start.ArgumentList.Add(argument);
        }

        using var git = System.Diagnostics.Process.Start(start)!;
        await git.WaitForExitAsync();
    }
}
