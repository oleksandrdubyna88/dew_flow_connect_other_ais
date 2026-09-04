using System.Text.Json;
using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using Serilog.Core;

namespace CoaiMcp.Tests;

/// <summary>
/// A round whose record did not land is not an answer — it is a failure that must say so.
/// </summary>
/// <remarks>
/// <para>Reported after the previous fix: "the round returned findings, but its final record did not
/// land again: on disk the round is still <c>running</c> and <c>pending</c> is empty, so
/// <c>resolve</c> cannot see the findings."</para>
/// <para>That was my own doing. Making the final save best-effort stopped the round DYING and
/// started it lying instead: the caller is handed findings, indexed, and told to resolve them — and
/// the indices point into a list that was never written. Silence is the worse of the two failures.
/// A round that cannot be recorded has not finished; it has to be re-run, and the only way a caller
/// can know that is to be told.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AnUnrecordedRoundIsNotAnAnswerTests : IAsyncLifetime
{
    private const string TwoFindings = """
        {"findings": [
          {"severity": "major", "category": "security", "file": "app.cs", "line": 10,
           "title": "token compared with ==", "why": "timing side channel", "fix": "FixedTimeEquals"},
          {"severity": "major", "category": "reliability", "file": "app.cs", "line": 40,
           "title": "no timeout", "why": "a hung peer hangs the request", "fix": "add one"}
        ]}
        """;

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;

    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-unrecorded-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-unrecorded-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", TwoFindings);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", TwoFindings);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT"])
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

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private PanelService Service() =>
        new(
            new PanelSettings
            {
                Providers = [new("codex") { ExecutablePath = FakeCliExe }],
                Rounds = PanelConfig.Uniform(3, 5),
                DataDir = _data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            },
            VaultKeys.None("no vault in tests"),
            default,
            _launcher,
            Logger.None);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private string SessionFile() =>
        Directory.EnumerateFiles(Path.Combine(_data, "sessions"), "session-*.json").Single();

    [Fact]
    public async Task AfterANormalRound_TheDiskCarriesWhatResolveNeeds()
    {
        // The state `resolve` runs on: the round is no longer running, and the findings it will be
        // asked to decide on are ON DISK. A verdict returned over a session that says neither is an
        // answer nobody can act on.
        var service = Service();
        await service.OpenAsync(_repo, "feature");

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", "a plan worth reviewing"));

        answer.GetProperty("findings").GetArrayLength().Should().Be(2);
        var onDisk = Parse(await File.ReadAllTextAsync(SessionFile()));
        onDisk.GetProperty("rounds")[0].GetProperty("status").GetString().Should().Be("done");
        onDisk.GetProperty("pending").GetArrayLength().Should().Be(2, "resolve indexes into this list");
    }

    [Fact]
    public async Task ARoundThatCouldNotBeRecorded_SaysSoInsteadOfHandingBackFindings()
    {
        // Somebody holds the session file for the whole round — a scanner, a backup, another tool.
        // The findings exist and the verdict is decided, and NONE of it can be resolved, because
        // `resolve` reads the pending list from the file that was never written.
        var service = Service();
        await service.OpenAsync(_repo, "feature");
        // Held for WRITING while still letting others read: the round loads its session perfectly
        // well and cannot write the result. Sharing None would block the read too and produce a
        // different, honest error ("no session") — which is not the failure being pinned here.
        using var held = new FileStream(
            SessionFile(), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        var answer = Parse(await service.ReviewPlanAsync(_repo, "feature", "a plan worth reviewing"));

        answer.TryGetProperty("error", out var error).Should().BeTrue(
            "an answer whose findings cannot be resolved is a failure, not a verdict");
        error.GetString().Should().Contain("could not be recorded");
    }
}
