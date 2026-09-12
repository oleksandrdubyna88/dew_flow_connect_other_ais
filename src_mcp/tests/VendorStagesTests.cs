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
[Collection("fakecli-env")]
public sealed class VendorStagesTests
{
    private static PanelService Service(params ProviderSettings[] providers) =>
        Service(Path.Combine(Path.GetTempPath(), $"coai-stages-{Guid.NewGuid():N}"), providers);

    /// <summary>Two services over one data directory is how a second round reads the first's session.</summary>
    private static PanelService Service(string dataDir, params ProviderSettings[] providers) =>
        new(
            new PanelSettings
            {
                DataDir = dataDir,
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
            .BuildWork([RoleCatalog.ArchitectureRole], Scratch(), "ctx", round: 1, isPlanStage: false).Reviewers;

        work.Should().BeEmpty("the only vendor configured does not review code");
    }

    [Fact]
    public void TheSameVendor_IsLaunchedForAPlanRound()
    {
        var work = Service(Local(plan: true, code: false))
            .BuildWork([RoleCatalog.PlanRole], Scratch(), "ctx", round: 1, isPlanStage: true).Reviewers;

        work.Should().NotBeEmpty("it reviews plans, which is the whole point of the setting");
    }

    [Fact]
    public void AVendorNarrowedToCode_IsNotLaunchedForAPlanRound()
    {
        var work = Service(Local(plan: false, code: true))
            .BuildWork([RoleCatalog.PlanRole], Scratch(), "ctx", round: 1, isPlanStage: true).Reviewers;

        work.Should().BeEmpty();
    }

    [Fact]
    public void AVendorWithNoFlagsSet_ReviewsBothStages()
    {
        // The update path: a settings file written before this feature existed must keep the gate it
        // had. `ProviderSettings` defaults both to true and `ParseVendors` reads an absent flag as
        // true, so an old configuration reviews exactly what it reviewed yesterday.
        var both = new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "qwen" };

        Service(both).BuildWork([RoleCatalog.ArchitectureRole], Scratch(), "ctx", round: 1, isPlanStage: false).Reviewers
            .Should().NotBeEmpty();
        Service(both).BuildWork([RoleCatalog.PlanRole], Scratch(), "ctx", round: 1, isPlanStage: true).Reviewers
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

        answer.Should().Contain("nothing could review").And.Contain("reviewed nothing");
        answer.Should().Contain("Otherwise every configured vendor is disabled",
            "the vendor advice stays actionable, and is worded as a check rather than a diagnosis: "
            + "a healthy Team server rejecting one custom role would make the assertion false");
        answer.Should().NotContain("proceed");
    }

    [Fact]
    public async Task TheCodeStageIsRefusedTheSameWay_WhichIsTheCaseThisFeatureIsFor()
    {
        // The gate asked for this, and it was right: the refusal was proven through review_plan only,
        // while the PRIMARY use of the feature is the other direction - local serves plans, so a code
        // round has nobody. Driven through the real tools with a scripted CLI, because reaching the
        // code stage means a plan round that actually PROCEEDED.
        var repo = await Repository();
        var data = Path.Combine(Path.GetTempPath(), $"coai-stages-{Guid.NewGuid():N}");
        var fake = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", CleanAnswer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", CleanAnswer);
        try
        {
            var plans = Service(data, new ProviderSettings("codex")
            {
                Enabled = true, Plan = true, Code = false, ExecutablePath = fake,
            });
            await plans.OpenAsync(repo, "feature");

            var plan = await plans.ReviewPlanAsync(repo, "feature", "PLAN - something to judge");
            plan.Should().Contain("proceed", "a clean answer from the one vendor that serves plans");
            // Nothing to decide, and the stage still advances only when a decision is recorded.
            await plans.ResolveAsync(repo, "feature", "[]");

            var code = await plans.ReviewCodeAsync(repo, "feature", "main", CodeScope);

            code.Should().Contain("nothing could review").And.Contain("reviewed nothing");
            code.Should().Contain("Otherwise every configured vendor is disabled");
            code.Should().NotContain("\"verdict\"");
        }
        finally
        {
            foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT"])
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    /// <summary>A reviewer that finds nothing, so the plan round reaches `proceed` in one pass.</summary>
    private const string CleanAnswer = """{"findings":[]}""";

    /// <summary>The code stage has a floor on its scope, and it is right to: a diff without the
    /// intent behind it can only be judged as "is this defensible", never as "is this what was
    /// asked for".</summary>
    private const string CodeScope = """
        SCOPE - the vendor stage flags.
        GOAL: a vendor can be set to review plans, code, or both, and a stage nobody serves is
        refused rather than passed with nothing reviewed.
        WHAT MUST BE TRUE: an existing configuration keeps the gate it had; the flags travel inside
        the vendor list; the stage is told to BuildWork rather than guessed from its arguments.
        CONSTRAINTS: no new environment variable, and the env block still carries only what differs.
        """;

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
