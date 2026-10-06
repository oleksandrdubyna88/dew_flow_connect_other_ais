using System.Text;
using CoaiMcp.Api;
using CoaiMcp.Core.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--ask-api --stream on</c> end to end against a local endpoint (todo/PLAN_api_streaming.md, S4 and S5): the request
/// asks for a stream and its usage; a streamed answer reaches the out file exactly as an unstreamed one does; and every
/// way a stream that began with a 200 can end prints its usage line FIRST — the last usage seen, or "not captured",
/// never a zero for a paid generation — then its exit.
/// </summary>
public sealed class AStreamedAskTests : IDisposable
{
    private const string Key = "sk-live-0123456789abcdefghijklmnopqrstuv";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-ask-stream-").FullName;
    private readonly List<string> _stderr = [];
    private readonly StringWriter _stdout = new();

    public void Dispose()
    {
        _stub.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Stderr => string.Join("\n", _stderr);

    private string OutFile => Path.Combine(_dir, "answer.json");

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);

        return path;
    }

    private Task<int> RunAsync(bool stream = true)
    {
        var args = new List<string>
        {
            "--ask-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", "grok-4", "--dialect", "openai",
            "--prompt-file", Write("prompt.txt", "review this"), "--schema-file", Write("schema.json", FindingSchema.Json),
            "--out", OutFile, "--timeout-seconds", "20", "--max-tokens", "4096",
        };
        if (stream)
        {
            args.AddRange(["--stream", "on"]);
        }

        return AskApiMode.RunAsync([.. args], _stderr.Add, _stdout, name => name == ApiRuntime.KeyVariable ? Key : null);
    }

    private static string Chunk(string content, string finish = "null") =>
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + content.Replace("\"", "\\\"") + "\"},\"finish_reason\":" + finish + "}]}\n\n";

    private const string Usage =
        "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":22,\"total_tokens\":33}}\n\n";

    private void Streams(bool drop, params string[] pieces) =>
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, string.Empty, ContentType: "text/event-stream",
            Chunks: [.. pieces.Select(Encoding.UTF8.GetBytes)], Drop: drop);

    [Fact]
    public async Task The_request_asks_for_a_stream_and_its_usage_and_the_streamed_answer_reaches_the_out_file()
    {
        Streams(false, Chunk("{\"findings\":"), Chunk("[]}", "\"stop\""), Usage, "data: [DONE]\n\n");

        var code = await RunAsync();

        code.Should().Be(0, Stderr);
        File.ReadAllText(OutFile).Should().Be("{\"findings\":[]}");
        _stub.Requests.Single().Body.Should().Contain("\"stream\":true,\"stream_options\":{\"include_usage\":true}");
        _stdout.ToString().Should().Contain("\"tokensIn\":11,\"tokensOut\":22");
    }

    [Fact]
    public async Task Without_the_switch_the_request_is_the_one_it_always_was()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}"));

        (await RunAsync(stream: false)).Should().Be(0, Stderr);
        _stub.Requests.Single().Body.Should().Contain("\"stream\":false").And.NotContain("stream_options");
    }

    [Fact]
    public async Task An_error_inside_the_200_stream_fails_quotes_the_vendor_and_still_reports_the_usage_first()
    {
        Streams(false, Chunk("part"),
            "data: {\"error\":{\"message\":\"Rate limit exceeded: 429\"},\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"error\"}]}\n\n",
            Usage, "data: [DONE]\n\n");

        var code = await RunAsync();

        code.Should().Be(70);
        Stderr.Should().Contain("Rate limit exceeded: 429").And.Contain("inside its HTTP 200");
        _stdout.ToString().Should().Contain("\"tokensIn\":11", "the vendor billed the generation it then failed");
        File.Exists(OutFile).Should().BeFalse();
    }

    [Fact]
    public async Task A_stream_cut_before_its_answer_finished_ends_before_an_answer_and_its_usage_is_unknown_never_zero()
    {
        Streams(true, Chunk("half an answ"));

        var code = await RunAsync();

        code.Should().Be(ApiRuntime.EndedBeforeAnAnswerExit, Stderr);
        Stderr.Should().Contain("ended before the answer finished").And.Contain("12 characters of answer");
        _stdout.ToString().Should().Contain("\"notCaptured\":true");
        File.Exists(OutFile).Should().BeFalse();
    }

    [Fact]
    public async Task A_gateway_that_ignored_the_stream_and_answered_one_JSON_is_read_whole_as_always()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}"));

        (await RunAsync()).Should().Be(0, Stderr);
        File.ReadAllText(OutFile).Should().Be("{\"findings\":[]}");
        // ...and its usage line does NOT say it streamed: the row's Check reports a stream it asked for and did not get
        // (the plan gate's finding 1, Story C).
        _stdout.ToString().Should().NotContain("streamed");
    }

    [Fact]
    public async Task The_usage_line_says_streamed_only_when_a_stream_was_read()
    {
        Streams(false, Chunk("{\"findings\":[]}", "\"stop\""), Usage, "data: [DONE]\n\n");

        (await RunAsync()).Should().Be(0, Stderr);
        _stdout.ToString().Should().Contain("\"streamed\":true");
    }

    [Theory]
    [InlineData(false, "{\"tokensIn\":1,\"tokensOut\":2,\"streamed\":true}", "")]
    [InlineData(true, "{\"tokensIn\":1,\"tokensOut\":2,\"streamed\":true}", "streamed")]
    [InlineData(true, "{\"tokensIn\":1,\"tokensOut\":2}", "not-streamed")]
    [InlineData(true, "", "not-streamed")]
    public void A_check_of_a_row_that_asked_to_stream_says_whether_it_did(bool asked, string stdout, string verdict)
    {
        // An older coai-mcp that ignored --stream on, and a gateway that answered one JSON, both leave no "streamed":
        // the Check cannot confirm the setting took effect, and says so (the plan gate's findings 1 and 2).
        CoaiMcp.Server.ConsultantCheck.StreamVerdict(asked, ["thread noise", stdout]).Should().Be(verdict);
    }

    [Fact]
    public void Every_module_keeps_the_stream_through_its_own_spelling_the_thinking_off_branches_included()
    {
        foreach (var module in ApiVendors.All)
        {
            foreach (var thinking in new[] { true, false })
            {
                var body = module.RequestBody(new ApiTurn("m", "p", FindingSchema.Json, 1, ThinkingOn: thinking, Stream: true));
                body.Should().Contain("\"stream\":true", $"{module.Name} with thinking {(thinking ? "on" : "off")}");
            }
        }
    }
}
