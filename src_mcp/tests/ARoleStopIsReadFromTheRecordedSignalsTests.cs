using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s3 t3 — how the role layer reads a finished launch (<see cref="RoleStops"/>): <see cref="ConsultFailures.Classify"/> and on top of it exactly the signals <c>SIGNALS.md</c> records from real runs. Every row here is a RECORDED
/// file (<c>tests/fixtures/role-stops</c>) run through the real executor and the real classifier; nothing is a sentence typed for the test.
/// </summary>
public sealed class ARoleStopIsReadFromTheRecordedSignalsTests
{
    private sealed class Answers(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => Task.FromResult(result);
    }

    private static RoleStop ReadFixture(string name, Func<string, string>? alter = null) => ReadAsync(name, alter).GetAwaiter().GetResult();

    private static async Task<RoleStop> ReadAsync(string name, Func<string, string>? alter)
    {
        var fixture = RoleStopFixtures.Load(name);
        var (runtime, runtimeName) = RoleModelLauncher.RuntimeOf(fixture.Cli);
        IReviewerRuntime adapter = fixture.Cli switch { "claude" => new ClaudeRuntime(), "codex" => new CodexRuntime(), _ => new AntigravityRuntime("agy") };
        var process = new ProcessResult(fixture.ExitCode, alter is null ? fixture.StdOut : alter(fixture.StdOut), fixture.StdErr, TimedOut: false);
        var invocation = new ReviewerInvocation(fixture.Cli, "role", new ProcessRequest(fixture.Cli, [], "."), string.Empty, adapter);
        var launch = await new ReviewerExecutor(new Answers(process)).LaunchAsync(invocation, TestContext.Current.CancellationToken);
        launch.Terminal.Should().NotBeNull($"{name} is a stop, not an answer");

        return RoleStops.Of(runtime, launch, fixture.Cli, runtimeName, new ConsultFailure.Timeout());
    }

    [Theory]
    // A limit: the recorded claude 429 envelope (read as the integer status) and agy's recorded spent quota — each skips its vendor's other models.
    [InlineData("claude-spend-limit-429-envelope", "limit", true, "monthly spend limit")]
    [InlineData("agy-quota-exhausted-stderr", "limit", true, "quota")]
    // A crash that also skips the vendor: a lost sign-in (three recorded), and an option the CLI does not know (claude's recorded wording).
    [InlineData("claude-no-sign-in", "crash", true, "the sign-in is lost")]
    [InlineData("codex-no-sign-in", "crash", true, "the sign-in is lost")]
    [InlineData("agy-no-sign-in", "crash", true, "the sign-in is lost")]
    [InlineData("claude-bad-flag", "crash", true, "unknown option")]
    // A plain crash: the next model of the same vendor may well work.
    [InlineData("claude-unknown-full-model-name-budget-capped", "crash", false, "exit 1")]
    [InlineData("codex-bad-model", "crash", false, "exit 1")]
    [InlineData("codex-bad-flag", "crash", false, "exit 2")]
    [InlineData("agy-bad-model", "crash", false, "exit 1")]
    [InlineData("agy-bad-flag", "crash", false, "exit 2")]
    public void ARecordedStop_IsReadAsItsWord_AndSaysWhetherItsVendorIsSkipped(string name, string word, bool skipsVendor, string said)
    {
        var stop = ReadFixture(name);

        (stop.Word, stop.SkipsVendor).Should().Be((word, skipsVendor));
        stop.Reason.Should().Contain(said);
        stop.FailsOver.Should().BeTrue();
        stop.Transient.Should().BeFalse("none of the recorded stops is a throttle that clears");
    }

    [Fact]
    public void AStatusThatIsTheStringTwoNineFour_IsNotTheIntegerTheLayerReads()
    {
        var stop = ReadFixture("claude-spend-limit-429-envelope", text => text.Replace("\"api_error_status\":429", "\"api_error_status\":\"429\"", StringComparison.Ordinal));

        (stop.Word, stop.SkipsVendor).Should().Be(("crash", false), "a string is not the structured field; no prose is matched");
    }

    [Fact]
    public void AnApiErrorStatusOfFiveHundredThree_WhichWasNeverRecorded_IsNotRead()
    {
        var stop = ReadFixture("claude-spend-limit-429-envelope", text => text.Replace("\"api_error_status\":429", "\"api_error_status\":503", StringComparison.Ordinal));

        stop.Word.Should().Be("crash");
    }

    [Fact]
    public void ASuccessEnvelopeWithTheSameWords_IsNoSignInLost()
    {
        var process = new ProcessResult(1, """{"is_error":false,"result":"Not logged in"}""", string.Empty, TimedOut: false);

        RoleStops.SignInLost("claude", process).Should().BeFalse("is_error decides, never the model's words");
        RoleStops.SignInLost("claude", process with { ExitCode = 0, StdOut = """{"is_error":true,"result":"Not logged in · Please run /login"}""" }).Should().BeFalse("an exit of zero is an answer");
    }

    [Fact]
    public void ATransientThrottle_IsALimitThatDoesNotSkipItsVendor_AndIsTheOneStopTheLastModelWaitsOut()
    {
        var stop = RoleStops.Of(new ConsultFailure.RateLimited("503 UNAVAILABLE: high demand"), "codex");

        (stop.Word, stop.SkipsVendor, stop.Transient, stop.FailsOver).Should().Be(("limit", false, true, true));
    }

    [Theory]
    [InlineData("quota", "limit", true)]
    [InlineData("timeout", "crash", false)]
    [InlineData("empty", "crash", false)]
    [InlineData("deadline", "crash", false)]
    [InlineData("conversation-dropped", "crash", false)]
    [InlineData("cli-not-found", "crash", true)]
    [InlineData("launch-refused", "crash", true)]
    [InlineData("vendor-refused", "crash", true)]
    [InlineData("read-denied", "crash", false)]
    public void EachOtherFailureTheClassifierNames_IsALimitOrACrash(string kind, string word, bool skipsVendor)
    {
        ConsultFailure failure = kind switch
        {
            "quota" => new ConsultFailure.Quota("spent"),
            "timeout" => new ConsultFailure.Timeout(),
            "empty" => new ConsultFailure.Empty(string.Empty),
            "deadline" => new ConsultFailure.Deadline(TimeSpan.FromMinutes(1)),
            "conversation-dropped" => new ConsultFailure.ConversationDropped(),
            "cli-not-found" => new ConsultFailure.CliNotFound("no such file", "install it"),
            "launch-refused" => new ConsultFailure.LaunchRefused("a home is not clean"),
            "vendor-refused" => new ConsultFailure.VendorRefused("refused", "fix it"),
            _ => new ConsultFailure.ReadDenied("Read"),
        };

        var stop = RoleStops.Of(failure, "claude");

        (stop.Word, stop.SkipsVendor, stop.FailsOver, stop.Kind).Should().Be((word, skipsVendor, true, kind));
    }

    [Fact]
    public void TheThreeStopsThatDoNotFailOver_AreCancelledTreeChangedAndOutputInvalid()
    {
        RoleStops.Of(new ConsultFailure.Cancelled(), "claude").FailsOver.Should().BeFalse();
        RoleStops.Of(new ConsultFailure.TreeChanged("the tree changed"), "claude").Word.Should().Be("tree-changed");
        new RoleStop(RoleStopWords.OutputInvalid, "x", "output-invalid", false, false).FailsOver.Should().BeFalse();
        new[] { RoleStopWords.Limit, RoleStopWords.Refusal, RoleStopWords.Crash }.Should().OnlyContain(word => new RoleStop(word, "x", "x", false, false).FailsOver);
    }

    [Fact]
    public void AReasonIsRedactedAndOneLine_BeforeItIsWrittenAnywhere()
    {
        var stop = RoleStops.Of(new ConsultFailure.Exit(1, "failed with token=abcdef1234567890abcdef\nsecond line"), "claude");

        stop.Reason.Should().NotContain("abcdef1234567890abcdef").And.NotContain("\n");
    }

    // ---- the output arm: the only refusal there is ----

    [Theory]
    [InlineData("""{"status":"refused","reason":"no"}""", "Refused")]
    [InlineData("""{"status":"refused"}""", "Refused")]
    [InlineData("""{"status":"ok"}""", "Valid")]
    [InlineData("I'm sorry, but I can't help with that.", "Invalid")]
    [InlineData("""{"status":"declined"}""", "Valid")]
    [InlineData("[]", "Invalid")]
    public void TheStatusArm_IsTheOnlyRefusal_AndProseIsNeverMatched(string answer, string kind)
    {
        var verdict = RoleAnswers.Read(answer, _ => string.Empty);

        verdict.GetType().Name.Should().Be(kind);
    }

    [Fact]
    public void ARefusalWithNoReason_SaysSo_AndTheRolesOwnCheckIsAskedOnlyOfAnObjectThatIsNotRefused()
    {
        RoleAnswers.Read("""{"status":"refused"}""", _ => throw new InvalidOperationException("the role's check must not see a refusal"))
            .Should().BeOfType<RoleAnswerVerdict.Refused>().Which.Reason.Should().Contain("no reason");
        RoleAnswers.Read("""{"status":"ok"}""", _ => "the plan has no stories").Should().BeOfType<RoleAnswerVerdict.Invalid>().Which.Reason.Should().Be("the plan has no stories");
    }
}
