using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A reasoning model is told not to think, unless somebody asks for it.
/// </summary>
/// <remarks>
/// <para><b>Measured 2026-09-02.</b> Gemma4 26B on Ollama answered the planted-defect plan once in
/// 171 s, and on the identical request once spent 1056 s filling a 64k context with 110 000
/// characters of <c>reasoning</c> and returned an EMPTY <c>content</c>. Qwen3.5 35B never answered at
/// all. The thinking is unbounded and not reproducible, and when it outruns the context there is no
/// review — a reviewer that answers one time in two is worth less than one that never runs.</para>
///
/// <para><b>The escape was found once already, in dew_flow_rag_qln</b>
/// (`AiRuntimeOptions.cs`, measured 2026-08-11 against the same model family): on Ollama's OpenAI
/// route <c>think:false</c> and <c>chat_template_kwargs</c> are ignored, <c>reasoning_effort:"low"</c>
/// still burns the whole budget, and only <c>reasoning_effort:"none"</c> returns
/// <c>finish_reason: stop</c> with the answer in <c>content</c>. Re-verified here: 23 s, zero reasoning
/// characters, a valid findings object. That is the default. A person who wants the thinking back
/// sets <c>COAI_LOCAL_REASONING_EFFORT</c> to a level, or to <c>engine</c> to send nothing and take
/// whatever the engine does on its own.</para>
/// </remarks>
public class LocalReasoningEffortTests
{
    private const string Schema = """{"type":"object","required":["findings"]}""";

    [Fact]
    public void NoneIsTheDefault_BecauseAnAnswerThatNeverArrivesIsWorthNothing()
    {
        PanelSettings.FromEnvironment(_ => null).LocalReasoningEffort.Should().Be("none");
    }

    [Theory]
    [InlineData("low")]
    [InlineData("high")]
    [InlineData("engine")]
    public void APersonCanAskForTheThinkingBack(string level)
    {
        PanelSettings.FromEnvironment(name => name == "COAI_LOCAL_REASONING_EFFORT" ? level : null)
            .LocalReasoningEffort.Should().Be(level);
    }

    [Fact]
    public void TheRequestCarriesTheEffort()
    {
        var body = LocalAsk.RequestBody("m", "p", Schema, 1, reasoningEffort: "none");

        body.Should().Contain("\"reasoning_effort\":\"none\"");
    }

    [Theory]
    [InlineData("")]
    [InlineData("engine")]
    public void EngineOrBlankSendsNothing_SoTheEngineDecides(string effort)
    {
        // "engine" is the explicit way to say "do not tell it either way": the field is absent
        // rather than set to some value this build guessed the engine would treat as neutral.
        var body = LocalAsk.RequestBody("m", "p", Schema, 1, reasoningEffort: effort);

        body.Should().NotContain("reasoning_effort");
    }

    [Fact]
    public void TheRuntimeHandsItToTheShim()
    {
        var settings = new ReviewerSettings("local") { Model = "gemma4", ReasoningEffort = "none" };
        var invocation = new LocalRuntime("local", string.Empty)
            .Build(ReviewRole.PlanCritique, "prompt", Path.GetTempPath(), "schema.json", Path.GetTempPath(), settings);

        var args = invocation.Request.Arguments;
        var at = args.ToList().IndexOf("--reasoning-effort");
        at.Should().BeGreaterThan(0, "the shim only knows what it is told on its command line");
        args[at + 1].Should().Be("none");
    }

    [Fact]
    public void ABlankSettingReachesTheShimAsNoFlag()
    {
        var settings = new ReviewerSettings("local") { Model = "gemma4" };
        var invocation = new LocalRuntime("local", string.Empty)
            .Build(ReviewRole.PlanCritique, "prompt", Path.GetTempPath(), "schema.json", Path.GetTempPath(), settings);

        invocation.Request.Arguments.Should().NotContain("--reasoning-effort");
    }
}
