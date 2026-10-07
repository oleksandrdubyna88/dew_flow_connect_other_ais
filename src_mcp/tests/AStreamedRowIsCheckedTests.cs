using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The person's flow of todo/PLAN_api_streaming.md, Story D, below the page: an api row with its stream switch on is
/// handed to <c>--check-model</c> exactly as the card's ✓ Check hands it, the real <c>coai-mcp --ask-api</c> child is
/// launched against a local endpoint, and the check's record says whether the answer came as a stream.
/// </summary>
/// <remarks>
/// The page half — the switch drawn on the new model card, saved to the row, sent only to a binary that lists
/// <c>apiStream</c> — is <c>anApiRowCanStream.test.ts</c>; the badge that reads this record is tested there too. What this
/// adds is the real child between them: the row's <c>stream</c> crossing stdin, the launch's <c>--stream on</c>, the
/// shim's usage line, and the verdict read back out of it.
/// </remarks>
public sealed class AStreamedRowIsCheckedTests : IDisposable
{
    private const string Key = "sk-check-0123456789abcdefghijklmnopqrstuv";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _data = Directory.CreateTempSubdirectory("coai-streamcheck-data-").FullName;
    private readonly string _temp = Directory.CreateTempSubdirectory("coai-streamcheck-temp-").FullName;

    public void Dispose()
    {
        _stub.Dispose();
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

    private PanelSettings Settings() => new()
    {
        Providers = [],
        DataDir = _data,
        ReviewerTimeout = TimeSpan.FromSeconds(90),
        ConsultEnabled = true,
    };

    private string Row(bool stream) =>
        "{\"row\":{\"id\":\"grok\",\"runtime\":\"api\",\"model\":\"grok-4\",\"dialect\":\"openai\",\"baseUrl\":" + JsonSerializer.Serialize(_stub.Endpoint)
        + ",\"executablePath\":" + JsonSerializer.Serialize(ServerBinary.Path) + (stream ? ",\"stream\":true" : string.Empty) + "}}";

    private async Task<JsonElement> CheckAsync(bool stream)
    {
        var (code, stdout, stderr) = await ConsultantCheckMode.AnswerModelAsync(
            Settings(), Row(stream), new ProcessLauncher(), _temp, _ => { }, Noticing.None, TestContext.Current.CancellationToken,
            new VaultKeys(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["grok"] = Key }, string.Empty));

        code.Should().Be(0, stderr);

        return JsonDocument.Parse(stdout).RootElement.Clone();
    }

    private const string Answer = "marker: none\\ncanary: CANNOT";

    private void AnswersAsAStream() =>
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, string.Empty, ContentType: "text/event-stream", Chunks:
        [
            .. new[]
            {
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"" + Answer + "\"},\"finish_reason\":\"stop\"}]}\n\n",
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":22,\"total_tokens\":33}}\n\n",
                "data: [DONE]\n\n",
            }.Select(Encoding.UTF8.GetBytes),
        ]);

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? string.Empty;

    [Fact]
    public async Task A_row_switched_to_stream_is_checked_through_the_real_child_and_its_record_says_streamed()
    {
        AnswersAsAStream();

        var said = await CheckAsync(stream: true);

        Text(said, "state").Should().Be("answered", said.ToString());
        Text(said, "streamed").Should().Be("streamed", said.ToString());
        _stub.Requests.Should().ContainSingle().Which.Body.Should().Contain("\"stream\":true", "the row's switch reached the vendor's request");
    }

    [Fact]
    public async Task A_row_switched_to_stream_that_was_answered_with_one_JSON_is_recorded_as_not_streamed()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion(Answer));

        var said = await CheckAsync(stream: true);

        Text(said, "state").Should().Be("answered", said.ToString());
        Text(said, "streamed").Should().Be("not-streamed", "a gateway that ignored the request answered all the same");
    }

    [Fact]
    public async Task A_row_that_did_not_ask_records_nothing_about_streams()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion(Answer));

        var said = await CheckAsync(stream: false);

        Text(said, "state").Should().Be("answered", said.ToString());
        (said.TryGetProperty("streamed", out var streamed) ? streamed.GetString() : string.Empty).Should().BeEmpty();
        _stub.Requests.Should().ContainSingle().Which.Body.Should().Contain("\"stream\":false");
    }
}
