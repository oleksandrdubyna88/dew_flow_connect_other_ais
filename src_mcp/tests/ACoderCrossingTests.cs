using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s7, the third security judgement, as pure values: which variables of a coder launch cross into the unit, in which order, and by which road. Every explicit variable with a value is a line of the unit's stdin (the pool key first); only the
/// proxy, certificate and time-zone names of coai's own environment cross by name; a name or a value the road cannot carry is refused, and the message never holds the value. The units themselves are read in <see cref="ACoderSecretsOnStdinTests"/>.
/// </summary>
public sealed class ACoderCrossingTests
{
    private static ProcessRequest Request(Dictionary<string, string?> environment, params string[] passthrough) =>
        new("claude", [], Path.GetFullPath("tree")) { Environment = environment, InheritsEnvironment = false, Passthrough = new HashSet<string>(passthrough) };

    private static string? NoEnvironment(string name) => null;

    private static string? EveryNameIsSet(string name) => "set";

    [Fact]
    public void EveryExplicitVariable_IsOnStdin_ThePoolKeyFirst_TheRestByName_NullsAreNotCrossed()
    {
        var request = Request(new Dictionary<string, string?>
        {
            ["ZED_TOKEN"] = "z",
            ["ANTHROPIC_API_KEY"] = "row",
            [CoderUnit.ClaudeKeyVariable] = "pool",
            ["ANTHROPIC_BASE_URL"] = null,
        });

        var crossing = CoderUnit.Cross(request, NoEnvironment);

        crossing.OnStdin.Select(pair => pair.Key).Should().Equal([CoderUnit.ClaudeKeyVariable, "ANTHROPIC_API_KEY", "ZED_TOKEN"], "the pool key is always the first line, the rest in name order, and a null (a removed name) is not crossed");
        crossing.StdinHead.Should().Be("pool\nrow\nz\n");
        crossing.ByName.Should().BeEmpty("no explicit variable is a --setenv");
    }

    [Fact]
    public void OnlyThePassableNamesCoaiHasCrossByName_AndAnExplicitOneOfTheSameNameIsOnStdinAlone()
    {
        var request = Request(new Dictionary<string, string?> { ["HTTPS_PROXY"] = "http://explicit.example:3128", ["ANTHROPIC_API_KEY"] = "row" }, "HTTPS_PROXY", "NO_PROXY", "TZ", "NOT_A_PASSABLE_NAME");

        var crossing = CoderUnit.Cross(request, EveryNameIsSet);

        crossing.ByName.Should().BeEquivalentTo(["NO_PROXY", "TZ"], "a passable name coai has crosses by name; one that the launch gave itself is not crossed twice; a name outside the list never crosses");
        crossing.OnStdin.Select(pair => pair.Key).Should().BeEquivalentTo(["ANTHROPIC_API_KEY", "HTTPS_PROXY"]);
        CoderUnit.Cross(request, NoEnvironment).ByName.Should().BeEmpty("the companion: a passable name that coai's own environment lacks crosses nothing");
    }

    [Theory]
    [InlineData("two words")]
    [InlineData("1LEADING_DIGIT")]
    [InlineData("A=B")]
    [InlineData("BAD-NAME")]
    public void AnExplicitNameThatIsNotAPlainIdentifier_IsRefused_BeforeItIsSpelledIntoTheUnitsScript(string name)
    {
        var request = Request(new Dictionary<string, string?> { [name] = "the-value-of-a-secret" });

        var cross = () => CoderUnit.Cross(request, NoEnvironment);

        cross.Should().Throw<ArgumentException>().Which.Message.Should().NotContain("the-value-of-a-secret");
    }

    [Theory]
    [InlineData("first\nsecond")]
    [InlineData("first\rsecond")]
    [InlineData("first\0second")]
    public void AValueThatIsNotOneLine_IsRefused_AndTheMessageNeverHoldsIt(string value)
    {
        var request = Request(new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = "a-secret-" + KeyWorld.Sentinel + value });

        var cross = () => CoderUnit.Cross(request, NoEnvironment);

        var message = cross.Should().Throw<ArgumentException>().Which.Message;
        message.Should().Contain("ANTHROPIC_API_KEY").And.NotContain(KeyWorld.Sentinel);
    }

    [Fact]
    public async Task TheUnitsShell_ReadsOneLinePerStdinName_InOrder_AndHoldsNoValue()
    {
        await using var bed = await BoundaryBed.CreateAsync();
        var request = Request(new Dictionary<string, string?> { [CoderUnit.ClaudeKeyVariable] = "pool-" + KeyWorld.Sentinel, ["ANTHROPIC_API_KEY"] = "row-" + KeyWorld.Sentinel }, "TZ");
        var crossing = CoderUnit.Cross(request, EveryNameIsSet);

        var inner = CoderUnit.Inner(bed.Host, bed.Job, CoderProgram.Resolve("/bin/true", bed.Host.Path, bed.Job), ["--print"], crossing);

        var script = inner[2];
        script.Should().Contain($"IFS= read -r {CoderUnit.ClaudeKeyVariable} || exit 70; export {CoderUnit.ClaudeKeyVariable}; IFS= read -r ANTHROPIC_API_KEY || exit 70; export ANTHROPIC_API_KEY; ", "one line per name, in the order the stdin head is written, each a hard stop when the line is missing");
        script.IndexOf("read -r", StringComparison.Ordinal).Should().BeGreaterThan(script.IndexOf("unset \"$v\"", StringComparison.Ordinal), "the unset loop runs first, so a name that was read is not unset after it");
        script.Should().Contain("\" TZ \"", "the by-name names are the only ones the unset loop spares");
        inner.Should().NotContain(argument => argument.Contains(KeyWorld.Sentinel, StringComparison.Ordinal), "no value is in the unit's argv");
    }
}
