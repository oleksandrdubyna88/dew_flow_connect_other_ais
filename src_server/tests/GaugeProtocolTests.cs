using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>One odd line on the reader's stdout is skipped; it never throws out of the parse.</summary>
public sealed class GaugeProtocolTests
{
    private static readonly DateTimeOffset Observed = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("""{"id":2.5,"result":{}}""")]
    [InlineData("""{"id":99999999999,"result":{}}""")]
    public void ALineWhoseIdIsNotAnInt32_IsSkippedAndTheAnswerStillRead(string odd)
    {
        var stdout = odd + "\n" + GaugeFixtures.Answer + "\n";

        GaugeProtocol.Answered(stdout).Should().BeTrue();
        GaugeProtocol.Parse(stdout, Observed).Should().BeOfType<GaugeAnswer.Read>();
    }
}
