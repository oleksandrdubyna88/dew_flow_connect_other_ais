using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One reading of a failed consultation launch, one word per thing a person would do differently, and one
/// vocabulary both halves of the product hold.
/// </summary>
/// <remarks>
/// Epic 2 of PLAN_the_consultant_works_on_every_vendor.md, E2.1 and E2.2. The scenario half — the words
/// reaching the caller, the record, the database and the health files through the real flow — is
/// <see cref="ConsultFailureScenarioTests"/> and <see cref="ConsultAntigravityDenialScenarioTests"/>.
/// </remarks>
public sealed class ConsultFailureTests
{
    private static readonly IConsultantRuntime Agy = new AntigravityConsultant(new AntigravityRuntime());
    private static readonly IConsultantRuntime Codex = new CodexConsultant(new CodexRuntime());

    /// <summary>A killed launch whose tokens nobody cancelled — what the service hands Classify then.</summary>
    private static readonly ConsultFailure OwnTimeout = new ConsultFailure.Timeout();

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "antigravity", name));

    private static ReviewerLaunch Ended(ReviewerOutcome outcome, string stdout = "", string stderr = "", bool cancelled = false) =>
        new(outcome, null, Usage.None, string.Empty, new ProcessResult(1, stdout, stderr, outcome is ReviewerOutcome.TimedOut, cancelled));

    private static ReviewerLaunch Silent(string stdout, string stderr) =>
        new(null, string.Empty, Usage.None, $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}", new ProcessResult(0, stdout, stderr, false));

    // ---------- the vocabulary ----------

    private static JsonElement Shared()
    {
        var path = Path.Combine(ProductionSources.RepositoryRoot(), "shared", "consult-failure-kinds.json");
        using var parsed = JsonDocument.Parse(File.ReadAllBytes(path));

        return parsed.RootElement.Clone();
    }

    /// <summary>Every case of the closed hierarchy, by reflection — never a list this test keeps.</summary>
    private static IReadOnlyList<ConsultFailure> EveryCase() =>
        [.. typeof(ConsultFailure).GetNestedTypes()
            .Where(type => type.IsSealed && typeof(ConsultFailure).IsAssignableFrom(type))
            .Select(type => (ConsultFailure)RuntimeHelpers.GetUninitializedObject(type))];

    private static IReadOnlyList<string> Constants() =>
        [.. typeof(ConsultFailureKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)];

    [Fact]
    public void TheVocabulary_IsTheCasesAndTheConstants_AndTheSharedFile_OneForOne()
    {
        var shared = Shared().GetProperty("kinds").EnumerateArray().Select(kind => kind.GetProperty("word").GetString()!).ToList();

        shared.Should().HaveCount(14, "the plan names fourteen kinds, and a file that did not load names none");
        EveryCase().Select(failure => failure.Kind).Should().BeEquivalentTo(shared, "every case is a word of the shared file, and every word a case");
        Constants().Should().BeEquivalentTo(shared, "and the constants the code names them by are the same words");
        shared.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void WhichKindsGiveTheCallBack_AgreesWithTheSharedFile()
    {
        var shared = Shared().GetProperty("kinds").EnumerateArray()
            .ToDictionary(kind => kind.GetProperty("word").GetString()!, kind => kind.GetProperty("givesTheCallBack").GetBoolean());

        foreach (var failure in EveryCase())
        {
            failure.GivesTheCallBack.Should().Be(shared[failure.Kind], failure.Kind);
        }

        shared.Where(pair => !pair.Value).Select(pair => pair.Key).Should().BeEquivalentTo(["cancelled", "tree-changed"]);
    }

    // ---------- the classification ----------

    [Fact]
    public void ACommandDeniedInTheStream_IsCommandDenied()
    {
        ConsultFailures.Classify(Agy, Silent(Fixture("consult-denied.ndjson"), Fixture("consult-denied.stderr.txt")), "antigravity", OwnTimeout)
            .Should().BeOfType<ConsultFailure.CommandDenied>();
    }

    [Fact]
    public void AReadDenied_IsReadDenied_NamingThePermission()
    {
        var stream = Fixture("consult-denied.ndjson").Replace("\"action\":\"command\"", "\"action\":\"read_url\"", StringComparison.Ordinal);

        ConsultFailures.Classify(Agy, Silent(stream, string.Empty), "antigravity", OwnTimeout)
            .Should().Be(new ConsultFailure.ReadDenied("read_url"));
    }

    [Fact]
    public void ADenialOutsideTheAllowlist_IsAnEmptyAnswer_WithWhatTheCliSaid()
    {
        var stream = Fixture("consult-denied.ndjson").Replace("\"action\":\"command\"", "\"action\":\"write_file\"", StringComparison.Ordinal);

        ConsultFailures.Classify(Agy, Silent(stream, "agy: the turn ended"), "antigravity", OwnTimeout)
            .Should().Be(new ConsultFailure.Empty("agy: the turn ended"));
    }

    [Fact]
    public void TheAgyQuotaLine_IsQuota_AndAThrottle_IsRateLimited()
    {
        var quota = Fixture("consult-quota.stderr.txt").Trim();

        ConsultFailures.Classify(Agy, Ended(new ReviewerOutcome.RateLimited(quota), stderr: quota), "antigravity", OwnTimeout)
            .Should().BeOfType<ConsultFailure.Quota>();
        ConsultFailures.Classify(Codex, Ended(new ReviewerOutcome.RateLimited("503 UNAVAILABLE: high demand")), "codex", OwnTimeout)
            .Should().BeOfType<ConsultFailure.RateLimited>();
    }

    [Fact]
    public void AnUnknownOption_IsVendorRefused_AndItsCureIsUpdateTheCli()
    {
        var failure = ConsultFailures.Classify(Codex, Ended(new ReviewerOutcome.NonZeroExit(1, "error: unknown option '--restricted'")), "claude", OwnTimeout);

        failure.Should().BeOfType<ConsultFailure.VendorRefused>().Which.Cure.Should().Contain("update the CLI");
    }

    [Fact]
    public void ACliNotStarted_OrNotOnThePath_IsCliNotFound_WithTheVendorsOwnInstallCommand()
    {
        ConsultFailures.Classify(Codex, Ended(new ReviewerOutcome.NotStarted("'agy' could not be started")), "antigravity", OwnTimeout, linux: true)
            .Cure.Should().Contain("antigravity.google/cli/install.sh");
        ConsultFailures.Classify(Codex, Ended(new ReviewerOutcome.NonZeroExit(127, "sh: codex: command not found")), "codex", OwnTimeout)
            .Should().BeOfType<ConsultFailure.CliNotFound>().Which.Cure.Should().Contain("npm install -g @openai/codex");
    }

    [Fact]
    public void AKilledLaunch_IsWhatTheTokensSay_NeverWhatTheLaunchesFlagSays()
    {
        // The launcher flags `Cancelled` for whatever token it was handed — the LINKED turn token — so a
        // deadline kill flagged itself as the caller's. The service decides from the tokens and passes it in.
        var flagged = Ended(new ReviewerOutcome.TimedOut(), cancelled: true);

        ConsultFailures.Classify(Codex, flagged, "codex", OwnTimeout).Should().BeOfType<ConsultFailure.Timeout>("no token was cancelled");
        ConsultFailures.Classify(Codex, flagged, "codex", new ConsultFailure.Deadline(TimeSpan.FromMinutes(12.5))).Should().BeOfType<ConsultFailure.Deadline>();
        ConsultFailures.Classify(Codex, flagged, "codex", new ConsultFailure.Cancelled()).Should().BeOfType<ConsultFailure.Cancelled>();
    }

    [Fact]
    public void AnUnrecognisedExit_IsAnExit_WithItsCodeAndItsFirstLine()
    {
        ConsultFailures.Classify(Codex, Ended(new ReviewerOutcome.NonZeroExit(3, "codex: something went wrong\n  at frame 1")), "codex", OwnTimeout)
            .Should().Be(new ConsultFailure.Exit(3, "codex: something went wrong"));
    }

    [Fact]
    public void AConversationTheVendorDropped_IsConversationDropped()
    {
        ConsultFailures.Classify(Agy, Ended(new ReviewerOutcome.NonZeroExit(1, "conversation not found"), stderr: "Error: conversation not found"), "antigravity", OwnTimeout)
            .Should().BeOfType<ConsultFailure.ConversationDropped>();
    }

    [Fact]
    public void TheDiagnosisTable_KeepsForAsItWas_AndClassifiesEveryRow()
    {
        VendorDiagnosis.For("401 Unauthorized").Should().Be(VendorDiagnosis.Classify("401 Unauthorized")!.Cure);
        VendorDiagnosis.Classify("401 Unauthorized")!.Kind.Should().Be(DiagnosisKind.Refused);
        VendorDiagnosis.Classify("bash: agy: command not found")!.Kind.Should().Be(DiagnosisKind.NotOnPath);
        VendorDiagnosis.Classify("error: unknown option '--restricted'")!.Kind.Should().Be(DiagnosisKind.UnknownOption);
        VendorDiagnosis.Classify("nothing anybody recognises").Should().BeNull();
        VendorDiagnosis.For("nothing anybody recognises").Should().BeNull();
    }

    [Fact]
    public void AnEmptyAnswerWithNoTranscript_StillReadsTheCliOwnStderr()
    {
        var silent = new ReviewerLaunch(null, string.Empty, Usage.None, string.Empty, new ProcessResult(0, string.Empty, "agy: the turn ended early", false));

        ConsultFailures.Classify(Codex, silent, "codex", OwnTimeout).Should().Be(new ConsultFailure.Empty("agy: the turn ended early"));
    }

    [Theory]
    [InlineData("unparseable")]
    [InlineData("stood down")]
    public void AnEndingThatIsNoExit_IsNotNamedExitZero(string which)
    {
        ReviewerOutcome ending = which == "unparseable" ? new ReviewerOutcome.Unparseable("not the schema") : new ReviewerOutcome.StoodDown("quiet round");

        var failure = ConsultFailures.Classify(Codex, Ended(ending), "codex", OwnTimeout);

        failure.Should().NotBeOfType<ConsultFailure.Exit>("there was no exit code, and inventing 0 reads as success");
        failure.What("codex").Should().NotContain("exit 0");
    }

    [Fact]
    public void EveryFailureNoRetryCures_SaysSo_AndEveryResumableOneDoesNot()
    {
        // The review's decision, written as the decision: these eight end the consultation, and saying "do
        // not retry" is the honest answer to each; the transients are resumable and must not say it.
        string[] noRetryCures = ["command-denied", "read-denied", "empty", "quota", "vendor-refused", "cli-not-found", "exit", "conversation-dropped"];

        foreach (var failure in EveryCase())
        {
            failure.DoNotRetry.Should().Be(noRetryCures.Contains(failure.Kind), failure.Kind);
        }
    }

    // ---------- the builders ----------

    private static ConsultationRecord Asking() =>
        new("c0ffee00", "caller", "claude", "no-session", "/repo", "main", "0123abc", "antigravity", "m", "antigravity", "vendorRemembers", 5, "2026-10-02T10:00:00.0000000Z")
        {
            Status = ConsultationStatuses.Asking,
        };

    private static TurnFailure Turn(string handle = "") =>
        new(Asking(), "antigravity", handle, new Usage(100, 10, null), "2026-10-02T10:01:00.0000000Z");

    [Fact]
    public void ADeniedCommand_FailsTheRecord_KeepsTheConversation_AndTellsTheCallerNotToRetry()
    {
        var failed = ConsultationFailing.Silent(Turn("conv-1"), new ConsultFailure.CommandDenied { Evidence = "/data/unparseable/consult-x.txt" });

        failed.Next.Status.Should().Be(ConsultationStatuses.Failed);
        failed.Next.Handle.Should().Be("conv-1");
        failed.Next.FailureKind.Should().Be("command-denied");
        failed.Next.Evidence.Should().Be("/data/unparseable/consult-x.txt");
        failed.Next.Billed.TokensIn.Should().Be(100, "a failed turn bills too, so the next report is measured against it");
        failed.LedgerOutcome.Should().Be("command-denied");
        ConsultationFailing.Answer(failed, gaveBack: true).Should().Contain("(failure: command-denied)")
            .And.Contain("/data/unparseable/consult-x.txt")
            .And.Contain("this call was not counted against your consult cap")
            .And.NotContain("sharper problem statement");
    }

    [Theory]
    [InlineData("command-denied")]
    [InlineData("empty")]
    public void AFailureWhoseCureIsAReframedAsk_SaysANewConsultIsTheMove_NotDoNotRetry(string kind)
    {
        // The whole-branch review, M: the cure says "name in the problem the files that hold the answer", and the
        // sentence after it said "do not retry" — two instructions that contradict each other in one reply.
        ConsultFailure failure = kind == "empty" ? new ConsultFailure.Empty(string.Empty) : new ConsultFailure.CommandDenied();

        var answer = ConsultationFailing.Answer(ConsultationFailing.Silent(Turn("conv-1"), failure), gaveBack: false);

        answer.Should().NotContain("do not retry", "the cure invites a reframed ask");
        answer.Should().Contain("a NEW consult").And.Contain("names the files that hold the answer");
    }

    [Theory]
    [InlineData("quota")]
    [InlineData("cli-not-found")]
    [InlineData("vendor-refused")]
    [InlineData("exit")]
    public void AFailureNoAskCures_StillSaysDoNotRetry(string kind)
    {
        ConsultFailure failure = kind switch
        {
            "quota" => new ConsultFailure.Quota("spent"),
            "cli-not-found" => new ConsultFailure.CliNotFound("absent", "install it"),
            "vendor-refused" => new ConsultFailure.VendorRefused("no", "update the CLI"),
            _ => new ConsultFailure.Exit(1, "boom"),
        };

        ConsultationFailing.Answer(ConsultationFailing.Terminal(Turn(), failure, kind), gaveBack: false)
            .Should().Contain("do not retry this consultation; carry on, or ask the person");
    }

    [Fact]
    public void ATimeoutsCure_NamesTheSettingWhereThePanelShowsIt()
    {
        new ConsultFailure.Timeout().Cure.Should().Contain("raise *Reviewer timeout, minutes* in ConnectOtherAIs > Limits (COAI_REVIEWER_TIMEOUT_MINUTES)",
            "the setting lives on the Limits tab under that label — the old cure sent a person to the Reviewers tab");
    }

    [Fact]
    public void ATerminalLaunchWithAConversation_IsInterrupted_AndResumable()
    {
        var failed = ConsultationFailing.Terminal(Turn("conv-1"), new ConsultFailure.Timeout(), "timeout");

        failed.Next.Status.Should().Be(ConsultationStatuses.Interrupted);
        failed.Next.Handle.Should().Be("conv-1");
        failed.Next.EndedUtc.Should().BeEmpty("an interrupted consultation is not over");
        failed.LedgerOutcome.Should().Be("timeout");
        ConsultationFailing.Answer(failed, gaveBack: false).Should().Contain("NOT counted").And.Contain("consultationId c0ffee00").And.NotContain("do not retry");
    }

    [Fact]
    public void ADroppedConversation_LosesItsHandle_AndEndsTheConsultation()
    {
        var failed = ConsultationFailing.Terminal(Turn("conv-1"), new ConsultFailure.ConversationDropped(), "exit 1: conversation not found");

        failed.Next.Status.Should().Be(ConsultationStatuses.Failed);
        failed.Next.Handle.Should().BeEmpty();
        ConsultationFailing.Answer(failed, gaveBack: false).Should().Contain("no longer holds").And.Contain("start a new consultation");
    }

    [Fact]
    public void ABreach_RecordsTheAlert_AndNeverSaysTheCallWasGivenBack()
    {
        var failed = ConsultationFailing.Breach(Turn(), new ConsultFailure.TreeChanged("the working tree changed: X.cs (modified)"));

        failed.Next.Alert.Should().Be("the working tree changed: X.cs (modified)");
        failed.Next.FailureKind.Should().Be("tree-changed");
        failed.LedgerOutcome.Should().Be("tree-changed");
        ConsultationFailing.Answer(failed, gaveBack: false).Should().NotContain("not counted against your consult cap");
    }

    [Fact]
    public void AnEndingThatIsNotTransient_IsEnded_EvenWithAConversation_AndKeepsItsHandleForTheReader()
    {
        foreach (ConsultFailure ending in (ConsultFailure[])[
            new ConsultFailure.Exit(1, "boom"), new ConsultFailure.Quota("spent"), new ConsultFailure.VendorRefused("no", "update the CLI"),
            new ConsultFailure.CliNotFound("absent", "install it")])
        {
            var failed = ConsultationFailing.Terminal(Turn("conv-1"), ending, ending.Kind);

            failed.Resumable.Should().BeFalse(ending.Kind);
            failed.Next.Status.Should().Be(ConsultationStatuses.Failed, ending.Kind);
            failed.Next.Handle.Should().Be("conv-1");
            ConsultationFailing.Answer(failed, gaveBack: false).Should().NotContain("call consult again", ending.Kind);
        }
    }

    [Fact]
    public void ATransientFailureWithoutThisTurnsConversation_IsEnded_KeepingTheRecordsOldHandle()
    {
        var turn = Turn(handle: string.Empty) with { Record = Asking() with { Handle = "conv-older" } };

        var failed = ConsultationFailing.Terminal(turn, new ConsultFailure.Timeout(), "timeout");

        failed.Resumable.Should().BeFalse("the record's handle is an earlier turn's, not proof this one reached the vendor");
        failed.Next.Handle.Should().Be("conv-older");
    }

    [Fact]
    public void AFailureThatCouldNotBeRecorded_SaysSo_AndKeepsItsOwnKind()
    {
        var failed = ConsultationFailing.Silent(Turn(), new ConsultFailure.Empty(string.Empty));

        ConsultationFailing.Answer(failed, gaveBack: true, unrecorded: "access denied")
            .Should().Contain("(failure: empty)").And.Contain("the record could not be updated to say so: access denied");
    }

    [Fact]
    public void ADeadlineWithoutAConversation_FailsTheRecord_AndSaysDeadline()
    {
        var failed = ConsultationFailing.AfterTheLaunch(Turn(), new ConsultFailure.Deadline(TimeSpan.FromMinutes(12.5)));

        failed.Next.Status.Should().Be(ConsultationStatuses.Failed);
        failed.Next.Reason.Should().Contain("12.5-minute deadline");
        failed.LedgerOutcome.Should().Be("deadline");
    }

    [Fact]
    public void AnAnswer_ClearsTheFailureItFollowed()
    {
        var interrupted = ConsultationFailing.Terminal(Turn("conv-1"), new ConsultFailure.Timeout(), "timeout").Next;
        var turn = new ConsultationTurn("2026-10-02T10:02:00.0000000Z", "again", "advice", 1, 1, 1, null);

        var answered = ConsultationAnswering.Answered(interrupted, turn, "conv-1");

        answered.FailureKind.Should().BeEmpty();
        answered.FailureCure.Should().BeEmpty();
        answered.Evidence.Should().BeEmpty();
    }
}
