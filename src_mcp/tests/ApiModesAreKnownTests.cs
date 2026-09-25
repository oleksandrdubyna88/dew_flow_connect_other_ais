using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--ask-api</c> and <c>--probe-api</c> are one-shot modes this binary KNOWS — selected by
/// <c>args[0]</c> before any transport opens, and never answered 64.
/// </summary>
/// <remarks>
/// <c>.agents/PROJECT.md</c> fixes the shape: a mode the binary does not have exits 64 so a caller can
/// fall back; a mode it does have never does, whatever is wrong with the request. The list in that
/// document is held to <c>Program.Classify</c> by <see cref="ARequestFaultIsNotAnOldBinaryTests"/>;
/// this file holds the two new modes to the switch itself.
/// </remarks>
public sealed class ApiModesAreKnownTests
{
    [Fact]
    public void AskApi_IsAMode_NotAnUnknownArgument()
    {
        Program.Classify(["--ask-api"]).Should().NotBe(Program.Startup.Usage,
            "an unknown args[0] exits 64, which the executor would read as a vendor that could not start");
    }

    [Fact]
    public void ProbeApi_IsAMode_NotAnUnknownArgument()
    {
        Program.Classify(["--probe-api"]).Should().NotBe(Program.Startup.Usage);
    }

    [Fact]
    public void TheTwoAreDifferentModes()
    {
        Program.Classify(["--ask-api"]).Should().NotBe(Program.Classify(["--probe-api"]),
            "a probe that answered as a review would write an answer file nobody asked for");
    }
}
