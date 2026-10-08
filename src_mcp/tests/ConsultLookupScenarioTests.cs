using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The stuck consultant on antigravity asks coai to look — a <c>coai-lookup</c> block — and coai serves it read-only over
/// the checkout and continues the SAME conversation, inside ONE <c>consult</c> call (todo/PLAN_agy_searches_through_coai.md, S3).
/// </summary>
[Collection("fakecli-env")]
public sealed class ConsultLookupScenarioTests : ConsultScenarioBase
{
    private const string Conversation = "7b3c4d5e-6f70-4a8b-9c0d-1e2f3a4b5c6d";

    private static readonly IReadOnlyDictionary<string, ConsultantChoice> OnAntigravity = new Dictionary<string, ConsultantChoice>
    {
        [CallerIdentity.Claude] = new("antigravity", "gemini-3.1-pro-high", Runtime: "antigravity", ExecutablePath: FakeCliExe),
    };

    private static readonly string[] Steered =
    [
        "FAKECLI_TURN_MARKER", "FAKECLI_STDOUT", "FAKECLI_TURN1_STDOUT", "FAKECLI_TURN2_STDOUT", "FAKECLI_STDERR",
        "FAKECLI_RECORD_DIR", "FAKECLI_SIDE_EFFECT", "FAKECLI_OUTFILE_TEXT",
    ];

    private string _record = string.Empty;

    private PanelService Agy() => Service(providers: [], consultants: OnAntigravity);

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

    private Steering Steer(string first, string second, string sideEffect = "")
    {
        _record = Directory.CreateTempSubdirectory("coai-clookup-argv-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_TURN_MARKER", AntigravityFollowUps.LookupTurnMarker.Replace("{0}", "{n}", StringComparison.Ordinal));
        Environment.SetEnvironmentVariable("FAKECLI_TURN1_STDOUT", first);
        Environment.SetEnvironmentVariable("FAKECLI_TURN2_STDOUT", second);
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", second);
        Environment.SetEnvironmentVariable("FAKECLI_STDERR", "agy: ok");
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", null);
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", _record);
        Environment.SetEnvironmentVariable("FAKECLI_SIDE_EFFECT", sideEffect.Length > 0 ? sideEffect : null);

        return new Steering(_record);
    }

    private sealed record Steering(string Recorded) : IDisposable
    {
        public IReadOnlyList<string[]> Launches() =>
            [.. Directory.EnumerateFiles(Recorded, "*.argv").Select(path => LaunchRecords.Read(path).Split('\0'))];

        public void Dispose()
        {
            foreach (var name in Steered)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            try
            {
                Directory.Delete(Recorded, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Told(string[] argv) =>
        JsonDocument.Parse(argv[^1]).RootElement.GetProperty("message").GetProperty("content").GetString()!;

    private async Task<IReadOnlyList<JsonElement>> LedgerRows() =>
        [.. (await File.ReadAllLinesAsync(Path.Combine(_data, "usage.jsonl"), TestContext.Current.CancellationToken))
            .Select(line => JsonDocument.Parse(line).RootElement)];

    [Fact]
    public async Task ALookup_InsideOneConsult_ContinuesTheSameConversation_AndTheRecordKeepsOneTurnBilledOnce()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Stream("Checking first.\n" + Block("search \"Count()\""), 1000, 10), Stream("Count() still returns 3; the uncommitted comment says it was 4.", 2500, 40));

        var reply = await Consult(Agy(), "why does the count look wrong?");

        reply.TryGetProperty("error", out var error).Should().BeFalse(error.ToString());
        Advise(reply).Should().Contain("Count() still returns 3");
        var launches = steering.Launches();
        launches.Should().HaveCount(2);
        var first = launches.Single(argv => !Told(argv).Contains(AntigravityFollowUps.LookupHeadingStart, StringComparison.Ordinal));
        Told(first).Should().Contain(LookupRequests.Fence, "the consultant's prompt teaches the block").And.Contain(_repo);
        var second = launches.Single(argv => Told(argv).Contains(AntigravityFollowUps.LookupHeadingStart, StringComparison.Ordinal));
        second.Should().ContainInConsecutiveOrder("--conversation", Conversation);
        Told(second).Should().Contain("Parser.cs:1:").And.Contain("was 4", "coai reads the WORKING tree, uncommitted edits included");
        var record = new ConsultationStore(_data).Read(reply.GetProperty("consultationId").GetString()!)!;
        record.Turns.Should().ContainSingle("the lookups happen inside one consult call").Which.TokensIn.Should().Be(2500, "agy's reports are cumulative: the larger, never the sum");
        (await LedgerRows()).Should().ContainSingle().Which.GetProperty("tokensIn").GetInt64().Should().Be(2500);
    }

    [Fact]
    public async Task AConsultantThatKeepsAsking_IsCappedAtThreeLookups_AndTheAdviceIsItsProseWithWhyItStopped()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var asking = Stream("Still looking.\n" + Block("list ."), 100, 5);
        using var steering = Steer(asking, asking);

        var reply = await Consult(Agy(), "why does the count look wrong?");

        steering.Launches().Should().HaveCount(4, "one launch and three lookup continuations, then the cap");
        Advise(reply).Should().Contain("Still looking.").And.Contain("lookups capped at 3 turns").And.Contain("list .")
            .And.NotContain(LookupRequests.Fence, "a capped block's prose is the advice, never the block");
    }

    [Fact]
    public async Task AWatchedTreeThatChangedBeforeTheContinuation_StopsIt_AndTheTurnSaysWhy()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        using var steering = Steer(Stream("Checking.\n" + Block("list ."), 100, 5), Stream("never reached", 200, 9), sideEffect: Path.Combine(_repo, "written-by-the-consultant.txt"));

        var reply = await Consult(Agy(), "why does the count look wrong?");

        steering.Launches().Should().ContainSingle("a consultant after which the checkout changed is not continued");
        Refusal(reply).Should().Contain("written-by-the-consultant.txt");
    }
}
