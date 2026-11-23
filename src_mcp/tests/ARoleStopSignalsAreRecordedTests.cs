using System.Text.Json;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E1 s3 t1 — the stops of a vendor CLI as they were really recorded (<c>tests/fixtures/role-stops</c>, described in <c>SIGNALS.md</c> of the story). Three things are held here so the document cannot drift
/// from the files: every fixture names its CLI version and its day; each fixture is read by the code AS IT IS TODAY (<see cref="ConsultFailures.Classify"/>, the one reading the failover loop builds on)
/// and the table of what it says is the table in <c>SIGNALS.md</c>; and the structured fields the document names — or says do not exist — are in the files.
/// </summary>
public sealed class ARoleStopSignalsAreRecordedTests
{
    private sealed class Answers(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) => Task.FromResult(result);
    }

    private static (IReviewerRuntime Adapter, IConsultantRuntime Consultant, string Runtime) VendorOf(string cli) => cli switch
    {
        "claude" => (new ClaudeRuntime(), new ClaudeConsultant(new ClaudeRuntime()), "claude"),
        "codex" => (new CodexRuntime(), new CodexConsultant(new CodexRuntime()), "codex"),
        _ => (new AntigravityRuntime(), new AntigravityConsultant(new AntigravityRuntime()), "antigravity"),
    };

    /// <summary>What the real executor and the real classifier say of one recorded run: the failure kind, or <c>answered</c> for a launch that exited 0.</summary>
    private static async Task<string> ReadAsync(string name, CancellationToken ct)
    {
        var fixture = RoleStopFixtures.Load(name);
        var (adapter, consultant, runtime) = VendorOf(fixture.Cli);
        var invocation = new ReviewerInvocation(fixture.Cli, "role", new ProcessRequest(fixture.Cli, [], "."), string.Empty, adapter);
        var launch = await new ReviewerExecutor(new Answers(fixture.AsProcessResult())).LaunchAsync(invocation, ct);

        return launch.Terminal is null
            ? "answered"
            : ConsultFailures.Classify(consultant, launch, runtime, new ConsultFailure.Timeout()).Kind;
    }

    [Fact]
    public void EveryFixture_NamesItsCli_ItsVersion_ItsDay_AndWhoRecordedIt()
    {
        RoleStopFixtures.Names.Should().HaveCountGreaterThanOrEqualTo(10);
        foreach (var name in RoleStopFixtures.Names)
        {
            var fixture = RoleStopFixtures.Load(name);
            fixture.Cli.Should().BeOneOf("claude", "codex", "agy");
            fixture.CliVersion.Should().NotBeNullOrWhiteSpace(name);
            fixture.RecordedUtc.Should().MatchRegex(@"^2026-\d\d-\d\d", name);
            fixture.RecordedBy.Should().NotBeNullOrWhiteSpace(name);
            (fixture.StdOut + fixture.StdErr).Should().NotBeNullOrWhiteSpace(name);
        }
    }

    [Fact]
    public void TheStopsThisStoryRecordedLive_AreTenCliRunsOfThreeVendors_WithTheirVersions()
    {
        var live = RoleStopFixtures.Names.Select(RoleStopFixtures.Load).Where(fixture => fixture.RecordedByThisStory).ToList();

        live.Should().HaveCount(10);
        live.Select(fixture => fixture.RecordedUtc[..10]).Distinct().Should().Equal("2026-11-23");
        live.Where(fixture => fixture.Cli == "claude").Select(fixture => fixture.CliVersion).Distinct().Should().Equal("2.1.330 (Claude Code)");
        live.Where(fixture => fixture.Cli == "codex").Select(fixture => fixture.CliVersion).Distinct().Should().Equal("codex-cli 0.171.0");
        live.Where(fixture => fixture.Cli == "agy").Select(fixture => fixture.CliVersion).Distinct().Should().Equal("1.5.14");
    }

    [Theory]
    // The only two stops today's classifier names from a recorded run: agy's spent quota, and an option claude does not know.
    [InlineData("agy-quota-exhausted-stderr", "quota")]
    [InlineData("claude-bad-flag", "vendor-refused")]
    // A run that exits 0 is an answer to the CLI whatever the model did: an unknown model name is NOT refused by claude 2.1.330 (it ran on the default).
    [InlineData("claude-unknown-model-name-runs-on-the-default", "answered")]
    // Everything below is a crash to the classifier today — including the three lost sign-ins and the 429 envelope, which the role layer reads on top (SIGNALS.md).
    [InlineData("claude-spend-limit-429-envelope", "exit")]
    [InlineData("claude-no-sign-in", "exit")]
    [InlineData("claude-unknown-full-model-name-budget-capped", "exit")]
    [InlineData("codex-bad-flag", "exit")]
    [InlineData("codex-bad-model", "exit")]
    [InlineData("codex-no-sign-in", "exit")]
    [InlineData("agy-bad-flag", "exit")]
    [InlineData("agy-bad-model", "exit")]
    [InlineData("agy-no-sign-in", "exit")]
    public async Task ARecordedStop_IsReadByTodaysClassifier_AsSIGNALSsaysIt(string name, string kind) =>
        (await ReadAsync(name, TestContext.Current.CancellationToken)).Should().Be(kind);

    [Fact]
    public void Claude_ReportsItsFailureInAnEnvelope_WhoseIsErrorIsTheTruth_AndANoSignInHasNoStatus()
    {
        var spend = Envelope("claude-spend-limit-429-envelope");
        var noSignIn = Envelope("claude-no-sign-in");
        var budget = Envelope("claude-unknown-full-model-name-budget-capped");

        spend.GetProperty("is_error").GetBoolean().Should().BeTrue();
        spend.GetProperty("api_error_status").GetInt32().Should().Be(429);
        noSignIn.GetProperty("is_error").GetBoolean().Should().BeTrue();
        noSignIn.GetProperty("result").GetString().Should().Be("Not logged in · Please run /login");
        noSignIn.GetProperty("api_error_status").ValueKind.Should().Be(JsonValueKind.Null);
        // The budget stop is an error envelope with NO `result` — the sentence is in `errors`, so WhyItFailed reads nothing from it.
        budget.GetProperty("is_error").GetBoolean().Should().BeTrue();
        budget.TryGetProperty("result", out _).Should().BeFalse();
        budget.GetProperty("errors")[0].GetString().Should().Contain("maximum budget");
    }

    [Fact]
    public void Claude_DoesNotRefuseAnUnknownModelName_ItRunsTheDefault_SoTheEnvelopeNamesTheModelThatRan()
    {
        var fixture = RoleStopFixtures.Load("claude-unknown-model-name-runs-on-the-default");
        var envelope = Envelope(fixture.Name);

        fixture.ExitCode.Should().Be(0);
        fixture.Argv.Should().ContainInOrder("--model", "no-such-model-xyz");
        envelope.GetProperty("is_error").GetBoolean().Should().BeFalse();
        envelope.GetProperty("modelUsage").EnumerateObject().Select(model => model.Name).Should().NotContain("no-such-model-xyz").And.ContainSingle();
    }

    [Fact]
    public void NoVendorRecordedAFieldThatSaysTheModelRefused_TheEnvelopeFieldsThatCouldHoldOneOnlyEverHeldOtherWords()
    {
        var seen = new[] { "claude-no-sign-in", "claude-spend-limit-429-envelope", "claude-unknown-full-model-name-budget-capped", "claude-unknown-model-name-runs-on-the-default" }
            .Select(Envelope).ToList();

        // claude: `stop_reason` and `terminal_reason` exist, and across every envelope on file they only ever held these words.
        seen.Select(envelope => envelope.GetProperty("stop_reason").GetString()).Distinct().Order().Should().Equal("end_turn", "stop_sequence");
        seen.Where(envelope => envelope.TryGetProperty("terminal_reason", out _)).Select(envelope => envelope.GetProperty("terminal_reason").GetString()).Distinct().Order()
            .Should().Equal("api_error", "budget_exhausted", "completed");
        seen.Where(envelope => envelope.TryGetProperty("safety_stops", out _)).Select(envelope => envelope.GetProperty("safety_stops").GetInt32()).Should().OnlyContain(count => count == 0);

        // agy: the result event has `status` (ERROR on the runs recorded, SUCCESS on the consultant's older advice) and `finished_reason` (error) — no word for a refusal.
        var agy = new[] { "agy-bad-model", "agy-no-sign-in" }.Select(name => AgyResult(RoleStopFixtures.Load(name).StdOut)).ToList();
        agy.Select(result => result.GetProperty("status").GetString()).Should().OnlyContain(status => status == "ERROR");
        agy.Where(result => result.TryGetProperty("finished_reason", out _)).Select(result => result.GetProperty("finished_reason").GetString()).Should().OnlyContain(reason => reason == "error");

        // codex: the stream's failure is a `turn.failed` event carrying a message; there is no refusal event on file.
        var codexTypes = new[] { "codex-bad-model", "codex-no-sign-in" }
            .SelectMany(name => RoleStopFixtures.Load(name).StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("type").GetString()).Distinct().Order().ToList();
        codexTypes.Should().Equal("error", "item.completed", "thread.started", "turn.failed", "turn.started");
    }

    [Fact]
    public void Codex_ReportsAFailureOnItsStdout_NeverOnStderr_AndAnUnknownModelIsRefusedAsNotSupported()
    {
        var bad = RoleStopFixtures.Load("codex-bad-model");

        bad.StdErr.Should().BeEmpty();
        bad.ExitCode.Should().Be(1);
        bad.StdOut.Should().Contain("\"type\":\"turn.failed\"").And.Contain("is not supported when using Codex with a ChatGPT account");
    }

    [Fact]
    public void Agy_RefusesAnUnknownModelBeforeAnyModelCall_NamingItsKnownModels()
    {
        var bad = RoleStopFixtures.Load("agy-bad-model");

        bad.ExitCode.Should().Be(1);
        bad.StdErr.Should().Contain("is not recognized as a known model").And.Contain("Available models:");
        AgyResult(bad.StdOut).GetProperty("usage").GetProperty("total_tokens").GetInt32().Should().Be(0);
    }

    private static JsonElement Envelope(string name) => JsonDocument.Parse(RoleStopFixtures.Load(name).StdOut).RootElement.Clone();

    private static JsonElement AgyResult(string stdout) =>
        stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .First(line => line.TryGetProperty("event", out var kind) && kind.GetString() == "result")
            .GetProperty("result").Clone();
}
