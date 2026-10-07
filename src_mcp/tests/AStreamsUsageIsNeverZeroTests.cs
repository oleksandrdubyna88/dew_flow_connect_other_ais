using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CoaiMcp.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Usage, never 0 (research/PLAN_api_streaming.md, test plan): a streamed call that began with a 200 may have been billed,
/// so however it ends — cut, failed inside the stream, over the answer cap, past the deadline — what
/// <see cref="ApiRuntime.ReadUsage"/> makes of the shim's output is the counted usage or UNKNOWN, never
/// <see cref="Usage.None"/> priced at $0. Judged at the parent's own reader, on a PRICED row: an unpriced one carries
/// <c>NoPriceSet</c> and could never equal <see cref="Usage.None"/>, which would prove nothing.
/// </summary>
public sealed class AStreamsUsageIsNeverZeroTests : IDisposable
{
    private const string Key = "sk-live-0123456789abcdefghijklmnopqrstuv";

    private static readonly TokenPrice Grok = new(new TokenRates(2.00, 0.50, 6.00), 0, TokenRates.None);

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-stream-usage-").FullName;

    public void Dispose()
    {
        _stub.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not the behaviour under test.
        }
    }

    private static string Chunk(string content, string finish = "null") =>
        "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":" + System.Text.Json.JsonSerializer.Serialize(content, ChunkJson.Default.String)
        + "},\"finish_reason\":" + finish + "}]}\n\n";

    private const string ErrorChunk =
        "data: {\"error\":{\"message\":\"model overloaded\"},\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"error\"}]}\n\n";

    private const string UsageChunk =
        "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":22,\"total_tokens\":33}}\n\n";

    private void Streams(bool drop, params string[] pieces) =>
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, string.Empty, ContentType: "text/event-stream",
            Chunks: [.. pieces.Select(Encoding.UTF8.GetBytes)], Drop: drop);

    [Fact]
    public async Task A_cut_stream_is_unknown()
    {
        Streams(true, Chunk("half an answ"));

        var (code, usage) = await AskAsync();

        code.Should().Be(ApiRuntime.EndedBeforeAnAnswerExit);
        usage.Should().Be(Unknown);
    }

    [Fact]
    public async Task An_error_inside_the_stream_is_the_usage_it_reported_or_else_unknown()
    {
        Streams(false, Chunk("part"), ErrorChunk, UsageChunk, "data: [DONE]\n\n");
        var (reported, counted) = await AskAsync();

        Streams(false, Chunk("part"), ErrorChunk, "data: [DONE]\n\n");
        var (silent, unknown) = await AskAsync();

        reported.Should().Be(70);
        counted.Should().Match<Usage>(u => u.TokensIn == 11 && u.TokensOut == 22 && !u.NotCaptured && u.CostUsd > 0,
            "the vendor billed the generation it then failed, and said how much");
        silent.Should().Be(70);
        unknown.Should().Be(Unknown, "a failure that reported no usage is unknown, not free");
    }

    [Fact]
    public async Task A_stream_over_the_answer_cap_is_unknown()
    {
        // Over AskApiMode's 8 MiB answer cap in lines each under its 1 MiB line ceiling, so the CAP is what stops it.
        var piece = Chunk(new string('x', 512 * 1024));
        Streams(false, [.. Enumerable.Repeat(piece, 17)]);

        var (code, usage) = await AskAsync();

        code.Should().Be(70);
        usage.Should().Be(Unknown);
    }

    [Fact]
    public async Task A_deadline_that_strikes_mid_stream_is_unknown()
    {
        // The stub cannot hold a stream open in the middle, so this body is served by a handler: a 200 event stream that
        // sends part of an answer and then never another byte.
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StreamContent(new Trickle(Encoding.UTF8.GetBytes(Chunk("thinking out loud")), 64, hangAtEnd: true))
            {
                Headers = { ContentType = new MediaTypeHeaderValue("text/event-stream") },
            },
        });

        var (code, usage) = await AskAsync(handler, timeoutSeconds: 1);

        code.Should().Be(ApiRuntime.EndedBeforeAnAnswerExit);
        usage.Should().Be(Unknown);
    }

    /// <summary>What the parent reads for a priced row that ended before an answer: unknown, priced at nothing.</summary>
    private static Usage Unknown => Usage.Unknown with { NoPriceSet = false };

    private async Task<(int Code, Usage Usage)> AskAsync(HttpMessageHandler? handler = null, int timeoutSeconds = 20)
    {
        var prompt = Path.Combine(_dir, "prompt.txt");
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(prompt, "review this");
        File.WriteAllText(schema, FindingSchema.Json);
        var stdout = new StringWriter();
        var stderr = new List<string>();
        string[] args =
        [
            "--ask-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", "grok-4", "--dialect", "openai",
            "--prompt-file", prompt, "--schema-file", schema, "--out", Path.Combine(_dir, "answer.json"),
            "--timeout-seconds", $"{timeoutSeconds}", "--max-tokens", "4096", "--stream", "on",
        ];

        var code = await AskApiMode.RunAsync(args, stderr.Add, stdout, name => name == ApiRuntime.KeyVariable ? Key : null, handler);
        var usage = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(Priced(), new ProcessResult(code, stdout.ToString(), string.Join("\n", stderr), false));
        usage.Should().NotBe(Usage.None, $"a stream that began with a 200 is never free — exit {code}, stdout '{stdout}'");

        return (code, usage);
    }

    private ReviewerInvocation Priced() =>
        new ApiRuntime("grok", _stub.Endpoint).Build(
            RoleCatalog.FeatureRole, "review this", _dir, Path.Combine(_dir, "schema.json"), _dir,
            new ReviewerSettings("grok") { Model = "grok-4.7", ApiKey = Key, Price = Grok, Timeout = TimeSpan.FromMinutes(2) });
}
