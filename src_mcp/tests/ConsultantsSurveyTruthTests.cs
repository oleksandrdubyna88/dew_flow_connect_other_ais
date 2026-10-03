using System.Text.Json;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What <c>--consultants</c> says about a row is what the row's consultant actually does and actually did: the auth
/// its TURN uses, a failure only when it is that consultant's, and a health file that cannot be read said as such.
/// </summary>
/// <remarks>
/// The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), findings A3, A4 and B. The
/// survey runs in-process over a launcher that answers every CLI with a version and runs nothing, so no real CLI on
/// this machine is asked anything.
/// </remarks>
public sealed class ConsultantsSurveyTruthTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-survey-truth-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Every CLI answers its version; nothing is ever started.</summary>
    private sealed class Versions : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ProcessResult(0, "fakecli 9.9.9", string.Empty, TimedOut: false));
    }

    private PanelSettings Settings() => new()
    {
        Providers = [],
        DataDir = _data,
        ConsultEnabled = true,
        Consultants = new Dictionary<string, ConsultantChoice>
        {
            [CallerIdentity.Claude] = new("codex", Runtime: "codex", ExecutablePath: "codex-on-this-machine"),
        },
    };

    private async Task<JsonElement> RowAsync(string kind = CallerIdentity.Claude)
    {
        var parts = new ConsultantParts(Settings(), new Versions(), _ => { }, Noticing.None);
        var answer = await new ConsultantsSurvey(parts).AnswerAsync(HostKind.Windows, DateTime.UtcNow, TestContext.Current.CancellationToken);
        var json = JsonSerializer.Serialize(answer, ConsultantsJsonContext.Default.ConsultantsAnswer);

        return JsonDocument.Parse(json).RootElement.GetProperty("consultants").EnumerateArray()
            .Single(row => row.GetProperty("callerKind").GetString() == kind).Clone();
    }

    private string Health(string name) =>
        Path.Combine(Directory.CreateDirectory(Path.Combine(_data, "consultations", "health")).FullName, name);

    // ---------- B: the auth source is the TURN's ----------

    [Fact]
    public async Task TheConsultantsAuthSource_IsWhatItsTurnUses_NeverAVaultKey()
    {
        // ConsultantTurnInputs.Settings sets no ApiKey: a consultation runs on the CLI's own sign-in whatever the vault
        // holds (only RosterBuilder hands a reviewer its key). The survey used to read the vault and probe as if the key
        // would be sent, and "vault key" on this row sent a person to look after a key the turn never reads (RED, before
        // the fix, with a vault key for codex present). The survey no longer takes the vault at all.
        var cli = (await RowAsync()).GetProperty("cli");

        cli.GetProperty("probed").GetBoolean().Should().BeTrue();
        cli.GetProperty("authSource").GetString().Should().NotBe("vault key",
            "the consultation turn carries no vault key, so its auth is the CLI's own");
    }

    // ---------- A4: an unreadable health file is said, never healthy ----------

    [Theory]
    [InlineData("claude.failure.json")]
    [InlineData("claude.answer.json")]
    public async Task AHealthFileThatCannotBeRead_IsSaidOnTheRow_NeverReadAsNothingHappened(string name)
    {
        File.WriteAllText(Health(name), "{ this is not json");

        var row = await RowAsync();

        row.TryGetProperty("healthUnreadable", out var said).Should().BeTrue(
            "a torn health file must reach the tab as unreadable — absent would read as a consultant that never failed: " + row);
        said.GetString().Should().Contain(name);
    }

    [Fact]
    public async Task ReadableHealthFiles_SayNothingIsUnreadable()
    {
        File.WriteAllText(Health("claude.answer.json"), """{"utc":"2026-10-03T10:00:00.0000000Z","callerKind":"claude","vendor":"codex"}""");

        var row = await RowAsync();

        row.GetProperty("healthUnreadable").GetString().Should().BeEmpty();
        row.GetProperty("lastAnswer").GetProperty("vendor").GetString().Should().Be("codex");
    }

    // ---------- A1: a survey that began before a failure does not erase it ----------

    [Fact]
    public void ASurveyWhoseProbesRanWhileAFailureWasRecorded_WritesThatFailure_NotItsStaleRow()
    {
        // The survey's rows were built before its probes finished; a turn failed in between and wrote its health file.
        // The write re-reads the health files immediately before the move, so the other side sees the failure.
        var stale = new ConsultantsAnswer
        {
            Utc = "2026-10-03T08:00:00.0000000Z",
            Side = "wsl",
            Consultants = [new ConsultantRowReport { CallerKind = CallerIdentity.Claude, Vendor = "codex", Runtime = "codex", FailureCurrent = false }],
        };
        File.WriteAllText(Health("claude.failure.json"),
            """{"utc":"2026-10-03T08:00:30.0000000Z","callerKind":"claude","vendor":"codex","kind":"quota","cure":"wait"}""");

        var printed = ConsultantsFile.Written(_data, stale, _ => { });

        var row = JsonDocument.Parse(printed).RootElement.GetProperty("consultants")[0];
        row.GetProperty("failureCurrent").GetBoolean().Should().BeTrue("the failure recorded while the probes ran must survive the survey's write");
        row.GetProperty("lastFailure").GetProperty("kind").GetString().Should().Be("quota");
        File.ReadAllText(Path.Combine(_data, "consultations", "health", "consultants.json")).Should().Be(printed, "what is printed is what is written");
    }
}
