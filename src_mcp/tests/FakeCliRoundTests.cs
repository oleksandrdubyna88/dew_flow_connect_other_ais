using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A real <c>review_plan</c> round against FakeCli, in a throwaway repository — what the AI that called
/// the gate actually reads back.
/// </summary>
/// <remarks>
/// <para>Extracted from <see cref="SplitOrderTests"/> when issue #467 needed the same round for the
/// command texts: two copies of the setup would drift, and the one that drifts is the one that stops
/// isolating its environment. A derived class carries <c>[Collection("fakecli-env")]</c> itself, because
/// FakeCli is steered by process-wide variables.</para>
/// </remarks>
public abstract class FakeCliRoundTests : IAsyncLifetime
{
    protected const string Clean = """{"findings": []}""";

    protected readonly ProcessLauncher _launcher = new();
    protected readonly string _caller = "test-caller-" + Guid.NewGuid().ToString("N")[..8];
    protected string _repo = string.Empty;
    protected string _data = string.Empty;

    protected static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-round-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-round-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await Git("checkout", "-b", "epic-1");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        // The caller Claude Code would have exported for us. A fresh one per test class, so a
        // developer's own session id can never make these pass or fail.
        Environment.SetEnvironmentVariable("COAI_CALLER_SESSION", _caller);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "COAI_CALLER_SESSION"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        foreach (var dir in (string[])[_repo, _data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    protected static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    protected async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>One FakeCli reviewer, this test's data directory, and budgets small enough for a test.</summary>
    protected PanelSettings Defaults() => new()
    {
        Providers = [new("codex") { ExecutablePath = FakeCliExe }],
        Rounds = PanelConfig.Uniform(3, 2),
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(30),
        RateLimitBackoff = TimeSpan.FromMilliseconds(5),
    };

    protected PanelService ServiceFor(PanelSettings settings) =>
        new(settings, VaultKeys.None("no vault in tests"), default, _launcher, Logger.None, Noticing.None);

    protected static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    protected static string[] CommandsOf(JsonElement answer) => ListOf(answer, "commands");

    protected static string[] ListOf(JsonElement answer, string field) =>
        answer.TryGetProperty(field, out var list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Select(c => c.GetString() ?? string.Empty)]
            : [];

    protected async Task<JsonElement> PlanRound(PanelService service, string branch, string plan)
    {
        await service.OpenAsync(_repo, branch);
        Script(Clean);
        return Parse(await service.ReviewPlanAsync(_repo, branch, plan));
    }
}
