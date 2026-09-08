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

    private static async Task<ReviewAttempt> Launch(
        ProcessResult result, string runtime = "claude", string vendorId = "claude") =>
        await Run(new Fixed(result), runtime, vendorId);

    /// <param name="model">
    /// A model this VENDOR actually serves. It was hard-coded to `haiku` for every runtime, so the
    /// antigravity case paired a Claude model with the agy CLI — which
    /// `.claude/rules/common/vendor-routing.md` forbids in as many words, and which antigravity
    /// itself caught reviewing this change.
    /// </param>
    private static async Task<ReviewAttempt> Run(
        IProcessLauncher launcher,
        string runtime = "claude",
        string vendorId = "claude",
        string model = "haiku")
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
            new Dictionary<string, string?>(),
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

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var schema = Path.Combine(request.WorkingDirectory, SchemaFile.Name);
            SchemaFound = File.Exists(schema);
            SchemaText = SchemaFound ? File.ReadAllText(schema) : string.Empty;

            return Task.FromResult(result);
        }
    }
}
