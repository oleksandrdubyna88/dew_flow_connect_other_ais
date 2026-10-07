using System.Text;
using System.Text.Json.Nodes;
using CoaiMcp.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Parity (research/PLAN_api_streaming.md, S4): a streamed answer and an unstreamed one are judged by ONE reader. Every
/// recorded golden answer (<c>fixtures/api-goldens/*.response.json</c>) is read twice through each module that answers
/// in its shape — once whole, as it was recorded, and once RE-TOLD AS A STREAM — and both give the same exit, the same
/// usage line (the streamed one adding only <c>"streamed":true</c>) and the same answer file.
/// </summary>
/// <remarks>
/// The streams are SYNTHETIC, built here from the recorded answer: its reasoning as one delta, its content as two, its
/// <c>finish_reason</c> on a closing chunk, then the recorded <c>usage</c> object verbatim on a <c>choices: []</c> chunk
/// and <c>[DONE]</c> — the order the vendors' streams use (research/RESULTS_api_streaming_vendors.md). A recorded live
/// stream waits for the owner's key export; this holds the reader, not the vendor.
/// </remarks>
public sealed class AStreamedGoldenIsReadLikeItsRecordingTests : IDisposable
{
    private const string Key = "sk-live-0123456789abcdefghijklmnopqrstuv";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-stream-parity-").FullName;

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

    /// <summary>Each module at its measured model, with every recorded answer in the shape its route answers.</summary>
    [Theory]
    [InlineData("xai", "grok-4.7", "xai-turn.response.json", 0)]
    [InlineData("xai", "grok-4.7", "xai-cut.response.json", 70)]
    [InlineData("qwen", "qwen3.8-max", "dashscope-turn.response.json", 0)]
    [InlineData("qwen", "qwen3.8-max", "dashscope-cut.response.json", 70)]
    [InlineData("deepseek", "deepseek-v4-pro", "dashscope-turn.response.json", 0)]
    [InlineData("deepseek", "deepseek-v4-pro", "dashscope-cut.response.json", 70)]
    [InlineData("glm", "glm-5.3", "dashscope-turn.response.json", 0)]
    [InlineData("glm", "glm-5.3", "dashscope-reasoning-only.response.json", 70)]
    [InlineData("openai", "gpt-5", "openai-turn.response.json", 0)]
    public async Task ARecordedAnswerToldAsAStream_GivesTheSameExitUsageLineAndAnswer(string dialect, string model, string fixture, int exit)
    {
        var recorded = File.ReadAllText(ApiVendorGoldensTests.Fixture(fixture));

        var whole = await RunAsync(dialect, model, new ApiEndpointStub.Answer(200, recorded), stream: false);
        var streamed = await RunAsync(dialect, model, new ApiEndpointStub.Answer(200, string.Empty, ContentType: "text/event-stream",
            Chunks: [.. AsStream(recorded).Select(Encoding.UTF8.GetBytes)]), stream: true);

        whole.Code.Should().Be(exit, $"the recorded answer as it was read before streaming — {whole.Stderr}");
        streamed.Code.Should().Be(whole.Code, streamed.Stderr);
        streamed.Line.Should().Be(whole.Line[..^1] + ",\"streamed\":true}", "the same usage, said to have streamed");
        streamed.Answer.Should().Be(whole.Answer, "the same answer file, or none on both");
    }

    /// <summary>A recorded chat completion, re-told as the server-sent events a streaming vendor sends.</summary>
    private static IEnumerable<string> AsStream(string recorded)
    {
        var root = JsonNode.Parse(recorded)!;
        var choice = root["choices"]![0]!;
        var message = choice["message"]!;
        var content = (string?)message["content"] ?? string.Empty;
        var half = content.Length / 2;

        IEnumerable<JsonObject> deltas =
        [
            .. message["reasoning_content"] is { } reasoning ? [new JsonObject { ["reasoning_content"] = reasoning.DeepClone() }] : Array.Empty<JsonObject>(),
            .. content.Length > 0 ? [new JsonObject { ["content"] = content[..half] }, new JsonObject { ["content"] = content[half..] }] : Array.Empty<JsonObject>(),
        ];

        return
        [
            .. deltas.Select(delta => Event(Choice(delta, null))),
            Event(Choice(new JsonObject(), choice["finish_reason"]!.DeepClone())),
            Event(new JsonObject { ["choices"] = new JsonArray(), ["usage"] = root["usage"]!.DeepClone() }),
            "data: [DONE]\n\n",
        ];
    }

    private static JsonObject Choice(JsonObject delta, JsonNode? finish) => new()
    {
        ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }),
    };

    private static string Event(JsonNode data) => $"data: {data.ToJsonString()}\n\n";

    private sealed record Run(int Code, string Line, string Stderr, string Answer);

    private async Task<Run> RunAsync(string dialect, string model, ApiEndpointStub.Answer answer, bool stream)
    {
        _stub.Answers = _ => answer;
        var stdout = new StringWriter();
        var stderr = new List<string>();
        var outFile = Path.Combine(_dir, stream ? "streamed.json" : "whole.json");
        var prompt = Path.Combine(_dir, "prompt.txt");
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(prompt, "review this");
        File.WriteAllText(schema, FindingSchema.Json);
        string[] args =
        [
            "--ask-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", model, "--dialect", dialect,
            "--prompt-file", prompt, "--schema-file", schema, "--out", outFile, "--timeout-seconds", "20", "--max-tokens", "8192",
            .. stream ? ["--stream", "on"] : Array.Empty<string>(),
        ];

        var code = await AskApiMode.RunAsync(args, stderr.Add, stdout, name => name == ApiRuntime.KeyVariable ? Key : null);

        return new Run(code, stdout.ToString().Trim(), string.Join("\n", stderr), File.Exists(outFile) ? File.ReadAllText(outFile) : string.Empty);
    }
}
