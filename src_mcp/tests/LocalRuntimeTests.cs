using Xunit;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>
/// A model served on this machine, reviewed with directly rather than through anybody's CLI.
/// </summary>
/// <remarks>
/// <para><b>Why this is not the custom-endpoint runtime.</b> `CustomCodexRuntime` points the Codex
/// CLI at an OpenAI-compatible base, and that was tried here first: it reaches a local Ollama and
/// answers. But codex's own system prompt is 21k tokens before any review content — measured — so a
/// small-context model is refused outright and a large one pays for a prompt that has nothing to do
/// with the review. A direct call pays none of it.</para>
/// <para><b>Why a process at all, then.</b> `IReviewerRuntime.Build` returns a `ProcessRequest` and
/// the executor runs it; widening that to allow an in-process answer reaches the scheduler, the
/// concurrency accounting, the usage parser and the failure classification. So the "CLI" here is
/// this binary in `--ask-local` mode, and the local reviewer is a process like every other one. The
/// process boundary is not a consolation prize: a hung local generation is ordinary rather than
/// exceptional, and it buys the hard timeout and the guaranteed kill for free.</para>
/// </remarks>
public class LocalRuntimeTests
{
    private static ReviewerSettings Settings(string model = "qwen2.5-coder-14b:latest") =>
        new("local") { Model = model, Timeout = TimeSpan.FromMinutes(5) };

    [Fact]
    public void ItLaunchesThisBinaryInAskLocalMode_NotAVendorCli()
    {
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");

        var invocation = runtime.Build(ReviewRole.Architecture, "the prompt", "/tmp/wt",
            "/tmp/schema.json", "/tmp/out", Settings());

        invocation.Request.Arguments.Should().Contain("--ask-local");
        invocation.Request.Executable.Should().NotContain("codex")
            .And.NotContain("agy")
            .And.NotContain("claude");
    }

    [Fact]
    public void RunThroughTheDotnetHost_ItPassesItsOwnDllRatherThanAskingDotnetForAskLocal()
    {
        // The gate raised this as Blocking and it was right. `Environment.ProcessPath` is the app in
        // a Native AOT release — what ships — and is `dotnet.exe` when the same code runs
        // framework-dependent as `dotnet coai-mcp.dll`, which is how the debug build and THIS test
        // runner start. `dotnet --ask-local` dies on an unrecognised option, and the failure would
        // read as the local engine's fault.
        //
        // The test above could not catch it: it rules out codex, agy and claude, and `dotnet` is
        // none of those. A test that only rules things out cannot notice a wrong right answer.
        var (executable, prefix) = LocalRuntime.SelfInvocation();

        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            prefix.Should().ContainSingle().Which.Should().EndWith("coai-mcp.dll");
        }
        else
        {
            prefix.Should().BeEmpty("a native single-file app is started directly");
        }
    }

    [Fact]
    public void TheDllComesBeforeOurOwnFlags_OrTheHostNeverSeesTheApp()
    {
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");

        var arguments = runtime.Build(ReviewRole.Architecture, "p", "/tmp/wt", "/tmp/s.json",
            "/tmp/out", Settings()).Request.Arguments.ToList();

        var askLocal = arguments.IndexOf("--ask-local");
        askLocal.Should().BeGreaterThan(-1);
        var (_, prefix) = LocalRuntime.SelfInvocation();
        arguments.Take(askLocal).Should().Equal(prefix, "the host's arguments precede ours, in order");
    }

    [Fact]
    public void AnExplicitExecutablePathIsTakenWhole_NotHandedToAHost()
    {
        // Somebody who set a path named an executable, not a host to pass a dll to.
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");
        var settings = new ReviewerSettings("local") { ExecutablePath = "/opt/coai/coai-mcp" };

        var invocation = runtime.Build(ReviewRole.Architecture, "p", "/tmp/wt", "/tmp/s.json",
            "/tmp/out", settings);

        invocation.Request.Executable.Should().Be("/opt/coai/coai-mcp");
        invocation.Request.Arguments.First().Should().Be("--ask-local");
    }

    [Fact]
    public void TheEndpointAndTheModelTravelAsArguments()
    {
        var runtime = new LocalRuntime("local", "http://box:8000/v1");

        var invocation = runtime.Build(ReviewRole.SecurityReliability, "p", "/tmp/wt",
            "/tmp/schema.json", "/tmp/out", Settings("mixtral:latest"));

        invocation.Request.Arguments.Should().ContainInOrder("--endpoint", "http://box:8000/v1");
        invocation.Request.Arguments.Should().ContainInOrder("--model", "mixtral:latest");
    }

    [Fact]
    public void ThePromptGoesToAFile_BecauseAReviewPromptIsNotACommandLine()
    {
        // Thousands of characters of diff and schema, containing quotes, newlines and backticks.
        // Every one of today's four shell-quoting failures came from text on a command line.
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");

        var invocation = runtime.Build(ReviewRole.PlanCritique, "a prompt\nwith \"quotes\" and `ticks`",
            "/tmp/wt", "/tmp/schema.json", "/tmp/out", Settings());

        var promptFlag = invocation.Request.Arguments.ToList().IndexOf("--prompt-file");
        promptFlag.Should().BeGreaterThan(-1, "the prompt is passed as a path, never inline");
        var promptFile = invocation.Request.Arguments.ElementAt(promptFlag + 1);
        File.ReadAllText(promptFile).Should().Contain("with \"quotes\" and `ticks`");
    }

    [Fact]
    public void TheAnswerIsReadFromTheNamedFile()
    {
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");
        var invocation = runtime.Build(ReviewRole.UxDxPerformance, "p", "/tmp/wt",
            "/tmp/schema.json", Path.GetTempPath(), Settings());

        invocation.OutputFile.Should().NotBeEmpty("the shim writes where the executor already looks");
        File.WriteAllText(invocation.OutputFile, """{"findings":[]}""");

        // Through the interface, because that is how the executor reads it — the default
        // `ReadAnswer` is exactly the behaviour wanted here, and overriding it to say so would be
        // code that exists only to be found by a test.
        ((IReviewerRuntime)runtime)
            .ReadAnswer(invocation, new Runners.Processes.ProcessResult(0, "", "", false))
            .Should().Be("""{"findings":[]}""");
    }

    [Fact]
    public void AnEmptyEndpointFallsBackToOllamasOwnDefault()
    {
        // A scripted run with no panel has no probe to inherit an address from, and 11434 is the
        // only port Ollama is ever on unless somebody moved it.
        var runtime = new LocalRuntime("local", "");

        var invocation = runtime.Build(ReviewRole.Architecture, "p", "/tmp/wt", "/tmp/s.json",
            "/tmp/out", Settings());

        invocation.Request.Arguments.Should().ContainInOrder("--endpoint", "http://127.0.0.1:11434/v1");
    }

    [Fact]
    public void TokensComeFromTheEndpointsOwnReport()
    {
        // Measured against the real endpoint: Ollama's /v1 answers with
        // `usage: {prompt_tokens, completion_tokens, total_tokens}`. So a local round is counted like
        // any other, and the spending chart shows tokens rather than a dash.
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");
        var invocation = runtime.Build(ReviewRole.Architecture, "p", "/tmp/wt", "/tmp/s.json",
            "/tmp/out", Settings());

        var usage = runtime.ReadUsage(invocation, new Runners.Processes.ProcessResult(
            0, """{"tokensIn":188,"tokensOut":42}""", "", false));

        usage.TokensIn.Should().Be(188);
        usage.TokensOut.Should().Be(42);
    }

    [Fact]
    public void MoneyIsNeverReported_BecauseThereIsNoBill()
    {
        // A model on your own hardware costs electricity and a busy card, neither of which this
        // product can see. Reporting 0 would read as free, and free and unpriced are different
        // facts — the same rule the spending chart already follows for codex and antigravity.
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");
        var invocation = runtime.Build(ReviewRole.Architecture, "p", "/tmp/wt", "/tmp/s.json",
            "/tmp/out", Settings());

        runtime.ReadUsage(invocation, new Runners.Processes.ProcessResult(
            0, """{"tokensIn":1,"tokensOut":1}""", "", false)).CostUsd.Should().BeNull();
    }

    [Fact]
    public void TheRequestBodyPinsSamplingAndDemandsTheSchema()
    {
        // Two things learned elsewhere and not negotiable here. Sampling: Ollama's /v1 route
        // substitutes its own defaults over anything in a Modelfile, so temperature and seed travel
        // IN the request or a measurement is not reproducible. Schema: `json_object` was tried
        // against the real endpoint and answered with an invented shape — only `json_schema` binds.
        var body = LocalAsk.RequestBody("m", "the prompt", """{"type":"object"}""", 4321);

        body.Should().Contain("\"temperature\":0");
        body.Should().Contain("\"seed\":4321");
        body.Should().Contain("\"stream\":false");
        body.Should().Contain("json_schema");
        body.Should().NotContain("json_object");
    }

    [Fact]
    public void TheAnswerIsTakenFromTheFirstChoicesMessage()
    {
        const string response = """
            {"choices":[{"message":{"role":"assistant","content":"{\"findings\":[]}"}}],
             "usage":{"prompt_tokens":188,"completion_tokens":2}}
            """;

        var (answer, usage) = LocalAsk.ReadResponse(response);

        answer.Should().Be("""{"findings":[]}""");
        usage.TokensIn.Should().Be(188);
        usage.TokensOut.Should().Be(2);
    }

    [Fact]
    public void AnEndpointThatAnswersNonsenseIsNotAnAnswer()
    {
        // Unparseable by name, with the raw text kept — the round already handles that state, and it
        // is the honest one for an engine that returned prose instead of the schema.
        foreach (var nonsense in new[] { "", "not json", "{}", """{"choices":[]}""" })
        {
            var (answer, _) = LocalAsk.ReadResponse(nonsense);
            answer.Should().BeNull($"'{nonsense}' carries no message content");
        }
    }

    // ---------- the failure contract the gate said was unspecified ----------

    /// <summary>
    /// Every way a local engine can disappoint, and what the round is told.
    /// </summary>
    /// <remarks>
    /// Raised by this product's own gate on the plan for this feature, and it was right: the plan
    /// said the shim "prints the answer" and named no behaviour for a 500, a truncated body, an
    /// answer that is not the schema, or a host that is not there. Those are not exotic — a local
    /// engine is a process on somebody's laptop.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"choices\":[{\"message\":{}}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}")]
    [InlineData("{\"error\":{\"message\":\"model not found\"}}")]
    public void AnAnswerThatIsNotAnAnswer_IsNull_SoTheRoundSaysUnparseable(string body)
    {
        var (answer, _) = LocalAsk.ReadResponse(body);

        answer.Should().BeNull();
    }

    [Fact]
    public void ATruncatedBody_LosesTheAnswerAndTheTokens_RatherThanInventingEither()
    {
        // A generation killed mid-stream is the ordinary shape of a local timeout.
        var (answer, usage) = LocalAsk.ReadResponse("{\"choices\":[{\"message\":{\"content\":\"{\\\"findi");

        answer.Should().BeNull();
        usage.TokensIn.Should().Be(0);
        usage.TokensOut.Should().Be(0);
        usage.CostUsd.Should().BeNull();
    }

    [Fact]
    public void TokensSurviveAnAnswerThatCouldNotBeUsed()
    {
        // The run consumed the card whether or not its answer was usable, and a spending record
        // that hides a wasted run is the one thing this product has already decided it must not do.
        var (answer, usage) = LocalAsk.ReadResponse(
            "{\"choices\":[],\"usage\":{\"prompt_tokens\":900,\"completion_tokens\":0}}");

        answer.Should().BeNull();
        usage.TokensIn.Should().Be(900);
    }

    [Fact]
    public void AnUnparseableSchema_IsRefusedBeforeTheCardIsSpent()
    {
        // This assertion is the REVERSE of what it first said, and the reversal is the finding. The
        // first version fell back to an empty `{}` schema so the request could still be sent — which
        // is the same defect as the `json_object` fallback this file refuses by name: an
        // unconstrained request that a local model answers with an invented shape, bought with a
        // full generation. Raised by this product's own gate against its own plan.
        var act = () => LocalAsk.RequestBody("m", "prompt", "{ this is not json", 1);

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void TheSchemaTravelsVerbatim_NotReSerialised()
    {
        // The server already writes the finding schema to disk for the CLI reviewers; re-encoding it
        // here would be a second definition of the same thing, drifting silently.
        var body = LocalAsk.RequestBody("m", "p", """{"type":"object","required":["findings"]}""", 1);

        body.Should().Contain("\"required\":[\"findings\"]");
    }

    [Fact]
    public void TheShimIsGivenTheSameDeadlineTheExecutorWillEnforce()
    {
        // The gate's third round, accepted: the shim had its own 30-minute HttpClient timeout, longer
        // than any reasonable round, so the only real deadline was the executor killing it. Two
        // timeouts that disagree mean the shorter one always wins and the longer one is decoration —
        // and the decoration was the one that could have closed the socket politely.
        var runtime = new LocalRuntime("local", "http://127.0.0.1:11434/v1");
        var settings = new ReviewerSettings("local") { Timeout = TimeSpan.FromMinutes(7) };

        var arguments = runtime.Build(ReviewRole.Architecture, "p", "/tmp/wt", "/tmp/s.json",
            "/tmp/out", settings).Request.Arguments.ToList();

        // 410, not 420: the margin is the point. The shim must reach its own deadline BEFORE
        // the executor kills it, or it never gets to say why it gave up.
        arguments.Should().ContainInOrder("--timeout-seconds", "410");
    }

    [Fact]
    public void TheShimsOwnDeadlineIsSHORTERThanTheExecutors_SoItCanReportRatherThanBeKilled()
    {
        // The point of giving it one at all: a shim that reaches its own deadline exits with a
        // reason, while a shim that is killed leaves the round guessing. A margin of a few seconds
        // is what makes the difference observable.
        var seconds = LocalAsk.ShimDeadlineSeconds(TimeSpan.FromMinutes(10));

        seconds.Should().BeLessThan(600).And.BeGreaterThan(540);
    }

    [Fact]
    public void AnAbsurdlyShortReviewerTimeoutStillLeavesTheShimTimeToAnswer()
    {
        // A floor, because the margin must not eat the whole budget: with a 5-second reviewer
        // timeout, subtracting ten would leave a negative deadline and every local round would fail
        // before it started.
        LocalAsk.ShimDeadlineSeconds(TimeSpan.FromSeconds(5)).Should().BeGreaterThan(0);
    }
}
