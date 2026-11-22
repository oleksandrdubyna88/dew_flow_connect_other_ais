using CoaiMcp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// v2 removed three one-shot modes (<c>--cadence</c>, <c>--security-prompt-text</c>, <c>--check-security</c>). A mode this binary
/// no longer knows must answer what every unknown mode answers: <b>64</b>, "never heard of it" — the code an extension that still
/// launches it reads as "this server does not have that", never as a request fault (PROJECT.md, one-shot modes).
/// </summary>
[Collection("console-out")]
public sealed class TheRemovedOneShotModesAreUnknownTests
{
    public static TheoryData<string> Removed => new()
    {
        "--cadence",
    };

    [Theory]
    [MemberData(nameof(Removed))]
    public void ARemovedMode_IsClassifiedAsUsage(string mode) =>
        Program.Classify([mode]).Should().Be(Program.Startup.Usage);

    [Theory]
    [MemberData(nameof(Removed))]
    public async Task ARemovedMode_ExitsSixtyFour_AndWritesNothingToStdout(string mode)
    {
        var (said, code) = await Stdout.OfAsync(() => Program.RunAsync([mode, "--repo", TempDir.Root]));

        code.Should().Be(64, $"`{mode}` was removed in v2: unknown mode, exit 64 — never 65, which would say the binary KNOWS it");
        said.Should().BeEmpty("an unknown mode speaks on stderr only");
    }
}
