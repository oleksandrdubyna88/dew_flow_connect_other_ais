using Xunit;
using CoaiMcp.Core.Findings;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// Every wrapping shape Gemini actually returns, as a literal fixture — this is the layer most
/// likely to break, so it is the one with the most fixtures.
/// </summary>
public sealed class GeminiPayloadTests
{
    private const string Payload = """{"findings": []}""";

    [Fact]
    public void Envelope_YieldsTheResponseField()
    {
        var enveloped = """{"response": "{\"findings\": []}", "stats": {"tokens": 1234}}""";

        GeminiPayload.Extract(enveloped).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(Payload);
    }

    [Fact]
    public void CleanJson_PassesThroughUntouched() =>
        GeminiPayload.Extract(Payload).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(Payload);

    [Fact]
    public void FencedJson_IsUnwrapped()
    {
        var fenced = "```json\n" + Payload + "\n```";

        GeminiPayload.Extract(fenced).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(Payload);
    }

    [Fact]
    public void PreambleThenFence_IsUnwrapped()
    {
        var chatty = "Here is the review you asked for:\n```json\n" + Payload + "\n```\nLet me know!";

        GeminiPayload.Extract(chatty).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(Payload);
    }

    [Fact]
    public void TrailingProseWithABrace_DoesNotExtendTheExtraction()
    {
        // First-{ to last-} would swallow "escaped }" into the payload and hand JsonSerializer
        // garbage — a parse error that reads like a model failure. Brace counting does not.
        var text = Payload + "\nNote: braces like } are common in prose.";

        GeminiPayload.Extract(text).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(Payload);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void EmptyAnswer_IsItsOwnOutcome_NeverACleanReview(string raw) =>
        GeminiPayload.Extract(raw).Should().BeOfType<ExtractOutcome.Empty>();

    [Fact]
    public void ProseWithNoObject_IsNoJson_WithAReason() =>
        GeminiPayload.Extract("I could not produce a review this time.")
            .Should().BeOfType<ExtractOutcome.NoJson>().Which.Reason.Should().NotBeEmpty();

    [Fact]
    public void TwoJsonObjectsInOneAnswer_TakesTheFirstBalancedOne()
    {
        var two = """{"findings": []} and also {"findings": [{"title": "x"}]}""";

        GeminiPayload.Extract(two).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(Payload);
    }

    [Fact]
    public void BraceInsideAStringLiteral_DoesNotCloseTheObject()
    {
        var tricky = """{"findings": [{"title": "closing brace } in text", "why": "\" escaped quote"}]}""";

        GeminiPayload.Extract(tricky).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(tricky);
    }

    [Fact]
    public void UnbalancedObject_IsNoJson() =>
        GeminiPayload.Extract("""{"findings": [""").Should().BeOfType<ExtractOutcome.NoJson>();

    [Fact]
    public void EnvelopeWithNonStringResponse_FallsThroughToTheRawText()
    {
        // An envelope whose response is an object is not the documented shape — treat the whole
        // thing as raw text and let the balanced scan find the object inside it.
        var odd = """{"response": {"findings": []}}""";

        GeminiPayload.Extract(odd).Should().BeOfType<ExtractOutcome.Payload>()
            .Which.Json.Should().Be(odd);
    }
}
