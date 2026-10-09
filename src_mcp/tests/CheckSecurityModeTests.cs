using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--check-security</c>: what the security lane would make of a piece of text — the signals it raises, the cards
/// that would be due, the patterns it refuses (research/PLAN_one_model_catalog.md, epic 2, story 4: what "Try it" calls).
/// </summary>
/// <remarks>
/// <para>Everything arrives on stdin, never argv: a sample can hold a token, and a command line is in process listings
/// and limited to 32 K on Windows. The lane being edited travels with it, so the answer is about the words on the
/// screen, not the ones saved. <c>--validate</c> only compiles the lane's patterns — the save-time check.</para>
/// <para>A request this binary cannot read is 65, never 64: 64 means "never heard of this mode", which is how the
/// extension recognises an older binary and falls back.</para>
/// </remarks>
public sealed class CheckSecurityModeTests
{
    private static (int Code, JsonElement Answer, string Err) Ask(string stdin, bool validate = false)
    {
        var (code, output, err) = CheckSecurityMode.Answer(stdin, validate);

        return (code, output.Length > 0 ? JsonDocument.Parse(output).RootElement.Clone() : default, err);
    }

    private static IReadOnlyList<string> Strings(JsonElement answer, string name) =>
        [.. answer.GetProperty(name).EnumerateArray().Select(one => one.GetString()!)];

    [Fact]
    public void TheShippedWords_AnswerForTextAlone()
    {
        var (code, answer, _) = Ask("""{"text":"var db = new DbContext(); var hash = MD5.Create();"}""");

        code.Should().Be(0);
        Strings(answer, "signals").Should().Contain("sql").And.Contain("crypto");
    }

    [Fact]
    public void TheLaneBeingEdited_DecidesTheAnswer_ItsCardsIncluded()
    {
        var (code, answer, _) = Ask("""
            {"text":"Acme.Charge(order);",
             "lane":{"enabled":true,"prompts":[{"id":"redteam-billing","words":["acme.charge("]}],"runs":[]}}
            """);

        code.Should().Be(0);
        // The card whose words matched — and not a conditional preset whose trigger did not; an "always" card is due on
        // any code, so it is truthfully among the due cards too.
        Strings(answer, "cards").Should().Contain("redteam-billing").And.NotContain("redteam-sql");
        Strings(answer, "signals").Should().NotContain(signal => signal.StartsWith("own:"), "the card answers as itself, not as an internal name");
    }

    [Fact]
    public void ARefusedPattern_IsNamedInTheAnswer()
    {
        var (_, answer, _) = Ask("""{"text":"x","lane":{"enabled":true,"signals":{"secrets":["/(?=x)/"]}}}""");

        var refused = answer.GetProperty("refused").EnumerateArray().Single();
        refused.GetProperty("pattern").GetString().Should().Be("/(?=x)/");
        refused.GetProperty("signal").GetString().Should().Be("secrets");
        refused.GetProperty("why").GetString().Should().Contain("lookaround");
    }

    [Fact]
    public void Validate_NeedsNoText_AndSaysOnlyWhatIsRefused()
    {
        var (code, answer, _) = Ask("""{"lane":{"enabled":true,"signals":{"secrets":["/(a)\\1/","password"]}}}""", validate: true);

        code.Should().Be(0);
        answer.GetProperty("refused").GetArrayLength().Should().Be(1);
        answer.TryGetProperty("signals", out _).Should().BeFalse("a validation tries no text");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"lane":{}}""")]
    [InlineData("""{"text":7}""")]
    public void ARequestItCannotRead_Is65_NeverTheOldBinarys64(string stdin)
    {
        var (code, _, err) = Ask(stdin);

        code.Should().Be(65);
        err.Should().NotBeEmpty("the sentence says what was wrong with the request");
    }

    [Fact]
    public void ARequestPastTheLimit_Is65()
    {
        var (code, _, err) = Ask($$"""{"text":"{{new string('a', CheckSecurityMode.MaxRequestCharacters)}}"}""");

        code.Should().Be(65);
        err.Should().Contain($"{CheckSecurityMode.MaxRequestCharacters}");
    }

    [Fact]
    public void TheModeIsKnown_AndListedInTheHelp()
    {
        Program.Classify(["--check-security"]).Should().NotBe(Program.Startup.Usage);
        Program.HelpText.Should().Contain("`--check-security");
        FeaturesMode.Listed.Should().Contain("securityWords").And.Contain("checkSecurity");
    }
}
