using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The REAL <see cref="ReviewLauncher"/>, against a process that behaved.
/// </summary>
/// <remarks>
/// <para>Every other test of this path drives <see cref="IReviewLauncher"/> through a fake that
/// hands the runner a <c>ReviewerOutcome.Ok</c> — a shape the real launcher has never once
/// produced. That is why 171 green tests sat on top of a Team server on which a review could not
/// succeed: `usage.jsonl` held two lines after weeks, both `NotStarted`, and the CLI transcript
/// beside them showed the model answering perfectly each time.</para>
/// <para>So this test starts one layer lower — at the launcher itself, with a fake PROCESS rather
/// than a fake launcher — because the defect lives in the translation between what the executor
/// says about a good run and what this server makes of it.</para>
/// </remarks>
public sealed class ReviewLauncherTests
{
    /// <summary>What `claude -p --output-format json` prints when it worked.</summary>
    private const string Envelope = """
        {"type":"result","subtype":"success","is_error":false,"result":"{\"findings\":[]}"}
        """;

    [Fact]
    public async Task AProcessThatExitedZeroWithAnAnswer_IsNotReportedAsNotStarted()
    {
        var attempt = await Launch(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Answered>(
            "the process ran, exited zero and answered — the executor's null verdict MEANS that");
    }

    [Fact]
    public async Task AProcessThatExitedZeroWithAnAnswer_CarriesTheVendorsRawText()
    {
        var attempt = await Launch(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Answered>()
            .Which.Raw.Should().Be("""{"findings":[]}""");
    }

    /// <summary>
    /// A run that said nothing is still a failure — but not the same one, and never a silent
    /// success. The distinction is what the person reading the round needs.
    /// </summary>
    [Fact]
    public async Task AProcessThatExitedZeroSayingNothing_FailsWithoutClaimingItNeverStarted()
    {
        var attempt = await Launch(new ProcessResult(0, string.Empty, string.Empty, TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Failed>()
            .Which.Outcome.Should().BeOfType<ReviewerOutcome.Unparseable>(
                "it started — it just produced no answer, and saying otherwise sends the reader to the wrong place");
    }

    [Fact]
    public async Task ANonZeroExit_IsStillTheExecutorsOwnVerdict()
    {
        var attempt = await Launch(new ProcessResult(3, string.Empty, "boom", TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Failed>()
            .Which.Outcome.Should().Match<ReviewerOutcome.NonZeroExit>(e => e.ExitCode == 3 && e.StdErrTail == "boom");
    }

    /// <summary>
    /// The three remaining terminal outcomes, unchanged.
    /// </summary>
    /// <remarks>
    /// Asked for on this change's own plan round by codex and gemini, and fairly: <c>Read</c>
    /// REPLACED the null-coalescing line that used to map every one of these, so "unchanged" was a
    /// claim with nothing behind it. A timeout silently recorded as an answer is the expensive
    /// direction of that mistake.
    /// </remarks>
    [Fact]
    public async Task ATimeout_IsStillATimeout()
    {
        var attempt = await Launch(new ProcessResult(0, string.Empty, string.Empty, TimedOut: true));

        attempt.Should().BeOfType<ReviewAttempt.Failed>()
            .Which.Outcome.Should().BeOfType<ReviewerOutcome.TimedOut>();
    }

    [Fact]
    public async Task ARefusalTheVendorNamed_IsStillRateLimited()
    {
        var attempt = await Launch(
            new ProcessResult(1, string.Empty, "You've hit your usage limit · resets 9:30pm", TimedOut: false));

        attempt.Should().BeOfType<ReviewAttempt.Failed>()
            .Which.Outcome.Should().BeOfType<ReviewerOutcome.RateLimited>();
    }

    [Fact]
    public async Task AVendorWithNoAdapter_StillSaysSoBeforeAnythingIsLaunched()
    {
        var attempt = await Launch(
            new ProcessResult(0, Envelope, string.Empty, TimedOut: false),
            runtime: "no-such-runtime",
            vendorId: "no-such-vendor");

        attempt.Should().BeOfType<ReviewAttempt.Failed>()
            .Which.Outcome.Should().BeOfType<ReviewerOutcome.NotStarted>(
                "nothing ran, so this is the one outcome that genuinely means it never started");
    }

    /// <summary>
    /// The file the vendor is POINTED at has to be there when the vendor looks.
    /// </summary>
    /// <remarks>
    /// <para>`ReviewLauncher` hands `runtime.Build` a schema PATH and never wrote the file, so every
    /// adapter that passes it on to its CLI failed the instant the CLI opened it. Measured against
    /// the live server on 2026-09-07, once each defect above was fixed and the two other vendors
    /// could finally be tried:</para>
    /// <code>
    /// codex        Failed to read output schema file /tmp/coai-server-job-6KLayP/schema.json: No such file or directory
    /// antigravity  Error: invalid --json-schema: failed to read schema file "/tmp/coai-server-job-9…"
    /// </code>
    /// <para>Claude's adapter takes no schema file — it puts the shape in the prompt — which is why
    /// claude alone appeared to work and why this hid behind the first two defects for so long.</para>
    /// </remarks>
    [Fact]
    public async Task TheSchemaFileTheVendorIsPointedAt_ExistsWhileTheVendorRuns()
    {
        var watching = new Watching(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        await Run(watching, runtime: "antigravity", vendorId: "antigravity", model: "gemini-3.8-flash-low");

        watching.SchemaFound.Should().BeTrue(
            "the adapter passes this path to its CLI with --json-schema, and the CLI opens it");
        watching.SchemaText.Should().Be(FindingSchema.Json, "a schema of other bytes shapes nothing");
    }

    /// <summary>
    /// The server sets BOTH halves of a confined launch — and this is the test that goes red when a
    /// later edit sets one of them.
    /// </summary>
    /// <remarks>
    /// <para>Story 2.2 of `PLAN_a_reviewer_on_the_team_server_is_confined_to_its_prompt.md`. The two
    /// halves live on two types that never meet: <c>ReviewerSettings.Confined</c> is read by the
    /// adapter while it composes argv, before a request exists, and
    /// <c>ProcessRequest.InheritsEnvironment</c> is read by the launcher after it does. Nothing made
    /// them agree, and epic 1's code round named what that costs (codex, Major): a server edit that
    /// set one and missed the other produces a reviewer with an isolated environment and a shell,
    /// or the reverse, and both look like a confined launch from every angle but the one that
    /// matters.</para>
    /// <para>What a test CAN observe is the pair, on the one request the launcher hands over:
    /// whichever half a later edit drops, this goes red. That the launcher has no second road to a
    /// request — that both are derived from one <c>Confinement</c> value in one step — is a
    /// property of its shape rather than of this assertion, and <c>ConfinementTests</c> holds the
    /// derivation itself.</para>
    /// <para>The argv is what is SENT. Whether the installed CLI honours a name in that list is the
    /// CLI's decision, observable only on the box that runs it — which is what `POST_DEPLOY.md`
    /// item 12 is for.</para>
    /// </remarks>
    [Fact]
    public async Task TheServerLaunchesEveryReviewerConfined()
    {
        var watching = new Watching(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        await Run(watching);

        watching.Request.InheritsEnvironment.Should().BeFalse(
            "the server's own configuration must not be in the environment of a process running somebody else's prompt");
        Denied(watching.Request.Arguments).Should().Contain(ReachesPastThePrompt,
            "on this box every one of these reaches another slot's sign-in, and the finding text goes back to the prompt's author verbatim");
    }

    /// <summary>
    /// Confinement takes nothing away from account isolation: the slot's own variables still arrive.
    /// </summary>
    /// <remarks>
    /// The variables are built by the function the runner itself calls rather than retyped here,
    /// because the guarantee is about whatever <c>SlotEnvironment.For</c> decides a slot needs —
    /// <c>HOME</c>, the XDG redirects, <c>CLAUDE_CONFIG_DIR</c>, the token — and a hand-written list
    /// would prove only that the names somebody remembered survived.
    /// </remarks>
    [Fact]
    public async Task AConfinedReviewerStillRunsAsItsOwnAccount()
    {
        var slot = SlotEnvironment.For(
            "claude", Path.Combine(Path.GetTempPath(), "coai-slot-a"), token: "not-a-real-token");
        var watching = new Watching(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        await Run(watching, environment: slot);

        slot.Should().NotBeEmpty("a loop over an empty dictionary asserts nothing");
        foreach (var (name, value) in slot)
        {
            watching.Request.Environment.Should().ContainKey(name,
                "the request's own variables are applied last, so the allowlist cannot take an account away")
                .WhoseValue.Should().Be(value);
        }
    }

    /// <summary>
    /// A job's temporary directory is its own working directory — not the box's shared one.
    /// </summary>
    /// <remarks>
    /// <para>The allowlist passes <c>TMPDIR</c>, <c>TMP</c> and <c>TEMP</c> through, so on a shared
    /// box running as root every reviewer would see the same <c>/tmp</c>, where other jobs' files and
    /// the host's sockets are (gemini, Major, epic 1's code round). The three names are the
    /// platforms' own spellings — POSIX reads the first, Windows the other two — not a list this
    /// server invents, which is why they are named here rather than read off the code.</para>
    /// <para>Checked from INSIDE the launch, like the schema: the directory is deleted in a finally
    /// the moment the launch returns, and a temporary directory that does not exist while the vendor
    /// runs is a CLI that fails on its first temp file.</para>
    /// </remarks>
    [Fact]
    public async Task AJobsTemporaryDirectoryIsItsOwnWorkingDirectory()
    {
        var watching = new Watching(new ProcessResult(0, Envelope, string.Empty, TimedOut: false));

        await Run(watching);

        var own = watching.Request.WorkingDirectory;
        foreach (var name in new[] { "TMPDIR", "TMP", "TEMP" })
        {
            watching.Request.Environment.Should().ContainKey(name,
                "the request's own variables win over the allowlist, and this one must")
                .WhoseValue.Should().Be(own, "anything else is a directory other jobs can read");
        }

        watching.TemporaryDirectoryExisted.Should().BeTrue(
            "the vendor writes its first temp file before it writes its answer");
    }

    /// <summary>The tools that reach past the prompt: the filesystem, a shell, the web, a sub-agent.</summary>
    /// <remarks>
    /// The same eight `ClaudeRuntimeTests` holds, on purpose: that list is the STORY's contract with
    /// the box, not a list that grows with the adapter. A ninth tool the adapter denies one day is
    /// the adapter's test to notice; this one asks whether the server asked for the confined set.
    /// </remarks>
    private static readonly string[] ReachesPastThePrompt =
        ["Bash", "Read", "Glob", "Grep", "WebFetch", "WebSearch", "Task", "Agent"];

    private const string DisallowedToolsFlag = "--disallowedTools";

    /// <summary>The names that follow the one <c>--disallowedTools</c>, up to the next flag.</summary>
    private static IReadOnlyList<string> Denied(IReadOnlyList<string> args)
    {
        args.Should().ContainSingle(a => a == DisallowedToolsFlag,
            "a variadic option given twice is whichever the CLI reads last, and that is not a list anybody wrote");

        return args
            .SkipWhile(a => a != DisallowedToolsFlag)
            .Skip(1)
            .TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal))
            .ToList();
    }

    private static async Task<ReviewAttempt> Launch(
        ProcessResult result, string runtime = "claude", string vendorId = "claude") =>
        await Run(new Fixed(result), runtime, vendorId);

    /// <param name="model">
    /// A model this VENDOR actually serves. It was hard-coded to `haiku` for every runtime, so the
    /// antigravity case paired a Claude model with the agy CLI — which
    /// `.claude/rules/common/vendor-routing.md` forbids in as many words, and which antigravity
    /// itself caught reviewing this change.
    /// </param>
    /// <param name="environment">
    /// What the runner hands over for the account — empty unless a test is about the account.
    /// </param>
    private static async Task<ReviewAttempt> Run(
        IProcessLauncher launcher,
        string runtime = "claude",
        string vendorId = "claude",
        string model = "haiku",
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        var slot = new AccountSlot(
            vendorId, "a", Path.GetTempPath(), DateTimeOffset.UtcNow, null, false, string.Empty, 0);
        var job = new JobRecord(
            JobId.New(), "dev@example.com", vendorId, model, "PlanCritique", "review this",
            JobStatus.Running, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10),
            TimeSpan.FromSeconds(60));

        return await new ReviewLauncher(launcher).RunAsync(
            new VendorConfig(vendorId, runtime, [model], ["a"]),
            slot,
            job,
            environment ?? new Dictionary<string, string?>(),
            CancellationToken.None);
    }

    private sealed class Fixed(ProcessResult result) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(result);
    }

    /// <summary>
    /// Looks at the work directory from INSIDE the launch — the only moment the answer is knowable,
    /// because the runner deletes that directory in a finally as soon as the launch returns.
    /// </summary>
    private sealed class Watching(ProcessResult result) : IProcessLauncher
    {
        public bool SchemaFound { get; private set; }

        public string SchemaText { get; private set; } = string.Empty;

        /// <summary>The request exactly as the launcher handed it over — the confinement is on it.</summary>
        /// <remarks>
        /// Until the launch it is a placeholder shaped so that nothing about it passes a confinement
        /// assertion: it inherits everything, denies nothing and carries no environment. A test that
        /// reads it before any launch therefore fails on the guarantee it names, never on a null.
        /// </remarks>
        public ProcessRequest Request { get; private set; } = new(string.Empty, [], string.Empty);

        /// <summary>Whether the directory the child was told is its temporary one existed at launch.</summary>
        public bool TemporaryDirectoryExisted { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Request = request;
            var schema = Path.Combine(request.WorkingDirectory, SchemaFile.Name);
            SchemaFound = File.Exists(schema);
            SchemaText = SchemaFound ? File.ReadAllText(schema) : string.Empty;
            TemporaryDirectoryExisted =
                request.Environment.TryGetValue("TMPDIR", out var temporary)
                && temporary is { Length: > 0 }
                && Directory.Exists(temporary);

            return Task.FromResult(result);
        }
    }
}
