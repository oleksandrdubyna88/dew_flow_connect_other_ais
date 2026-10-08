using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An antigravity question row asks coai to look — a <c>coai-lookup</c> block — and coai serves it read-only and continues
/// the SAME conversation with the result (research/PLAN_agy_searches_through_coai.md, S2).
/// </summary>
/// <remarks>
/// The fake CLI runs as a real child on the question row's minimal environment, steered by the temp-directory file; it
/// tells the turns apart by <see cref="AntigravityFollowUps.LookupTurnMarker"/> on stdin, which every continuation carries.
/// </remarks>
[Collection("fakecli-env")]
public sealed class QuestionRowLookupScenarioTests : IAsyncLifetime
{
    private const string Conversation = "6a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d";

    private readonly ProcessLauncher _launcher = new();
    private string _repo = string.Empty;
    private string _data = string.Empty;
    private string _record = string.Empty;

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private static string SteeringFile => Path.Combine(Path.GetTempPath(), "fakecli-minimal.json");

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-qlookup-repo-").FullName;
        _data = Directory.CreateTempSubdirectory("coai-qlookup-data-").FullName;
        _record = Directory.CreateTempSubdirectory("coai-qlookup-argv-").FullName;
        await Git("init", "-b", "main");
        await Git("config", "user.email", "t@example.com");
        await Git("config", "user.name", "t");
        await File.WriteAllTextAsync(Path.Combine(_repo, "Mailer.cs"), "void Send() { }\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            File.Delete(SteeringFile);
        }
        catch (IOException) { }

        foreach (var dir in (string[])[_repo, _data, _record])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest("git", args, _repo), TestContext.Current.CancellationToken);
        result.ExitCode.Should().Be(0, string.Join(' ', args) + ": " + result.StdErr);
    }

    /// <summary>One agy stream: the conversation named, and a result with the answer and the (cumulative) usage given.</summary>
    private static string Stream(string response, long tokensIn, long tokensOut) =>
        JsonSerializer.Serialize(new Dictionary<string, object> { ["event"] = "init", ["conversation_id"] = Conversation }) + "\n"
        + JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["event"] = "result",
            ["result"] = new Dictionary<string, object>
            {
                ["conversation_id"] = Conversation,
                ["status"] = "SUCCESS",
                ["response"] = response,
                ["usage"] = new Dictionary<string, long> { ["input_tokens"] = tokensIn, ["output_tokens"] = tokensOut },
            },
        }) + "\n";

    private static string Block(params string[] lines) => "```" + LookupRequests.Fence + "\n" + string.Join("\n", lines) + "\n```";

    /// <summary>Turn 1 prints <paramref name="first"/>; turn n (a continuation) prints <paramref name="later"/>[n-2], else the last.</summary>
    private void Steer(string first, params string[] later)
    {
        var pairs = new Dictionary<string, string>
        {
            ["FAKECLI_TURN_MARKER"] = AntigravityFollowUps.LookupTurnMarker.Replace("{0}", "{n}", StringComparison.Ordinal),
            ["FAKECLI_STDOUT"] = later.Length > 0 ? later[^1] : first,
            ["FAKECLI_TURN1_STDOUT"] = first,
            ["FAKECLI_STDERR"] = string.Empty,
            ["FAKECLI_RECORD_DIR"] = _record,
        };
        for (var turn = 0; turn < later.Length; turn++)
        {
            pairs[$"FAKECLI_TURN{turn + 2}_STDOUT"] = later[turn];
        }

        File.WriteAllText(SteeringFile, JsonSerializer.Serialize(pairs));
    }

    private PanelService Service() => new(
        new PanelSettings
        {
            Providers = [],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ReviewerTimeout = TimeSpan.FromSeconds(30),
            QuestionConsult = new QuestionConsultSettings
            {
                Rows = [new QuestionRow("agy-disk", "antigravity", "antigravity", "gemini-3.8-flash-low", string.Empty, FakeCliExe, string.Empty, "question-disk", Enabled: true)],
                Roots = [_repo],
                RowBudget = TimeSpan.FromSeconds(60),
            },
        },
        VaultKeys.None("no vault in tests"),
        default,
        _launcher,
        Logger.None,
        Noticing.None);

    private async Task<JsonElement> AskAsync() =>
        JsonDocument.Parse(await Service().AskConsultantsAsync(
            _repo, "Which helper sends the mail?", "I have not searched yet.", ct: TestContext.Current.CancellationToken)).RootElement;

    /// <summary>Every launch's argv in the order they were made, its stdin as the last field (the recorder's format).</summary>
    private IReadOnlyList<string[]> Launches() =>
        [.. Directory.EnumerateFiles(_record, "*.argv").OrderBy(File.GetCreationTimeUtc).ThenBy(p => p, StringComparer.Ordinal)
            .Select(path => LaunchRecords.Read(path).Split('\0'))];

    /// <summary>What a launch told the model — its stdin line DECODED, as the CLI reads it.</summary>
    private static string Told(string[] argv) =>
        JsonDocument.Parse(argv[^1]).RootElement.GetProperty("message").GetProperty("content").GetString()!;

    private string[] TurnOf(int n) => Launches().Single(argv =>
        n == 1 ? !Told(argv).Contains(AntigravityFollowUps.LookupHeadingStart, StringComparison.Ordinal)
               : Told(argv).Contains(string.Format(System.Globalization.CultureInfo.InvariantCulture, AntigravityFollowUps.LookupTurnMarker, n), StringComparison.Ordinal));

    private IReadOnlyList<JsonElement> LedgerRows() =>
        [.. File.ReadAllLines(Path.Combine(_data, "usage.jsonl")).Select(line => JsonDocument.Parse(line).RootElement)];

    private static JsonElement Row(JsonElement reply) => reply.GetProperty("answers").EnumerateArray().Should().ContainSingle().Subject;

    [Fact]
    public async Task ALookup_IsServedByCoai_InTheSameConversation_AndTheRowAnswers()
    {
        Steer(Stream("Let me look first.\n" + Block("search \"Send()\""), 1000, 10), Stream("The mail is sent by Send() in Mailer.cs.", 2500, 40));

        var row = Row(await AskAsync());

        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered, row.ToString());
        row.GetProperty("advice").GetString().Should().Contain("Send() in Mailer.cs");
        Launches().Should().HaveCount(2);
        Told(TurnOf(1)).Should().Contain(LookupRequests.Fence, "the prompt teaches the block").And.Contain(_repo, "and names the root to look in");
        var second = TurnOf(2);
        second.Should().ContainInOrder("--conversation", Conversation).And.Contain("--mode");
        Told(second).Should().Contain("Mailer.cs:1:", "coai served the search").And.NotContain("Let me look first", "only the results travel, not the whole prompt again");
        LedgerRows().Should().HaveCount(2, "one ledger line per turn");
        LedgerRows().Sum(r => r.GetProperty("tokensIn").GetInt64()).Should().Be(2500, "agy's reports are cumulative: the turns' shares add up to the last report, never the sum of reports");
    }

    [Fact]
    public async Task ALookupOutsideTheRoot_IsRefusedInTheNextTurn_AndNothingOutsideIsRead()
    {
        Steer(Stream("Looking around.\n" + Block("list ..", "list " + Path.GetTempPath()), 100, 5), Stream("Nothing outside the folder; Mailer.cs sends it.", 200, 9));

        var row = Row(await AskAsync());

        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered);
        var told = Told(TurnOf(2));
        told.Should().Contain("not served").And.Contain("outside the granted roots");
        told.Should().NotContain(Path.GetFileName(_data), "a sibling folder outside the root is never listed");
    }

    [Fact]
    public async Task TheLastTurn_StillAsking_EndsOnItsProse_AndSaysTheLookupsWereCapped()
    {
        Steer(Stream("Draft: probably Mailer.cs.\n" + Block("list ."), 100, 5));

        var row = Row(await AskAsync());

        Launches().Should().HaveCount(1 + LookupBudget.FollowUps, "never more turns than the cap");
        Told(TurnOf(1 + LookupBudget.FollowUps)).Should().Contain(AntigravityFollowUps.LastTurn, "the last turn is told it is the last");
        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Answered);
        row.GetProperty("advice").GetString().Should().Contain("Draft: probably Mailer.cs.").And.NotContain(LookupRequests.Fence);
        row.GetProperty("note").GetString().Should().Contain("capped").And.Contain("list .");
    }

    [Fact]
    public async Task TheLastTurn_WithNothingButABlock_FailsSayingTheLookupsWereCapped()
    {
        Steer(Stream(Block("list ."), 100, 5));

        var row = Row(await AskAsync());

        row.GetProperty("status").GetString().Should().Be(RowOutcomes.Failed);
        (row.GetProperty("reason").GetString() + " " + row.GetProperty("note").GetString()).Should().Contain("capped");
    }
}
