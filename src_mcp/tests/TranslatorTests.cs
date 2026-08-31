using Xunit;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Translation;
using FluentAssertions;

namespace CoaiMcp.Tests;

public sealed class LanguageTests
{
    [Theory]
    [InlineData("en", "English")]
    [InlineData("es", "Spanish")]
    [InlineData("de", "German")]
    [InlineData("ru", "Russian")]
    [InlineData("uk", "Ukrainian")]
    [InlineData("RU", "Russian")]
    [InlineData(" uk ", "Ukrainian")]
    public void TheFiveLanguages_ResolveFromTheirCodes(string code, string english) =>
        Language.For(code).EnglishName.Should().Be(english);

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData(null)]
    public void AnUnknownCode_IsEnglish_NeverAFailure(string? code) =>
        Language.For(code).Should().Be(Language.English,
            "a question in English is still a question; a refused escalation is not");

    [Fact]
    public void ThePrompt_ForbidsCommentary_AndDemandsPassThrough()
    {
        var prompt = TranslationPrompt.For(Language.Ukrainian, "question");

        prompt.Should().Contain("Ukrainian").And.Contain("українська");
        prompt.Should().Contain("ALREADY").And.Contain("unchanged");
        prompt.Should().Contain("ONLY the translation");
        prompt.Should().Contain("file paths");
    }
}

[Collection("fakecli-env")]
public sealed class CliTranslatorTests : IDisposable
{
    private static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public CliTranslatorTests() => Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");

    public void Dispose()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    private static CliTranslator Translator(string provider = "gemini", string exe = "") =>
        new(new ProcessLauncher(), new TranslatorSettings(provider)
        {
            ExecutablePath = exe.Length > 0 ? exe : FakeCliExe,
            Timeout = TimeSpan.FromSeconds(20),
        });

    [Fact]
    public async Task ATranslation_ReplacesTheText_AndKeepsTheOriginal()
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"response": "Два зауваження досі блокують. Все одно випускати?"}""");

        var result = await Translator().TranslateAsync(
            "Two findings still gate. Ship anyway?", Language.Ukrainian, "question", TestContext.Current.CancellationToken);

        result.Text.Should().Be("Два зауваження досі блокують. Все одно випускати?");
        result.Original.Should().Be("Two findings still gate. Ship anyway?");
        result.Note.Should().BeEmpty();
        result.WasTranslated.Should().BeTrue();
    }

    [Fact]
    public async Task AModelThatFences_OrQuotes_ItsAnswer_IsCleanedUp()
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", """{"response": "```\n\"Ship anyway?\"\n```"}""");

        var result = await Translator().TranslateAsync("x", Language.English, "question", TestContext.Current.CancellationToken);

        result.Text.Should().Be("Ship anyway?", "a modal must not show a person a fenced, quoted string");
    }

    [Fact]
    public async Task WhenTheCliIsMissing_TheORIGINAL_IsShown_WithTheReason()
    {
        var result = await Translator(exe: "translator-that-does-not-exist").TranslateAsync(
            "Ship anyway?", Language.German, "question", TestContext.Current.CancellationToken);

        result.Text.Should().Be("Ship anyway?", "a question in the wrong language is a nuisance; a missing one stops a review");
        result.Note.Should().Contain("could not be started");
        result.WasTranslated.Should().BeFalse();
    }

    [Fact]
    public async Task WhenTheCliFails_TheOriginalIsShown_WithTheExitCode()
    {
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "7");

        var result = await Translator().TranslateAsync("Ship anyway?", Language.Spanish, "question", TestContext.Current.CancellationToken);

        result.Text.Should().Be("Ship anyway?");
        result.Note.Should().Contain("exited 7");
    }

    [Fact]
    public async Task WhenTheCliAnswersNothing_TheOriginalIsShown()
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", "");

        var result = await Translator().TranslateAsync("Ship anyway?", Language.Russian, "question", TestContext.Current.CancellationToken);

        result.Text.Should().Be("Ship anyway?");
        result.Note.Should().Contain("answered nothing");
    }

    [Fact]
    public async Task ProviderNone_SwitchesTranslationOff_WithoutSpawningAnything()
    {
        var result = await new CliTranslator(new ProcessLauncher(), new TranslatorSettings("none"))
            .TranslateAsync("Ship anyway?", Language.Russian, "question", TestContext.Current.CancellationToken);

        result.Text.Should().Be("Ship anyway?");
        result.Note.Should().Contain("switched off");
    }

    [Fact]
    public async Task EmptyText_IsNotSentToAModel()
    {
        var result = await Translator(exe: "translator-that-does-not-exist")
            .TranslateAsync("   ", Language.German, "question", TestContext.Current.CancellationToken);

        result.Note.Should().BeEmpty("nothing to translate is not a failure");
    }

    [Fact]
    public void TheEnvelopeComesOff_AndPlainTextSurvives()
    {
        CliTranslator.Extract("""{"response": "hola", "stats": {}}""").Should().Be("hola");
        CliTranslator.Extract("  hola  ").Should().Be("hola");
        CliTranslator.Extract("""{"not": "an envelope"}""").Should().Be("""{"not": "an envelope"}""");
    }
}
