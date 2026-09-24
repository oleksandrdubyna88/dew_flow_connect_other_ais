using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An Antigravity reviewer whose shell command was auto-denied is asked again IN THE SAME conversation,
/// told the command will not come — never re-run from scratch into the same denial (issue #504).
/// </summary>
/// <remarks>
/// Measured on the real CLI (agy 1.2.10), 2026-09-24: a reviewer that calls <c>run_command</c> in headless
/// <c>--mode plan</c> has it auto-denied and ends its turn with <c>status: SUCCESS</c> and an empty
/// response; <c>--sandbox</c> and a project-level <c>settings.json</c> change nothing. Resumed with
/// <c>--conversation</c> and told that commands are unavailable, the same conversation answered with the
/// schema's JSON in its second turn. The ordinary repair — a fresh launch of the same question — met the
/// same denial, which is how #504's rounds ended "unparseable … after one repair attempt".
/// </remarks>
public sealed class ADeniedCommandIsAskedAgainTests
{
    private const string Denied =
        "jetski: no output produced — a tool required the \"command\" permission that headless mode cannot "
        + "prompt for, so it was auto-denied.";

    private static readonly string DeniedStream =
        """{"event":"init","conversation_id":"conv-7","init":{"model":"gemini-3.8-flash-low"}}""" + "\n"
        + """{"event":"result","result":{"conversation_id":"conv-7","status":"SUCCESS","response":""}}""" + "\n";

    private static string Transcript(string stdout, string stderr) => $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}";

    private static ReviewerInvocation First() =>
        new AntigravityRuntime().Build("Architecture", "review this", Path.GetTempPath(), "schema.json", Path.GetTempPath(),
            new ReviewerSettings("antigravity") { Timeout = TimeSpan.FromMinutes(1) });

    [Fact]
    public void ADeniedCommand_IsFollowedUpInTheSameConversation_ToldTheCommandWillNotCome()
    {
        var first = First();

        var next = first.Adapter!.FollowUp(first, Transcript(DeniedStream, Denied));

        next.Should().NotBeNull();
        var args = next!.Request.Arguments;
        args.Should().ContainInConsecutiveOrder("--conversation", "conv-7");
        // The follow-up is as read-only as the first launch.
        args.Should().ContainInConsecutiveOrder("--mode", "plan");
        args.Should().ContainInConsecutiveOrder("--json-schema", "schema.json");
        next.Request.StdIn.Should().Contain("not available").And.Contain("Answer now");
    }

    [Fact]
    public void AFirstLaunchThatWasNotDenied_HasNoFollowUp_AndTheOrdinaryRepairRuns()
    {
        var first = First();

        first.Adapter!.FollowUp(first, Transcript("""{"event":"result","result":{"response":"prose"}}""", string.Empty))
            .Should().BeNull();
        first.Adapter.FollowUp(first, Transcript(string.Empty, Denied))
            .Should().BeNull("with no conversation id there is nothing to continue");
    }

    [Theory]
    // Another key order, or whitespace, is still the init event — read by its property, not by a prefix.
    [InlineData("""{"conversation_id":"conv-9","event":"init"}""", "conv-9")]
    [InlineData("""{ "event": "init", "conversation_id": "conv-9" }""", "conv-9")]
    // A conversation id that is not a string, or does not look like an id, is no id: no follow-up, and
    // never an exception out of the executor's failure path. (codex and our own reviewer, the code round.)
    [InlineData("""{"event":"init","conversation_id":7}""", "")]
    [InlineData("""{"event":"init","conversation_id":"a b; rm -rf ."}""", "")]
    public void TheConversationId_IsReadByItsProperty_AndOnlyWhenItLooksLikeOne(string init, string expected) =>
        AntigravityStream.ConversationId(Transcript(init, Denied)).Should().Be(expected);

    [Fact]
    public void AnEmptyDeniedActionsList_IsNoDenial()
    {
        AntigravityStream.WasDenied(Transcript("""{"event":"step_update","denied_actions":[]}""", string.Empty)).Should().BeFalse();
        AntigravityStream.WasDenied(Transcript("""{"event":"step_update","denied_actions":[{"action":"command"}]}""", string.Empty))
            .Should().BeTrue();
    }

    [Fact]
    public async Task TheExecutor_RunsTheFollowUp_InsteadOfTheRepair_AndCountsBothLaunches()
    {
        var launcher = new ByConversationLauncher();
        var executor = new ReviewerExecutor(launcher);
        var first = First();
        var genericRepair = first with { Request = first.Request with { Arguments = ["--generic-repair"] } };

        var outcome = await executor.RunAsync(first, genericRepair, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Ok>().Which.Repaired.Should().BeTrue();
        launcher.Launched.Should().HaveCount(2);
        launcher.Launched[1].Should().Contain("--conversation", "the second launch continued the first");
        launcher.Launched.Should().NotContain(argv => argv.Contains("--generic-repair"), "the fresh repair would meet the same denial");
    }

    [Fact]
    public async Task AFollowUpThatAlsoSaysNothing_IsStillUnparseable_AndSaysWhy()
    {
        // Success is an ANSWER, never "a second launch happened": the continued conversation is parsed
        // like any repair, and one that is denied again ends as the round always did. (the plan round.)
        var executor = new ReviewerExecutor(new ByConversationLauncher(answerTheFollowUp: false));
        var first = First();

        var outcome = await executor.RunAsync(first, repair: null, TestContext.Current.CancellationToken);

        outcome.Should().BeOfType<ReviewerOutcome.Unparseable>()
            .Which.Reason.Should().Contain("empty answer").And.Contain("auto-denied");
    }

    /// <summary>Denies the first launch; answers a launch that continues a conversation (unless told not to).</summary>
    private sealed class ByConversationLauncher(bool answerTheFollowUp = true) : IProcessLauncher
    {
        public List<IReadOnlyList<string>> Launched { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Launched.Add(request.Arguments);
            var answered = """{"event":"result","result":{"conversation_id":"conv-7","status":"SUCCESS","response":"{\"findings\": []}"}}""";

            return Task.FromResult(request.Arguments.Contains("--conversation") && answerTheFollowUp
                ? new ProcessResult(0, answered + "\n", string.Empty, false)
                : new ProcessResult(0, DeniedStream, Denied, false));
        }
    }
}
