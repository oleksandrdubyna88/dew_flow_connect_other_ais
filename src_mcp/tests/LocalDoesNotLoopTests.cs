using System.Text.Json;
using Xunit;
using FluentAssertions;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Tests;

/// <summary>
/// A local reviewer must not be able to spend its whole budget repeating one sentence.
/// </summary>
/// <remarks>
/// <para>Observed on 2026-09-04, and it cost 6.7 minutes of the one GPU while every other window
/// queued behind it. The model opened with a perfectly good finding and then collapsed into:</para>
/// <code>
/// … The client retries again. The client retries again. The client retries again. …
/// </code>
/// <para>for forty kilobytes, until `max_tokens` stopped it mid-string. The JSON never closed, so the
/// round reported "the answer was not the schema's JSON" — true, and nothing like the real story.</para>
/// <para><b>Greedy decoding is the cause.</b> `temperature: 0` was chosen so a reviewer is
/// reproducible, and reproducibility is worth keeping; a frequency penalty is deterministic too, so
/// it costs nothing of it. A schema does not save this: a repeated sentence inside a string value is
/// schema-valid right up to the token that runs out.</para>
/// </remarks>
public sealed class LocalDoesNotLoopTests
{
    private static JsonElement Body(int maxTokens = 8192) =>
        JsonDocument.Parse(LocalAsk.RequestBody(
            "qwen", "review this", """{"type":"object"}""", 42, "none", maxTokens)).RootElement;

    [Fact]
    public void TheRequestCarriesAFrequencyPenalty()
    {
        var body = Body();

        body.TryGetProperty("frequency_penalty", out var penalty).Should().BeTrue(
            "greedy decoding with no penalty is how a model spends a whole budget on one sentence");
        penalty.GetDouble().Should().BeGreaterThan(0);
    }

    [Fact]
    public void ItIsSmall_BecauseAPenaltyIsNotAnOpinionAboutContent()
    {
        // Enough to break a literal loop, not enough to push the model off its own vocabulary — a
        // review is full of legitimately repeated words: the file names it is talking about.
        Body().GetProperty("frequency_penalty").GetDouble().Should().BeLessThanOrEqualTo(0.5);
    }

    [Fact]
    public void ReproducibilityIsUntouched()
    {
        // The reason temperature is zero in the first place. A frequency penalty is deterministic,
        // so the same prompt is still the same request.
        var body = Body();

        body.GetProperty("temperature").GetDouble().Should().Be(0);
        body.GetProperty("seed").GetInt32().Should().Be(42);
        LocalAsk.RequestBody("qwen", "p", """{"type":"object"}""", 42, "none", 8192)
            .Should().Be(LocalAsk.RequestBody("qwen", "p", """{"type":"object"}""", 42, "none", 8192));
    }

    [Fact]
    public void TheCeilingIsStillThere() =>
        Body(4096).GetProperty("max_tokens").GetInt32().Should().Be(4096);
}
