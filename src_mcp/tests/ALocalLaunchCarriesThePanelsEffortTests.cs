using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A ✓ Check of a <c>local</c> row is launched with the panel's local effort and ceiling — exactly what a local REVIEWER
/// is launched with (research/PLAN_local_effort_reaches_every_local_launch.md, S1.1).
/// </summary>
/// <remarks>
/// Found 2026-10-13 on the owner's machine: two checks of a Gemma4 row each generated exactly 8 192 tokens of thinking
/// and no content, because the consultant launch sent no <c>reasoning_effort</c> while every reviewer sends the panel's
/// <c>none</c>. The panel here carries NON-default values (<c>medium</c>, 4 096) so a launch that ignored the panel and
/// hard-coded the defaults could not pass (plan round, finding 0).
/// </remarks>
public sealed class ALocalLaunchCarriesThePanelsEffortTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-localeffort-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-localeffort-temp-").FullName;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public void Dispose()
    {
        foreach (var dir in (string[])[_data, _temp])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    private PanelSettings Panel() => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(60),
        LocalReasoningEffort = "medium",
        LocalMaxTokens = 4096,
    };

    private static string LocalRow(string extra = "") =>
        "{\"row\":{\"id\":\"gemma\",\"runtime\":\"local\",\"model\":\"gemma4\",\"baseUrl\":\"http://127.0.0.1:9/v1\",\"executablePath\":"
        + JsonSerializer.Serialize(FakeCliExe) + extra + "}}";

    private async Task<IReadOnlyList<string>> CheckedArgv(string row)
    {
        var spy = new AskLocalSpy(new ProcessLauncher());
        await ConsultantCheckMode.AnswerModelAsync(
            Panel(), row, spy, _temp, _ => { }, Noticing.None, TestContext.Current.CancellationToken, VaultKeys.None("t"));

        spy.Launches.Should().ContainSingle("a check is one paid turn of the row");

        return spy.Launches.Single();
    }

    [Fact]
    public async Task ACheckOfALocalRow_IsLaunchedWithThePanelsLocalEffortAndCeiling()
    {
        var argv = await CheckedArgv(LocalRow());

        argv.Should().ContainInConsecutiveOrder(["--reasoning-effort", "medium"],
            "a thinking model told nothing thinks to the ceiling — the check must send what a local reviewer sends");
        argv.Should().ContainInConsecutiveOrder(["--max-tokens", "4096"], "the panel's local ceiling, not the record's default");
    }

    [Fact]
    public async Task ACheckOfALocalRowWithItsOwnEffort_KeepsTheRowsEffort()
    {
        var argv = await CheckedArgv(LocalRow(",\"effort\":\"low\""));

        argv.Should().ContainInConsecutiveOrder(["--reasoning-effort", "low"], "a row's own effort wins over the panel's, as for a reviewer");
    }

    /// <summary>The real launcher for everything but the local shim, whose argv it keeps and whose answer is a refusal.</summary>
    private sealed class AskLocalSpy(IProcessLauncher inner) : IProcessLauncher
    {
        private readonly ConcurrentQueue<IReadOnlyList<string>> _launches = new();

        public IReadOnlyList<IReadOnlyList<string>> Launches => [.. _launches];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (!request.Arguments.Contains("--ask-local"))
            {
                return inner.RunAsync(request, ct);
            }

            _launches.Enqueue([.. request.Arguments]);

            return Task.FromResult(new ProcessResult(70, string.Empty, "the spy answers nothing", TimedOut: false));
        }
    }
}
