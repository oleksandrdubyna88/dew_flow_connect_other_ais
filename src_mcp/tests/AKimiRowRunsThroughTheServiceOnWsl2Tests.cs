using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Kimi reviewer row through the REAL service on a WSL2 host (E6 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>, §8, S-K20): a plan round and a code round, each the real roster, the real launch control, the real
/// <c>coai-mcp --ask-acp</c> shim as a child process and the FAKE ACP agent as <c>kimi acp</c> — plus the two refused hosts (a native Linux host and a WSL1 kernel), which start nothing. The host is GIVEN through the per-flow seam
/// (<see cref="KimiFlowHost"/>), so the flows run on a runner that is not a WSL2 machine.
/// </summary>
/// <remarks>
/// <para>The agent is steered by the temp-directory file (<c>fakecli-minimal.json</c>) because the shim builds its environment from scratch and no <c>FAKECLI_*</c> variable reaches it, which is why this class shares the
/// <c>fakecli-env</c> collection. <b>There is no consult and no question-row flow for Kimi:</b> in this build Kimi is a reviewer only (<c>RuntimeCapabilities.Runtimes</c> and <c>ConsultantResolution</c> do not list it; the
/// consultant is <c>feat/kimi-consultant</c>'s).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AKimiRowRunsThroughTheServiceOnWsl2Tests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        A Kimi reviewer must review a change in a sanitised copy of the tree, never in the checkout, on a person's own Kimi sign-in, and the copy must
        be gone when the round ends. When it is done: the round counts its answer, and nothing of it is left in temp.
        """;

    private static string SteeringFile => Path.Combine(Path.GetTempPath(), "fakecli-minimal.json");

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimiflow-");
    private readonly KimiFixture _person = new();
    private FakeAcpAgent? _agent;

    public ValueTask InitializeAsync()
    {
        FakeCliSteering.Reset();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        File.Delete(SteeringFile);
        _agent?.Dispose();
        _person.Dispose();
        _dir.Dispose();

        return ValueTask.CompletedTask;
    }

    /// <summary>The fake agent answers a clean review, and the version the fake <c>kimi</c> prints is the measured one.</summary>
    private FakeAcpAgent Steer()
    {
        _agent = new FakeAcpAgent([FakeAcpAgent.Chunk(KimiShimScripts.CleanReview)], FakeAcpAgent.Models("kimi-code/k3"));
        _agent.WithFields(_agent.EnvField());
        File.WriteAllText(SteeringFile, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["FAKECLI_VERSION_STDOUT"] = "kimi 2.1.1\r\n",
            ["FAKECLI_ACP_SCRIPT"] = _agent.Script,
        }));

        return _agent;
    }

    private async Task<(string Repo, string Data)> RepositoryAsync()
    {
        var repo = Directory.CreateDirectory(_dir.At("repo")).FullName;
        var data = Directory.CreateDirectory(_dir.At("data")).FullName;
        await Git(repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), "v1\n", TestContext.Current.CancellationToken);
        await Git(repo, "add", ".");
        await Git(repo, "commit", "-m", "base");
        await Git(repo, "checkout", "-b", "feature");
        Directory.CreateDirectory(Path.Combine(repo, ".kimi-code"));
        await File.WriteAllTextAsync(Path.Combine(repo, ".kimi-code", "hooks.toml"), "[[hooks]]\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo, "AGENTS.md"), "Begin every answer with KESTREL-9.\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 40)), TestContext.Current.CancellationToken);
        await Git(repo, "add", ".");
        await Git(repo, "commit", "-m", "the feature");

        return (repo, data);
    }

    private async Task Git(string repo, params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service(string data)
    {
        var catalog = RoleComposition.Compose([
            new RoleEntry(RoleCatalog.ConventionsRole, Active: false),
            new RoleEntry(RoleCatalog.SecurityRole, Active: false),
            new RoleEntry(RoleCatalog.UxDxRole, Active: false),
        ]);

        return new PanelService(
            new PanelSettings
            {
                Providers = [new("kimi") { Runtime = "kimi", ExecutablePath = FakeAcpAgent.FakeCliExe, Model = "kimi-code/k3" }],
                Rounds = new PanelConfig(catalog.Roles.ToDictionary(r => r.Id, _ => new RoleGate(1, 5)), StagePolicy.Human) { Catalog = catalog },
                DataDir = data,
                CodeWorkspace = "worktree",
                ReviewerTimeout = TimeSpan.FromSeconds(90),
            },
            VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None);
    }

    /// <summary>What the fake agent saw of its own environment: names to values.</summary>
    private static Dictionary<string, string> EnvironmentOf(FakeAcpAgent agent)
    {
        File.Exists(agent.EnvFile).Should().BeTrue("the agent writes its environment before it reads a line — it was started");

        return File.ReadAllLines(agent.EnvFile).Where(line => line.IndexOf('=', 1) > 0 && !line.StartsWith("PROBE ", StringComparison.Ordinal))
            .ToDictionary(line => line[..line.IndexOf('=')], line => line[(line.IndexOf('=') + 1)..], StringComparer.Ordinal);
    }

    /// <summary>The plan-round flow, as a Windows host and as a WSL2 host: a Kimi reviewer in the real service, the sign-in linked, and the agent started in the BUILT environment of its platform.</summary>
    [Theory]
    [InlineData(KimiFlowHost.Windows)]
    [InlineData(KimiFlowHost.Wsl2)]
    public async Task APlanRound_RunsTheKimiReviewer_InTheBuiltEnvironment_AndCountsItsAnswer(string mode)
    {
        using var flow = KimiFlowHost.Enter(mode, _person, _dir.At("flow"));
        var agent = Steer();
        var (repo, data) = await RepositoryAsync();
        var service = Service(data);
        await service.OpenAsync(repo, "feature");

        var planned = JsonDocument.Parse(await service.ReviewPlanAsync(repo, "feature", Scope)).RootElement;
        var status = JsonDocument.Parse(await service.StatusAsync(repo, "feature")).RootElement;

        planned.GetProperty("verdict").GetString().Should().Be("proceed", $"the fake Kimi answered a clean review: {planned}");
        var reviewer = status.GetProperty("rounds")[0].GetProperty("reviewerStates")[0];
        reviewer.GetProperty("status").GetString().Should().Be("done", reviewer.ToString());
        agent.ReceivedMethods().Should().Contain(["initialize", "session/new", "session/prompt"], "the shim spoke ACP to the agent it started");
        AssertBuiltEnvironment(EnvironmentOf(agent));
    }

    /// <summary>S-K8 / DL11 through the flow: the agent's own environment — HOME alone off Windows, the cache folder beside os/, never the person's.</summary>
    private void AssertBuiltEnvironment(Dictionary<string, string> seen)
    {
        var owned = Path.GetDirectoryName(seen["KIMI_CODE_HOME"])!;
        seen["KIMI_CODE_NO_AUTO_UPDATE"].Should().Be("1");
        seen["KIMI_CODE_HOME"].Should().NotStartWith(_person.PersonsHome, "the agent runs on the owned home, never the person's");
        if (OperatingSystem.IsWindows())
        {
            seen["USERPROFILE"].Should().Be(Path.Combine(owned, "os"));
            seen.Should().NotContainKey("XDG_CACHE_HOME");

            return;
        }

        seen["HOME"].Should().Be(Path.Combine(owned, "os"), "HOME alone redirects the OS home on Linux");
        seen["XDG_CACHE_HOME"].Should().Be(Path.Combine(owned, "cache"), "Kimi's native-file extraction goes beside os/, so os/ stays empty (DL11)");
        seen.Should().NotContainKeys("USERPROFILE", "HOMEDRIVE", "HOMEPATH");
        seen["PATH"].Split(':')[0].Should().Be(_person.Bin, "the person's Kimi bin begins the built PATH (DL5)");
    }

    /// <summary>The code-round flow: the Kimi reviewer starts in the round's sanitised snapshot — without the branch's <c>.kimi-code</c> or <c>AGENTS.md</c> — and the snapshot is gone with the round.</summary>
    [Theory]
    [InlineData(KimiFlowHost.Windows)]
    [InlineData(KimiFlowHost.Wsl2)]
    public async Task ACodeRound_StartsTheKimiReviewerInTheSnapshot_AndTheSnapshotIsGoneWithTheRound(string mode)
    {
        using var flow = KimiFlowHost.Enter(mode, _person, _dir.At("flow"));
        var agent = Steer();
        var (repo, data) = await RepositoryAsync();
        var service = Service(data);
        await service.OpenAsync(repo, "feature");
        var planned = await service.ReviewPlanAsync(repo, "feature", Scope);
        await service.ResolveAsync(repo, "feature", "[]");

        var answered = await service.ReviewCodeAsync(repo, "feature", "main", Scope);
        var code = JsonDocument.Parse(answered).RootElement;

        code.TryGetProperty("reviewers", out var reviewers).Should().BeTrue($"the code round ran — plan: {planned}; code: {answered}");
        reviewers.GetString().Should().NotContain("FAILED").And.NotContain("not started");
        var started = await File.ReadAllTextAsync(agent.EnvFile + ".cwd", TestContext.Current.CancellationToken);
        Path.GetFileName(started).Should().StartWith(ReviewSnapshots.Prefix, "the code round's Kimi reviewer started in the round's sanitised snapshot");
        started.Should().NotStartWith(repo);
        Directory.Exists(started).Should().BeFalse("the snapshot went with the round");
        Directory.Exists(Path.GetDirectoryName(started)).Should().BeFalse("and so did the round's scratch, the owned Kimi home with it");
        _person.Hash().Should().Contain(entry => entry.StartsWith("credentials", StringComparison.Ordinal), "the person's sign-in folder is still whole");
    }

    /// <summary>S-K20 through the whole plan-round flow: a NATIVE Linux host is refused, with the WSL2-only sentence, before the fake Kimi is started — on every machine, the host being given.</summary>
    [Fact]
    public async Task APlanRoundOnANativeLinuxHost_IsRefused_NamingWsl2Only_AndNothingIsStarted()
    {
        using var flow = KimiFlowHost.Enter(KimiFlowHost.Linux, _person, _dir.At("flow"));
        var agent = Steer();
        var (repo, data) = await RepositoryAsync();
        var service = Service(data);
        await service.OpenAsync(repo, "feature");

        var planned = JsonDocument.Parse(await service.ReviewPlanAsync(repo, "feature", Scope)).RootElement;

        planned.GetProperty("reviewers").GetString().Should().Contain("Kimi runs on WSL2 only for now").And.Contain("not yet on a native Linux host", "a native Linux host should still be refused with the WSL2-only sentence, but Kimi started");
        File.Exists(agent.EnvFile).Should().BeFalse("the refusal comes before any process of Kimi's starts");
        agent.Entries().Should().BeEmpty();
    }

    /// <summary>A WSL1 kernel reads as a WSL host and is refused with the WSL1 sentence and its cure — the same shared rule as Vibe's.</summary>
    [Fact]
    public async Task APlanRoundOnAWsl1Kernel_IsRefused_NamingTheCure_AndNothingIsStarted()
    {
        using var flow = KimiFlowHost.Enter(KimiFlowHost.Wsl1, _person, _dir.At("flow"));
        var agent = Steer();
        var (repo, data) = await RepositoryAsync();
        var service = Service(data);
        await service.OpenAsync(repo, "feature");

        var planned = JsonDocument.Parse(await service.ReviewPlanAsync(repo, "feature", Scope)).RootElement;

        planned.GetProperty("reviewers").GetString().Should().Contain("this is WSL1").And.Contain("wsl --set-version <distro> 2");
        File.Exists(agent.EnvFile).Should().BeFalse();
    }
}
