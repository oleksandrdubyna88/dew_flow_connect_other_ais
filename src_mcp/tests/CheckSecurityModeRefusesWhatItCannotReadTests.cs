using System.Text.Json;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-security</c> never answers a request it did not read as if it had read it — epic 2's code round
/// (coai session 589a145b): an unknown argument, a lane that is not an object, a sample the detector could not finish,
/// and a request too large to hold.
/// </summary>
/// <remarks>
/// Each of these used to answer 0 with a result a person reads as "no signals" or "your lane is valid": the argument
/// was ignored, the lane was replaced by the shipped one, the incomplete detection was not said, and an over-large
/// stdin was buffered whole before it was refused.
/// </remarks>
public sealed class CheckSecurityModeRefusesWhatItCannotReadTests
{
    [Theory]
    [InlineData("--bogus")]
    [InlineData("--validate", "--validate")]
    [InlineData("--validate", "extra")]
    public void AnUnknownOrRepeatedArgument_IsRefusedByName(params string[] extra)
    {
        string[] args = ["--check-security", .. extra];

        CheckSecurityMode.ArgumentRefusal(args).Should().Contain("--check-security").And.Contain(extra[^1]);
    }

    [Theory]
    [InlineData]
    [InlineData("--validate")]
    public void TheModeAlone_OrWithValidate_IsAccepted(params string[] extra) =>
        CheckSecurityMode.ArgumentRefusal(["--check-security", .. extra]).Should().BeEmpty();

    [Theory]
    [InlineData("[]")]
    [InlineData("\"words\"")]
    [InlineData("null")]
    public void ALaneThatIsNotAnObject_IsRefused_NeverReplacedByTheShippedOne(string lane)
    {
        var (code, output, err) = CheckSecurityMode.Answer($$"""{"text":"x","lane":{{lane}}}""", validate: true);

        code.Should().Be(65);
        output.Should().BeEmpty();
        err.Should().Contain("lane");
    }

    [Fact]
    public void ASampleTheDetectorCouldNotFinish_SaysSo()
    {
        var oversized = new string('x', SecuritySignals.MaxFileCharacters + 10);
        var (code, output, _) = CheckSecurityMode.Answer(JsonSerializer.Serialize(new Dictionary<string, string> { ["text"] = oversized }), validate: false);

        code.Should().Be(0);
        JsonDocument.Parse(output).RootElement.GetProperty("detectionIncomplete").GetBoolean().Should().BeTrue(
            "an empty signal list from a detector that did not finish is not 'no signals'");
    }

    [Fact]
    public void ASampleItRead_SaysItFinished()
    {
        var (_, output, _) = CheckSecurityMode.Answer("""{"text":"var hash = MD5.Create();"}""", validate: false);

        JsonDocument.Parse(output).RootElement.GetProperty("detectionIncomplete").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task AnOverLargeStdin_IsReadOnlyToItsLimit_AndRefused()
    {
        using var reader = new StringReader(new string('x', CheckSecurityMode.MaxRequestCharacters + 4096));

        var read = await CheckSecurityMode.ReadBoundedAsync(reader);

        read.Length.Should().Be(CheckSecurityMode.MaxRequestCharacters, "nothing past the limit is held in memory");
        CheckSecurityMode.Answer(read, validate: false).Code.Should().Be(65);
    }
}
