using System.Collections;
using System.Text.Json;
using CoaiMcp.Core.Roles;
using CoaiMcp.Roles;
using CoaiMcp.Runners.Detach;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s4 (from the real finding of s3, <c>SIGNALS.md</c> section 6): claude 2.1.330 does NOT refuse an unknown model name — it runs the default model and bills it. So no name reaches a launch unless
/// <c>shared/role-model-names.json</c> says a CLI accepts it: for claude the four aliases and ids of the shape <c>claude-[a-z0-9.-]+</c>; for codex and agy the names of their lists. A refusal names the setting that holds the
/// name. No test here makes a paid call: the launches that would happen are counted by FakeCli, and the count is zero.
/// </summary>
[Collection("fakecli-env")]
public sealed class ARoleModelNameIsCheckedBeforeAnyLaunchTests
{
    private sealed class NothingStarted : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));
    }

    private static readonly RoleDescriptor Routed = new(FailoverProbeWork.Role, "a test-only role that asks its route's models", () => new FailoverProbeWork(TimeSpan.FromSeconds(1), []), RouteAs: "ba");

    private static RoleJobService ServiceFor(RoleJobWorld world, Dictionary<string, string?> settings)
    {
        settings.TryAdd("COAI_ROLE_LAUNCHER", "setsid");

        return new RoleJobService(world.DataDir, name => settings.GetValueOrDefault(name), world.Log, notice => { world.Notices.Enqueue(notice); return true; })
        {
            Clock = world.Clock,
            Launcher = new DetachedLauncher(new NothingStarted()),
            Descriptors = [Routed],
            EnvironmentSource = new Hashtable(),
        };
    }

    private static Task<string> Start(RoleJobService service, string repo, string? model = null) =>
        service.StartAsync(FailoverProbeWork.Role, repo, null, "write the plan", model, null, null, TestContext.Current.CancellationToken);

    private static string ErrorOf(string answer) => JsonDocument.Parse(answer).RootElement.GetProperty("error").GetString()!;

    // ---- the table ----

    [Theory]
    [InlineData("opus")]
    [InlineData("sonnet")]
    [InlineData("haiku")]
    [InlineData("fable")]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-sonnet-5-5")]
    [InlineData("claude-haiku-4-5-20251001")]
    [InlineData("claude-opus-4.8")]
    public void AClaudeAlias_OrAnIdOfTheShape_IsAccepted(string model) =>
        RoleModelNames.Refusal(model, "claude", "COAI_ROLES_MODELS").Should().BeEmpty();

    [Theory]
    [InlineData("sonet")]
    [InlineData("opuss")]
    [InlineData("Sonnet")]
    [InlineData("OPUS")]
    [InlineData("claude")]
    [InlineData("claude-")]
    [InlineData("Claude-opus-5")]
    [InlineData("claude-opus 5")]
    [InlineData("claude-OPUS-5")]
    [InlineData("claude-opus-5\n")]
    [InlineData("claude-ópus-5")]
    [InlineData("no-such-model-xyz")]
    [InlineData("")]
    public void AClaudeTypo_IsRefused_NamingTheKey_AndSayingWhyItMatters(string model)
    {
        var refusal = RoleModelNames.Refusal(model, "claude", "COAI_ROLES_MODELS");

        refusal.Should().Contain($"model '{model}'").And.Contain("COAI_ROLES_MODELS").And.Contain("opus, sonnet, haiku, fable").And.Contain("claude-[a-z0-9.-]+").And.Contain("bills it");
    }

    [Fact]
    public void ACodexOrAgyName_IsAccepted_OnlyFromItsOwnList()
    {
        RoleModelNames.Refusal("astra", "codex", "k").Should().BeEmpty();
        RoleModelNames.Refusal("gpt-6-astra", "codex", "k").Should().BeEmpty();
        RoleModelNames.Refusal("gemini-3.8-flash-low", "agy", "k").Should().BeEmpty();
        RoleModelNames.Refusal("gemini-3.8-flash-low", "codex", "k").Should().Contain("not in the list of names known for the codex CLI").And.Contain("fix it in k");
        RoleModelNames.Refusal("astra", "agy", "k").Should().Contain("not in the list of names known for the agy CLI");
        RoleModelNames.Refusal("gemini-3.7-flash-high", "agy", "k").Should().Contain("fix it in k", "agy 1.4.4 refuses 3.7 before any model call");
    }

    [Fact]
    public void ADifferentVendor_IsNotJudgedHere_TheLauncherRefusesIt()
    {
        RoleModelNames.Refusal("anything", "kimi", "k").Should().BeEmpty();
    }

    [Fact]
    public void TheFile_HoldsWhatItsSourceSays_EachMeasuredNameIsListed_AndEveryKnownIdMatchesTheShape()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.Root(), "shared", "role-model-names.json")));
        var root = document.RootElement;
        var table = RoleModelNames.Current;

        foreach (var vendor in new[] { "codex", "agy" })
        {
            var names = root.GetProperty(vendor).GetProperty("names").EnumerateArray().Select(name => name.GetString()!).ToList();
            root.GetProperty(vendor).GetProperty("measured").EnumerateArray().Select(name => name.GetString()!).Should().OnlyContain(name => names.Contains(name), $"a measured {vendor} name is on its list");
            table.NamesOf(vendor).Should().Equal(names);
        }

        table.ClaudeKnownIds.Should().OnlyContain(id => table.ClaudeAccepts(id));
        table.ClaudeAliases.Should().Equal("opus", "sonnet", "haiku", "fable");
    }

    [Fact]
    public void EveryShippedDefault_PassesTheCheck_SoAFreshInstallIsNeverRefused()
    {
        foreach (var vendor in RoleDefaults.Current.MainAi.Values)
        {
            foreach (var (role, models) in vendor.Roles)
            {
                models.Where(model => RoleModelNames.Refusal(model.Model, model.Vendor, "defaults").Length > 0).Should().BeEmpty($"role {role}");
            }

            vendor.Bands.Where(band => RoleModelNames.Refusal(band.Model, band.Vendor, "defaults").Length > 0).Should().BeEmpty();
        }
    }

    // ---- start_role: refused before a folder is made ----

    [Fact]
    public async Task ATypoInCOAI_ROLES_MODELS_IsRefusedAtTheStart_NamingTheKey_AndNoFolderIsMade()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world, new Dictionary<string, string?> { ["COAI_ROLES_MODELS"] = """{"ba":[{"model":"sonet","vendor":"claude"},{"model":"opus","vendor":"claude"}]}""" });

        var error = ErrorOf(await Start(service, world.DataDir));

        error.Should().Contain("model 'sonet'").And.Contain("COAI_ROLES_MODELS").And.Contain("bills it");
        world.Store.JobIds().Should().BeEmpty("a refused start makes no folder and no runner");
        Directory.Exists(world.Store.JobsRoot).Should().BeFalse();
    }

    [Theory]
    [InlineData("sonet")]
    [InlineData("sonnet9")]
    [InlineData("opus-5")]
    public async Task ATypoInAnExplicitStartModel_IsRefused_NamingTheArgument_AndNoFolderIsMade(string model)
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world, []);

        var error = ErrorOf(await Start(service, world.DataDir, model));

        error.Should().Contain($"'{model}'").And.Contain("model argument of start_role");
        world.Store.JobIds().Should().BeEmpty();
    }

    [Fact]
    public async Task ARightName_IsStillAccepted_AliasOrFullId()
    {
        using var world = new RoleJobWorld();
        var service = ServiceFor(world, []);

        JsonDocument.Parse(await Start(service, world.DataDir, "sonnet")).RootElement.TryGetProperty("error", out _).Should().BeFalse();
        var full = JsonDocument.Parse(await Start(service, world.DataDir, "claude-opus-5-5")).RootElement;
        full.TryGetProperty("error", out _).Should().BeFalse();
        full.GetProperty("route")[0].GetString().Should().Be("claude/claude-opus-5-5");
    }

    // ---- the runner: a route that holds a bad name launches nothing ----

    [Fact]
    public async Task AJobWhoseRouteHoldsATypo_LaunchesNothing_AndFailsNamingTheName()
    {
        await using var rig = await RoleFailoverRig.CreateAsync();
        rig.Says("sonet", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok)).Says("opus", FakeRoleAnswers.ClaudeSays(FakeRoleAnswers.Ok));
        var job = rig.NewJob(["claude/opus", "claude/sonet"]);

        var (exit, after) = await rig.RunAsync(job);

        exit.Should().Be(RoleRunner.Done);
        after.State.Should().Be(RoleJobStates.Failed);
        after.Stop.Should().Be("route-invalid");
        after.Reason.Should().Contain("model 'sonet'").And.Contain("nothing was launched");
        rig.Launches().Should().BeEmpty("not even the valid model before the typo was started: the route is checked whole, before the first launch");
        rig.LedgerLines().Should().BeEmpty();
    }
}
