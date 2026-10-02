using CoaiMcp.Core.QuestionConsult;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A web row is given strictly the question — no code, no path, no config, no secret, nothing of this
/// machine (PLAN_question_consultant.md, A2; D10). The sanitiser REFUSES and never redacts: every
/// refusal names its class and its cure, and a clean question passes through unchanged (S1 acceptance 3).
/// </summary>
/// <remarks>
/// One test per refusal class of §4, and the class list is pinned so a class added without a test — or a
/// test left over from a class that was dropped — is red. The refusal sentences never quote what they
/// refused: a web row's refusal goes into the question's record, and a secret in a reason is a secret
/// on disk.
/// </remarks>
public sealed class WebQuestionSanitiserTests
{
    private static readonly WebQuestionContext Here = new("D:/rsd/dew_flow_connect_other_ais", ["D:/projects/alpha", "D:/projects/beta-shop"]);

    private static WebQuestion.Refused RefusedAs(string question, string cls, WebQuestionContext? context = null)
    {
        var refused = WebQuestionSanitiser.Check(question, context ?? Here).Should().BeOfType<WebQuestion.Refused>()
            .Which;
        refused.Class.Should().Be(cls, $"the class decides the cure: {refused.Reason}");
        refused.Cure.Should().NotBeEmpty("every refusal names its cure");
        refused.Reason.Should().NotBeEmpty();

        return refused;
    }

    [Theory]
    [InlineData("What is the best way to configure Serilog so a stdio host logs to stderr and to a file per run?")]
    [InlineData("How does the OpenAI Responses API report cached prompt tokens, and since which version?")]
    [InlineData("Is TypeScript 7 supported by typescript-eslint 8.70, or does it still refuse TS 7.0?")]
    [InlineData("Which version of codex-cli first made --search a top-level flag? See https://github.com/openai/codex/releases")]
    [InlineData("What does `--restricted` do in Claude Code 2.1?")]
    public void ACleanQuestion_PassesUnchanged(string question)
    {
        WebQuestionSanitiser.Check(question, Here).Should().BeOfType<WebQuestion.Clean>()
            .Which.Text.Should().Be(question, "nothing is ever redacted, trimmed or rewritten");
    }

    [Fact]
    public void TheRefusalClasses_AreTheElevenOfSection4_PlusTheTwoOfS4b_AndEachHasATestBelow()
    {
        // The checkout and the roots are asked BEFORE the generic path shapes, so a question naming the
        // checkout is refused as `repository` — the more specific cure — rather than as a path. S4b added
        // `invisible` (a Unicode format character, after the secret, which is found on the normalised text) and
        // `scheme` (an address that is not http, https or ftp).
        WebQuestionSanitiser.RefusalClasses.Should().Equal(
            "empty", "secret", "invisible", "code-fence", "inline-code", "stack-trace", "config-line", "repository", "root", "path", "scheme", "internal-host", "too-long");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    public void AnEmptyQuestion_IsRefused(string question) =>
        RefusedAs(question, "empty").Cure.Should().Contain("ask");

    [Fact]
    public void ASecretShape_IsRefused_AndTheRefusalDoesNotQuoteIt()
    {
        const string token = "sk-live-0123456789abcdefghijklmnop";
        var refused = RefusedAs($"Why does the vendor answer 401 to {token} on the models endpoint?", "secret");

        refused.Reason.Should().NotContain(token, "a refusal is written down, and a secret in a reason is a secret on disk");
        refused.Cure.Should().NotContain(token);
        refused.Cure.Should().Contain("vendor", "the cure says where the text would have gone");
    }

    [Fact]
    public void ACodeFence_IsRefused_TellingTheCallerToDescribeTheCode() =>
        RefusedAs("Why does this throw?\n```csharp\nvar x = Foo();\n```", "code-fence").Cure.Should().Contain("words");

    [Fact]
    public void TwoInlineCodeSpans_AreRefused_AndOneIsAllowed()
    {
        RefusedAs("Should `PeriodicTimer` replace `System.Threading.Timer` here?", "inline-code").Cure.Should().Contain("one");
        WebQuestionSanitiser.Check("Should `PeriodicTimer` replace the old timer here?", Here).Should().BeOfType<WebQuestion.Clean>();
    }

    [Theory]
    [InlineData("It fails with\n   at CoaiMcp.Runners.Processes.ProcessLauncher.RunAsync(ProcessRequest request)\nwhat does it mean?")]
    [InlineData("Traceback (most recent call last):\n  File \"app.py\", line 3, in <module>")]
    [InlineData("System.InvalidOperationException: the sequence contains no elements — why?")]
    [InlineData("TypeError: x is not iterable")]
    [InlineData("thread 'main' panicked at src/main.rs:12:5")]
    public void AStackTraceLine_IsRefused(string question) =>
        RefusedAs(question, "stack-trace").Cure.Should().Contain("one sentence");

    [Theory]
    [InlineData("Is COAI_DATA_DIR=D:/data a sensible setting for a NAS?")]
    // Not `max_tokens=…`: `token` is one of the credential words, so that line is a SECRET by the
    // product's own rule and is refused as one before this class is reached — which is the order.
    [InlineData("I have max_lines=8192 and the answer is cut — why?")]
    [InlineData("My settings say\nSerilog.MinimumLevel: Debug\nis that right?")]
    [InlineData("TIMEOUT_MINUTES: 10 — is that enough?")]
    public void AConfigLine_IsRefused(string question) =>
        RefusedAs(question, "config-line").Cure.Should().Contain("prose");

    [Fact]
    public void ProseWithAColon_IsNotAConfigLine()
    {
        // `Question: …` and `Note: …` are how people write, and a key that is one capitalised word
        // followed by a sentence is prose, not a setting.
        WebQuestionSanitiser.Check("Question: which Node version dropped the --experimental-vm flag?", Here)
            .Should().BeOfType<WebQuestion.Clean>();
    }

    [Theory]
    [InlineData("Why does C:\\Users\\me\\AppData\\Roaming\\npm\\codex.cmd truncate arguments?")]
    [InlineData("The file lives at \\\\nas\\share\\coai — is UNC supported?")]
    [InlineData("I keep it under ~/.codex/config.toml, is that the right place?")]
    [InlineData("Running ./scripts/run-tests.mjs fails on Windows — why?")]
    [InlineData("Is src/Server/PanelService.cs too long at 1884 lines?")]
    [InlineData("Where should appsettings.json live for a worker service?")]
    [InlineData("Can WebFetch open file:///tmp/x?")]
    public void APathShape_IsRefused(string question) =>
        RefusedAs(question, "path").Cure.Should().Contain("describe");

    [Theory]
    [InlineData("How is the dew_flow_connect_other_ais repository usually deployed?", "repository")]
    [InlineData("Everything under D:/rsd/dew_flow_connect_other_ais is slow to index — why?", "repository")]
    [InlineData("The alpha project uses an older eslint; is beta-shop on the same version?", "root")]
    public void TheCheckoutAndEveryRoot_AreRefusedByPathAndByName(string question, string cls) =>
        RefusedAs(question, cls).Cure.Should().Contain("machine");

    [Fact]
    public void ARepositoryNameAlsoUsedAsAnOrdinaryWord_IsNotRefusedWhenItIsShort()
    {
        // A checkout called `api` must not make every question about an API unaskable: a name shorter
        // than four characters is not matched as a name, and the path itself still is.
        var context = new WebQuestionContext("D:/work/api", []);

        WebQuestionSanitiser.Check("Which API returns cached token counts?", context).Should().BeOfType<WebQuestion.Clean>();
        RefusedAs("Why is D:/work/api slow?", "repository", context);
    }

    [Theory]
    [InlineData("Our Ollama is at http://192.168.1.20:11434 — is that endpoint shape right?")]
    [InlineData("Why does localhost:5310 refuse the request?")]
    [InlineData("The server is build01.corp.internal — can it reach npm?")]
    [InlineData("Is 10.0.0.7 a sensible gateway?")]
    [InlineData("Why does 127.0.0.1 answer and 172.20.3.4 not?")]
    public void AnInternalHostOrAddress_IsRefused(string question) =>
        RefusedAs(question, "internal-host").Cure.Should().Contain("network");

    [Fact]
    public void APublicHost_IsAllowed()
    {
        WebQuestionSanitiser.Check("Does registry.npmjs.org rate-limit unauthenticated version lookups?", Here)
            .Should().BeOfType<WebQuestion.Clean>();
    }

    [Fact]
    public void AQuestionPastSixHundredCharacters_IsRefused_NamingTheLimit()
    {
        var question = string.Join(' ', Enumerable.Repeat("why is the sky blue and the grass green", 20));
        question.Length.Should().BeGreaterThan(WebQuestionSanitiser.MaxChars);

        var refused = RefusedAs(question, "too-long");

        refused.Cure.Should().Contain(WebQuestionSanitiser.MaxChars.ToString());
        WebQuestionSanitiser.MaxChars.Should().Be(600, "the plan's §4 figure");
    }

    [Fact]
    public void ExactlySixHundredCharacters_Pass()
    {
        var question = new string('q', 594) + " blue?";
        question.Length.Should().Be(600);

        WebQuestionSanitiser.Check(question, Here).Should().BeOfType<WebQuestion.Clean>();
    }

    // ---------- S4b item 4: other schemes, invisible characters, compatibility forms ----------

    [Theory]
    [InlineData("Can the agent open vscode://settings/editor.fontSize from a link?")]
    [InlineData("Does ssh://git@build-runner/repo need an agent forwarded?")]
    [InlineData("Is smb://nas/projects mounted read-only by default?")]
    [InlineData("Why does git+ssh://example.com/x.git ask for a password?")]
    public void AnAddressOfAnyOtherScheme_IsRefused_OnlyHttpHttpsAndFtpAreSetAside(string question) =>
        RefusedAs(question, "scheme").Cure.Should().Contain("http");

    [Theory]
    [InlineData("Which version of codex-cli first made --search a top-level flag? See https://github.com/openai/codex/releases")]
    [InlineData("Is ftp://ftp.gnu.org/gnu/ still the canonical mirror?")]
    [InlineData("Does HTTP://Example.COM/Path differ from the lower-case spelling?")]
    public void AnHttpHttpsOrFtpLink_MayStillBeCited(string question) =>
        WebQuestionSanitiser.Check(question, Here).Should().BeOfType<WebQuestion.Clean>();

    [Theory]
    [InlineData("Why does C:\u200B\\work\\app fail on the second attempt?")]
    [InlineData("Is the \u202Eflag reversed\u202C in this output?")]
    [InlineData("Which\u2060word joiner breaks the search?")]
    [InlineData("A soft\u00ADhyphen in a question — is that fine?")]
    public void AQuestionCarryingInvisibleFormatCharacters_IsRefused_NamingTheCure(string question)
    {
        var refused = RefusedAs(question, "invisible");

        refused.Cure.Should().Contain("retype");
    }

    [Fact]
    public void ASecretSplitByAZeroWidthSpace_IsStillASecret()
    {
        // The secret outranks the invisible character: it is found on the normalised text, where the split is gone.
        RefusedAs("Why does sk-\u200Blive-0123456789abcdefghijklmnop answer 401?", "secret");
    }

    [Theory]
    [InlineData("Why does \uFF23\uFF1A\uFF3C\uFF57\uFF4F\uFF52\uFF4B\uFF3C\uFF41\uFF50\uFF50 throw?", "path")]
    [InlineData("Our Ollama is at \uFF11\uFF19\uFF12.\uFF11\uFF16\uFF18.1.20 — is that the right shape?", "internal-host")]
    [InlineData("Is \uFF53\uFF4B-live-0123456789abcdefghijklmnop a valid key format?", "secret")]
    public void ACompatibilityFormSpelling_IsCheckedAsTheTextItNormalisesTo(string question, string cls) =>
        RefusedAs(question, cls);

    [Fact]
    public void TheFirstClassFound_IsTheOneNamed_AndTheOrderPutsASecretFirst()
    {
        // A question that trips several classes gets ONE refusal, and a secret outranks the rest: it
        // is the one thing the caller must act on before anything else about the question matters.
        var refused = RefusedAs("```\nsk-live-0123456789abcdefghijklmnop\n```\nsee C:\\x\\y.cs", "secret");

        refused.Class.Should().Be("secret");
    }
}
