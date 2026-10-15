using CoaiMcp.Runners.Acp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The line <c>/usage</c> answers with after a turn (S3.13 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, RESULTS §5): the session's input
/// and output tokens, or — for anything else Kimi says — unknown, never a zero standing for a number.
/// </summary>
public sealed class AcpUsageLineTests
{
    [Theory]
    [InlineData("Context: 20004 / 262144 tokens (8%) Session total: 19,973 input, 31 output", 19973, 31)]
    [InlineData("Context: 20004 / 262144 tokens (8%)\nSession total: 19973 input, 31 output\n", 19973, 31)]
    [InlineData("Session total: 7 input, 0 output", 7, 0)]
    [InlineData("Session total:   1,234,567   input,   89,012   output", 1234567, 89012)]
    public void ASessionTotalLine_IsRead_WithOrWithoutThousandsCommas(string text, long input, long output)
    {
        AcpUsageLine.Parse(text).Should().Be(new AcpTokens(input, output, true));
    }

    [Theory]
    [InlineData("Context: 0 / 262144 tokens (0%) Session total: no LLM calls yet")]
    [InlineData("Session total: 7 input")]
    [InlineData("Session total: input, output")]
    [InlineData("19973 input, 31 output")]
    [InlineData("")]
    public void AnythingElse_IsNotCaptured_NeverZeroAsANumber(string text)
    {
        AcpUsageLine.Parse(text).Should().Be(AcpTokens.Unknown);
    }

    [Fact]
    public void ANumberTooBigForALong_IsNotCaptured_NotAThrow()
    {
        AcpUsageLine.Parse("Session total: 99999999999999999999999 input, 1 output").Should().Be(AcpTokens.Unknown);
    }
}
