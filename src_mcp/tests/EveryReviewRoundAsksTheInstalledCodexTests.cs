using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every review round asks the installed codex its release before it builds the roster, and an Off codex row on a release
/// that refuses the standard tier is sent no tier at all (research/PLAN_codex_tier_floor.md, build step 4) — the plan round,
/// the code round with its security lane, and the document round, each through the real service.
/// </summary>
/// <remarks>
/// <para>Against the fake CLI answering <c>codex-cli 0.120.0</c> (in the refusing range: no <c>service_tier</c> on any
/// launch) and <c>codex-cli 0.160.0</c> (outside it: <c>service_tier=default</c> exactly as before). What is asserted is
/// the argv each launch actually STARTED with, recorded by the child — the pure half of the rule is
/// <see cref="ACodexThatRefusesTheStandardTierTests"/>; this is the proof that each site fills it.</para>
/// <para>The feature round's twin is <c>AFeatureIsReviewedEndToEndTests.AFeatureRound_TellsItsCodexOnlyWhatItsReleaseTakes</c>,
/// beside its own fixture.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class EveryReviewRoundAsksTheInstalledCodexTests : IAsyncLifetime
{
    private const string Scope = """
        # SCOPE

        An Off codex row on a release that refuses the standard tier is told no tier: every review launch on codex
        0.110.0 through 0.130.0 failed at config load before this, and a release outside that range is told exactly what
        it was told before. Done when every launch's argv says so.
        """;

    private const string Purpose = """
        # What this document is for

        It is the requirements for a payments integration, written for the two engineers who will build it. After
        reading it they must be able to start without asking anybody a question.
        """;

    private static readonly string[] Steering =
        ["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_RECORD_DIR", "FAKECLI_VERSION_STDOUT"];

    private readonly ProcessLauncher _launcher = new();
    private readonly string _repo = Directory.CreateTempSubdirectory("coai-tierprobe-repo-").FullName;
    private readonly string _data = Directory.CreateTempSubdirectory("coai-tierprobe-data-").FullName;
    private readonly string _record = Directory.CreateTempSubdirectory("coai-tierprobe-record-").FullName;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Query.cs"), "class Query { int F() => 1; }\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Query.cs"), "class Query { int F(string v) => database.Query(v); }\n");
        await Git("add", ".");
        await Git("commit", "-m", "change");
        Directory.CreateDirectory(Path.Combine(_data, "prompts"));
        await File.WriteAllTextAsync(Path.Combine(_data, "prompts", "redteam-general.md"), "Fixture: return an empty findings array.");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        // One answer both schemas read: the ordinary reviewers' findings, and the security lane's status beside them.
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", """{"status":"SECURE","findings":[]}""");
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in Steering)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service()
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":true,"prompts":[{"id":"redteam-general"}],"runs":[{"vendor":"codex","prompt":"redteam-general"}]}
            """, [new("codex")]);

        return new PanelService(
            new PanelSettings
            {
                // Fast left at its default — Off, the state that fails on 0.110–0.130.
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                SecurityLane = lane,
                Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human) with { SecurityLane = lane.Gate },
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None);
    }

    /// <summary>Every codex review launch recorded since the last clear — each one's argv, the stdin field dropped.</summary>
    private List<string[]> Launches() =>
        [.. Directory.GetFiles(_record, "*.argv").Select(file => File.ReadAllText(file).Split('\0')[..^1]).Where(argv => argv.Contains("exec"))];

    private void Forget()
    {
        foreach (var file in Directory.GetFiles(_record))
        {
            File.Delete(file);
        }
    }

    private static IEnumerable<string> Tiers(string[] argv) =>
        argv.Zip(argv.Skip(1)).Where(pair => pair.First == "-c" && pair.Second.StartsWith("service_tier=", StringComparison.Ordinal)).Select(pair => pair.Second);

    private static void EveryLaunch(List<string[]> launches, string[] sent, string release) =>
        launches.Should().NotBeEmpty().And.OnlyContain(argv => Tiers(argv).SequenceEqual(sent),
            $"codex {release} with fast Off is told {(sent.Length == 0 ? "no tier" : sent[0])} on every launch");

    private static void Answered(string reply)
    {
        var root = JsonDocument.Parse(reply).RootElement;
        root.TryGetProperty("error", out var error).Should().BeFalse($"unexpected refusal: {error}");
    }

    [Theory]
    [InlineData("0.120.0", new string[0])]
    [InlineData("0.160.0", new[] { "service_tier=default" })]
    public async Task APlanRound_TellsItsCodexOnlyWhatItsReleaseTakes(string release, string[] sent)
    {
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", $"codex-cli {release}\n");
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        Answered(await service.ReviewPlanAsync(_repo, "feature", "the plan"));

        EveryLaunch(Launches(), sent, release);
    }

    [Theory]
    [InlineData("0.120.0", new string[0])]
    [InlineData("0.160.0", new[] { "service_tier=default" })]
    public async Task ACodeRound_AndItsSecurityLane_TellTheirCodexOnlyWhatItsReleaseTakes(string release, string[] sent)
    {
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", $"codex-cli {release}\n");
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        await service.ReviewPlanAsync(_repo, "feature", "the plan");
        await service.ResolveAsync(_repo, "feature", "[]");
        Forget();

        Answered(await service.ReviewCodeAsync(_repo, "feature", "main", Scope));

        var launches = Launches();
        launches.Should().Contain(argv => argv.Any(a => a.Contains("finding-schema-security", StringComparison.Ordinal)),
            "the security lane's codex launched in this round too");
        EveryLaunch(launches, sent, release);
    }

    [Theory]
    [InlineData("0.120.0", new string[0])]
    [InlineData("0.160.0", new[] { "service_tier=default" })]
    public async Task ADocumentRound_TellsItsCodexOnlyWhatItsReleaseTakes(string release, string[] sent)
    {
        Environment.SetEnvironmentVariable("FAKECLI_VERSION_STDOUT", $"codex-cli {release}\n");
        var service = Service();
        await service.OpenAsync(_repo, "main");

        Answered(await service.ReviewDocumentAsync(_repo, "main", Purpose, documentText: "# Spec\n\nIt must be fast.\n", documentName: "spec"));

        EveryLaunch(Launches(), sent, release);
    }
}
