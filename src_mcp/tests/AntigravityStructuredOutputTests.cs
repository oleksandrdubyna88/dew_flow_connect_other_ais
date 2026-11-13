using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// With <c>--json-schema</c>, agy's answer is the CLI's own validated <c>structured_output</c> object — NOT the
/// <c>response</c> text, which is every agent turn's text run together.
/// </summary>
/// <remarks>
/// <para>Measured 2026-11-13 on agy 1.5.8: a launch with <c>--json-schema</c> runs a second "reminder" turn
/// (<c>num_turns: 2</c>), and <c>response</c> concatenates each turn's text — two copies of the schema's JSON
/// locally, four on the Team server. Read as one document that is a JSON object followed by more JSON, and the
/// parser either refuses it or reads the wrong copy. <c>structured_output</c> is the single object the CLI
/// validated against the schema. It is absent on older agy and on launches without a schema (the consultant),
/// where <c>response</c> is still the answer.</para>
/// <para>Pure string inputs: nothing here touches the disk or starts a process.</para>
/// </remarks>
public sealed class AntigravityStructuredOutputTests
{
    private const string StepUpdate =
        """{"event":"step_update","step":{"index":0,"type":"PLANNER_RESPONSE","status":"DONE"}}""";

    private const string FindingA =
        """{"severity":"major","category":"security","file":"a.cs","line":1,"title":"finding A","why":"because A","fix":"fix A"}""";

    private const string FindingB =
        """{"severity":"major","category":"reliability","file":"b.cs","line":2,"title":"finding B","why":"because B","fix":"fix B"}""";

    private static readonly ReviewerInvocation Invocation =
        new("antigravity", "Architecture", new ProcessRequest("agy", [], "."));

    /// <summary>The stream as agy writes it: a step line, then the result line; <paramref name="extraMembers"/> are raw members added to the result payload.</summary>
    private static string Stream(string response, string? extraMembers = null) =>
        StepUpdate + "\n"
        + "{\"event\":\"result\",\"result\":{\"conversation_id\":\"b4d2417f-fa5a-48fd-9a8b-c768ba4d8436\","
        + "\"status\":\"SUCCESS\",\"response\":" + JsonSerializer.Serialize(response)
        + ",\"duration_seconds\":4.97,\"num_turns\":2"
        + (extraMembers is null ? string.Empty : "," + extraMembers)
        + ",\"json_schema\":{\"type\":\"object\"},"
        + "\"usage\":{\"input_tokens\":12057,\"output_tokens\":110,\"thinking_tokens\":85,\"cache_read_tokens\":0,\"total_tokens\":12167}}}\n";

    private static string? Read(string stdout) =>
        new AntigravityRuntime().ReadAnswer(Invocation, new ProcessResult(0, stdout, string.Empty, false));

    [Fact]
    public void ADuplicatedResponse_IsReadAsTheStructuredOutputOnce()
    {
        const string Object = """{"findings":[]}""";

        var answer = Read(Stream(Object + "\n" + Object + "\n", "\"structured_output\":" + Object));

        answer.Should().NotBeNull();
        using var document = JsonDocument.Parse(answer!);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        answer.Should().Be(Object);
        answer.Should().NotContain("\n", "a second copy of the object is exactly what the concatenated response carried");
    }

    [Fact]
    public void WithoutStructuredOutput_TheResponseIsTheAnswer_Unchanged()
    {
        var response = "{\"findings\":[]}\n";

        Read(Stream(response)).Should().Be(response);
    }

    [Fact]
    public void ANullStructuredOutput_FallsBackToTheResponse()
    {
        var response = "{\"findings\":[]}";

        Read(Stream(response, "\"structured_output\":null")).Should().Be(response);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"x\"")]
    [InlineData("7")]
    [InlineData("true")]
    public void ANonObjectStructuredOutput_FallsBackToTheResponse(string structuredOutput)
    {
        var response = "{\"findings\":[],\"notes\":\"the response\"}";

        Read(Stream(response, "\"structured_output\":" + structuredOutput)).Should().Be(response);
    }

    [Fact]
    public void WithNoResultEvent_ThereIsNoAnswer()
    {
        Read(StepUpdate + "\n").Should().BeNull();
    }

    [Fact]
    public void TheParsedReview_IsTheValidatedObject_NotTheFirstCopyOfTheConcatenatedResponse()
    {
        var first = "{\"findings\":[" + FindingA + "]}";
        var second = "{\"findings\":[" + FindingB + "]}";

        var raw = Read(Stream(first + "\n" + second + "\n", "\"structured_output\":" + second));
        var review = ReviewerExecutor.ParseAnswer(raw, "antigravity");

        review.Should().NotBeNull();
        review!.Findings.Select(f => f.Title).Should().Equal("finding B");
    }
}
