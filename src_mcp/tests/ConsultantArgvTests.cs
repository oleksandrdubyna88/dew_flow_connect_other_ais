using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every consult flag asserted literally. These argvs were verified against the real CLI
/// (codex-cli 0.153.4) on 2026-09-12 — see PLAN_consultant.md, phase 0b.
/// </summary>
public sealed class ConsultantArgvTests
{
    private const string Repo = "D:/rsd/some-checkout";
    private const string Answers = "D:/data/consultations/answers";

    private static CodexConsultant Consultant() => new(new CodexRuntime());

    private static ReviewerInvocation Build(string handle = "", string prompt = "help me", string model = "") =>
        Consultant().Build(new ConsultantLaunch(Repo, prompt, handle, Answers, new ReviewerSettings("codex") { Model = model }));

    [Fact]
    public void AFirstTurn_IsReadOnly_AndCarriesNoEphemeral()
    {
        // MEASURED 2026-09-12: with --ephemeral the thread cannot be resumed — `thread/resume
        // failed: no rollout found` in 0.7 s. Copying the review argv wholesale would make every
        // turn 2 a fresh conversation that LOOKS like a model which lost the thread.
        var args = Build().Request.Arguments;

        args.Should().ContainInOrder("exec", "-s", "read-only");
        args.Should().NotContain("--ephemeral", "a consultation must be resumable");
        // The expectations are an ARRAY: `ContainInOrder("-C", Repo, "because…")` would assert the
        // reason as a third expected ITEM, which is a trap this family has paid for before.
        args.Should().ContainInOrder(["-C", Repo], "the consultant stands in the live checkout");
        args.Should().Contain("--json").And.Contain("-o");
        args[^1].Should().Be("-", "the prompt comes from stdin");
    }

    [Fact]
    public void NoSchemaFlag_BecauseAConsultationAnswersProse()
    {
        Build().Request.Arguments.Should().NotContain("--output-schema");
    }

    [Fact]
    public void AResumedTurn_CarriesTheHandle_AndTheSandboxAsAConfigOverride()
    {
        // `codex exec resume` accepts neither -s nor -C (measured): the sandbox rides a config
        // override, and the cwd — which the launch sets to the repository — is what locates the thread.
        var invocation = Build(handle: "0198f2c1-aaaa-bbbb-cccc-0123456789ab");
        var args = invocation.Request.Arguments;

        args.Should().ContainInOrder("exec", "resume", "0198f2c1-aaaa-bbbb-cccc-0123456789ab");
        args.Should().ContainInOrder("-c", "sandbox_mode=read-only");
        args.Should().NotContain("-s").And.NotContain("-C");
        invocation.Request.WorkingDirectory.Should().Be(Repo);
    }

    [Fact]
    public void TheModelRidesWhenOneIsChosen_AndNotOtherwise()
    {
        Build(model: "gpt-5.6-luna").Request.Arguments.Should().ContainInOrder("-m", "gpt-5.6-luna");
        Build().Request.Arguments.Should().NotContain("-m");
    }

    [Fact]
    public void NoArgument_EverContainsANewline()
    {
        // The rule the first real run wrote: cmd.exe parses every npm .cmd shim on Windows and
        // truncates an argument at its first newline, silently.
        var multiline = "## The budget\nYou have 5 turns\n\n## The question\nwhy is it red";
        var invocation = Build(prompt: multiline);

        invocation.Request.Arguments.Should().OnlyContain(a => !a.Contains('\n'));
        invocation.Request.StdIn.Should().Be(multiline);
    }

    [Fact]
    public void TheRoleIsConsult_SoTheLaunchIsLabelledAsOne()
    {
        // Roles became strings in cdbbc84, so a consultation needs no enum value — and the
        // executor's TrackAs, the ledger and the evidence name all read this one field.
        Build().Role.Should().Be("consult");
    }

    [Fact]
    public void AMalformedHandle_NeverReachesACommandLine()
    {
        foreach (var hostile in (string[])["a & calc", "a|b", "\"x\"", "../../etc/passwd", new string('a', 200), " "])
        {
            var build = () => Build(handle: hostile);

            build.Should().Throw<ArgumentException>($"'{hostile}' is not a handle and this argv goes through cmd.exe");
        }
    }

    [Fact]
    public void TheOutputFileIsInTheAnswersDirectory_NeverInTheRepository()
    {
        var file = Build().OutputFile;

        Path.GetDirectoryName(Path.GetFullPath(file)).Should().Be(Path.GetFullPath(Answers));
        Path.GetFullPath(file).Should().NotStartWith(Path.GetFullPath(Repo),
            "a file written inside the checkout would trip the invariant the consultant is judged by");
    }
}

/// <summary>Reading the vendor's own conversation id back off its stream — and refusing to believe a bad one.</summary>
public sealed class ConsultantHandleTests
{
    private static readonly CodexConsultant Codex = new(new CodexRuntime());

    private static ProcessResult Said(string stdout, string stderr = "", int exit = 0) =>
        new(exit, stdout, stderr, TimedOut: false);

    [Fact]
    public void TheThreadIdComesOffTheStartedEvent()
    {
        var stdout = "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-aaaa\"}\n{\"type\":\"item.completed\"}\n";

        Codex.ReadHandle(Said(stdout)).Should().Be("0198f2c1-aaaa");
    }

    [Fact]
    public void ATruncatedStreamStillYieldsTheHandle_WhichIsTheWholePointOnATimeout()
    {
        // The vendor accepted the turn and the kill came before the answer. That handle is the only
        // thing standing between the caller and paying for the same turn twice.
        var stdout = "{\"type\":\"thread.started\",\"thread_id\":\"0198f2c1-bbbb\"}\n{\"type\":\"item.st";

        Codex.ReadHandle(Said(stdout, exit: -1)).Should().Be("0198f2c1-bbbb");
    }

    [Fact]
    public void AHandleThatIsNotWellFormed_IsNotBelieved()
    {
        var stdout = "{\"type\":\"thread.started\",\"thread_id\":\"../../etc/passwd\"}\n";

        Codex.ReadHandle(Said(stdout)).Should().BeEmpty();
    }

    [Fact]
    public void ALogLineMentioningTheEvent_IsNotTheEvent()
    {
        Codex.ReadHandle(Said("waiting for thread.started...\n")).Should().BeEmpty();
    }

    [Fact]
    public void NothingSaid_IsNoHandle()
    {
        Codex.ReadHandle(Said(string.Empty)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("0198f2c1-aaaa-bbbb", true)]
    [InlineData("abc_DEF-123", true)]
    [InlineData("", false)]
    [InlineData("-leading-dash", false)]
    [InlineData("has space", false)]
    [InlineData("has\"quote", false)]
    [InlineData("../escape", false)]
    public void TheGuardIsTheSameShapeTheExtensionUses(string candidate, bool wellFormed)
    {
        ConsultantHandle.IsWellFormed(candidate).Should().Be(wellFormed);
    }

    [Fact]
    public void AVendorThatDroppedTheThread_IsRecognisedByItsOwnWords()
    {
        // A FAILED process saying it. The exit code is half the test, and this assertion used to
        // leave it at zero — which is how the defect below went unnoticed.
        Codex.DroppedTheConversation(Said(string.Empty, "Error: thread/resume: no rollout found for thread id 0198", exit: 1))
            .Should().BeTrue();
        Codex.DroppedTheConversation(Said(string.Empty, "some other failure", exit: 1)).Should().BeFalse();
    }

    /// <summary>
    /// The model QUOTING the phrase in its advice is not the vendor losing the conversation.
    /// </summary>
    /// <remarks>
    /// Stdout is where the answer arrives on both routes that ask this question, and the consult
    /// prompt carries the working tree — so a consultant asked about this repository, whose plan
    /// contains both literals, can produce them in a perfectly good turn. Without the exit code the
    /// turn is discarded and the handle reset, which costs the caller a turn and the conversation
    /// its memory. (CodeRabbit, on the pull request.)
    /// </remarks>
    [Fact]
    public void AGoodTurnQuotingTheVendorsOwnFailurePhrase_IsNotADroppedConversation()
    {
        var advice = "Your resume path is wrong: codex answers `thread/resume failed: no rollout found` "
            + "when the id is stale, so branch on the exit code rather than on that text.";

        Codex.DroppedTheConversation(Said(advice)).Should().BeFalse("the process succeeded — this is the ANSWER");
        Codex.DroppedTheConversation(Said(string.Empty, advice)).Should().BeFalse("still a zero exit");

        var agy = new AntigravityConsultant(new AntigravityRuntime());
        agy.DroppedTheConversation(Said("I could not find it: conversation not found, it said.")).Should().BeFalse();
        agy.DroppedTheConversation(Said(string.Empty, "conversation not found", exit: 1)).Should().BeTrue();
    }
}

/// <summary>Which configured vendor may consult, and the sentence for one that may not.</summary>
public sealed class ConsultantResolutionTests
{
    [Fact]
    public void ACodexRow_Consults()
    {
        ConsultantResolution.For(new VendorIdentity("codex", "codex", string.Empty)).Should().NotBeNull();
    }

    [Theory]
    [InlineData("remsoftdev-codex", "remote", "https://coai.remsoft.dev")]
    [InlineData("deepseek", "codex", "https://api.deepseek.com")]
    public void ARowThisBuildCannotConsultWith_IsRefusedByNameRatherThanSubstituted(string provider, string runtime, string baseUrl)
    {
        // A Team server row and a custom endpoint riding the codex CLI. `claude`, `antigravity` and
        // `local` were here until story 2 gave each of them an adapter; their coverage moved to
        // ConsultantResolutionAfterStoryTwoTests, which asserts they DO consult.
        var identity = new VendorIdentity(provider, runtime, baseUrl);

        ConsultantResolution.For(identity).Should().BeNull();
        ConsultantResolution.CannotConsult(identity).Should().Contain(provider).And.Contain("Consultant section");
    }
}
