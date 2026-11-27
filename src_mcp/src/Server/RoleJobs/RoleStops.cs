using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>The words of a stop — what <c>role_status</c> shows in <c>attempts[].stop</c> and what <c>job.json</c> records. Three of them fail over; the others end the job.</summary>
internal static class RoleStopWords
{
    /// <summary>The vendor refused the call for want of allowance: a spent quota, a throttle, a stated reset. Fails over.</summary>
    public const string Limit = "limit";

    /// <summary>The model said it would not do the work — the role's own output arm <c>status: "refused"</c>, never prose. Fails over.</summary>
    public const string Refusal = "refusal";

    /// <summary>Anything else that ended the launch without an answer: an exit, a timeout, an empty answer, a dropped conversation, a missing CLI, a lost sign-in. Fails over.</summary>
    public const string Crash = "crash";

    /// <summary>The caller withdrew the job. Ends it.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>The working tree changed while a read-only role ran — the one promise it runs under was broken. Ends the job: another model would run on a tree nobody has looked at.</summary>
    public const string TreeChanged = "tree-changed";

    /// <summary>
    /// The OS layer of a coder job refused a write the coder made, or refused to start it (epoch 3 E2): the job's one promise — the coder writes its worktree and nothing else — was tested and the layer answered. Ends the job, with the layer's sentence
    /// naming the path: another model would meet the same layer, and a coder that was stopped did not do its work.
    /// </summary>
    public const string CoderBoundary = "coder-boundary";

    /// <summary>The model answered, and the answer is not the role's output. Ends the job (a repair, where a role has one, happens before this).</summary>
    public const string OutputInvalid = "output-invalid";

    /// <summary>
    /// The exact text of the launch's claude pool key was found in what the launch printed, or in the files a coder changed (epoch 3 E2 s7): the key is dead (exposed), nothing is committed, and the job ends — another model would meet the same worktree and the same transcript.
    /// </summary>
    public const string KeyInOutput = "key-in-output";

    /// <summary>
    /// A coder job whose base declares an instruction step (<c>.coai/toolchain.yaml</c>, key <c>instructions</c>; SPEC C9) and that step could not run to a clean end — a non-zero exit, a run past its time, more than 64 KiB, bytes that are not UTF-8, a changed tree, a unit that
    /// would not start (epoch 3 E3 s7 t3). It ends the job BEFORE any model is launched: a coder's own stop, no failover, never green, nothing cut.
    /// </summary>
    public const string InstructionsFailed = "instructions-failed";
}

/// <summary>
/// One stop of one launch, read for the failover loop.
/// </summary>
/// <param name="Word">One of <see cref="RoleStopWords"/>.</param>
/// <param name="Reason">The sentence a person reads — the classifier's own, redacted and bounded.</param>
/// <param name="Kind">The classifier's word (<c>quota</c>, <c>exit</c>, <c>cli-not-found</c>…), or a role word, kept for the evidence.</param>
/// <param name="SkipsVendor">The vendor's OTHER models would stop the same way (a spent quota, a missing CLI, a lost sign-in): the loop skips them.</param>
/// <param name="Transient">A throttle that may clear while the loop waits — the one stop the LAST model waits out.</param>
internal sealed record RoleStop(string Word, string Reason, string Kind, bool SkipsVendor, bool Transient)
{
    /// <summary>True when what the launch printed must not be kept as evidence (agy's lost sign-in: its stderr holds a login address): the evidence is the reason alone.</summary>
    public bool NoEvidence { get; init; }

    /// <summary>Limit, refusal and crash go on to the next model; every other word ends the job.</summary>
    public bool FailsOver => Word is RoleStopWords.Limit or RoleStopWords.Refusal or RoleStopWords.Crash;
}

/// <summary>
/// Reads a finished launch that gave no answer as one of the role stops (spec A, "Stops"). The reading is <see cref="ConsultFailures.Classify"/> — the one reading of a consultation launch — plus exactly the
/// signals <c>SIGNALS.md</c> of the story records from real runs and that Classify files under <c>exit</c>: a claude envelope with <c>api_error_status</c> 429, and the three vendors' lost sign-in. Pure.
/// </summary>
/// <remarks>
/// <b>Nothing here matches a phrase that no file in <c>tests/fixtures/role-stops</c> shows.</b> A refusal is not read here at all: no vendor has a structured refusal field on file, so the only refusal is the role's
/// own output arm, which the loop reads from the answer.
/// </remarks>
internal static class RoleStops
{
    /// <param name="vendor">The CLI of the role route: <c>claude</c>, <c>codex</c> or <c>agy</c>.</param>
    /// <param name="runtime">The runtime's name for the install cure (<c>claude</c>, <c>codex</c>, <c>antigravity</c>).</param>
    public static RoleStop Of(IConsultantRuntime consultant, ReviewerLaunch launch, string vendor, string runtime, ConsultFailure killedAs)
    {
        var failure = ConsultFailures.Classify(consultant, launch, runtime, killedAs);
        var said = Redaction.SafeText(failure.What(vendor), Redaction.TitleLimit);
        var process = launch.Process;

        if (process is not null && SpendLimitEnvelope(process))
        {
            return new RoleStop(RoleStopWords.Limit, said, failure.Kind, SkipsVendor: true, Transient: false);
        }

        if (process is not null && SignInLost(vendor, process))
        {
            // The stderr of a lost agy sign-in holds a login address (a challenge of that one start): it is not copied into the sentence, the evidence or a notice.
            return vendor == "agy"
                ? new RoleStop(RoleStopWords.Crash, "the sign-in is lost: agy asked for a new sign-in, and its login address is not copied here", failure.Kind, SkipsVendor: true, Transient: false) { NoEvidence = true }
                : new RoleStop(RoleStopWords.Crash, "the sign-in is lost: " + said + SignInCure(vendor), failure.Kind, SkipsVendor: true, Transient: false);
        }

        return Stopped(failure, said);
    }

    /// <summary>A stop for a failure the classifier already named — also what a refused preparation (a CLI that cannot be started) becomes.</summary>
    public static RoleStop Of(ConsultFailure failure, string vendor) => Stopped(failure, Redaction.SafeText(failure.What(vendor), Redaction.TitleLimit));

    private static RoleStop Stopped(ConsultFailure failure, string said) => failure switch
    {
        ConsultFailure.Quota => new RoleStop(RoleStopWords.Limit, said, failure.Kind, SkipsVendor: true, Transient: false),
        ConsultFailure.RateLimited => new RoleStop(RoleStopWords.Limit, said, failure.Kind, SkipsVendor: false, Transient: true),
        ConsultFailure.Cancelled => new RoleStop(RoleStopWords.Cancelled, said, failure.Kind, SkipsVendor: false, Transient: false),
        ConsultFailure.TreeChanged => new RoleStop(RoleStopWords.TreeChanged, said, failure.Kind, SkipsVendor: false, Transient: false),
        ConsultFailure.VendorRefused or ConsultFailure.CliNotFound or ConsultFailure.LaunchRefused => new RoleStop(RoleStopWords.Crash, said, failure.Kind, SkipsVendor: true, Transient: false),
        _ => new RoleStop(RoleStopWords.Crash, said, failure.Kind, SkipsVendor: false, Transient: false),
    };

    /// <summary>The claude envelope of a failed call whose <c>api_error_status</c> is the integer 429 (recorded: "You've hit your monthly spend limit"). A status that is a string, or any other number, is not read.</summary>
    internal static bool SpendLimitEnvelope(ProcessResult process)
    {
        var root = Parsed(process.StdOut);

        return process.ExitCode != 0
            && root is { ValueKind: JsonValueKind.Object } envelope
            && IsTrue(envelope, "is_error")
            && envelope.TryGetProperty("api_error_status", out var status)
            && status.ValueKind == JsonValueKind.Number
            && status.TryGetInt32(out var code)
            && code == 429;
    }

    /// <summary>The three recorded sign-in failures, one reading per vendor (see <c>SIGNALS.md</c> section 2). A non-zero exit only: stdout of a good run is the model speaking.</summary>
    internal static bool SignInLost(string vendor, ProcessResult process) => process.ExitCode != 0 && vendor switch
    {
        "claude" => ClaudeSignInLost(process.StdOut),
        "codex" => CodexSignInLost(process),
        "agy" => AgySignInLost(process),
        _ => false,
    };

    private static bool ClaudeSignInLost(string stdout) =>
        Parsed(stdout) is { ValueKind: JsonValueKind.Object } envelope
        && IsTrue(envelope, "is_error")
        && Text(envelope, "result").StartsWith("Not logged in", StringComparison.Ordinal);

    /// <summary>
    /// A codex turn that lost its sign-in (the judgement's section 13.6; epoch 3 E3 s5 t4), by ANY of: a stderr line with <c>Failed to refresh token</c> (the CLI's own line, written before any answer of the vendor, in every network state); a <c>turn.failed</c> message with <c>unauthorized (401)</c>
    /// (the vendor refused the token); the sentence <c>Your access token could not be refreshed</c> (an override that ANSWERED 400); or the recorded <c>401 Unauthorized</c> of a file with no sign-in. <c>workspace routing discovery failed</c> alone is none of them: with the host network it says the network or the vendor could not be reached, and goes the road of a crash.
    /// </summary>
    private static bool CodexSignInLost(ProcessResult process) =>
        process.StdErr.Contains("Failed to refresh token", StringComparison.Ordinal)
        || process.StdErr.Contains("Your access token could not be refreshed", StringComparison.Ordinal)
        || Lines(process.StdOut).Any(line => line.TryGetProperty("type", out var type) && type.GetString() is "error" or "turn.failed" && CodexMessage(line) is var message
            && (message.Contains("401 Unauthorized", StringComparison.Ordinal) || message.Contains("unauthorized (401)", StringComparison.Ordinal) || message.Contains("Your access token could not be refreshed", StringComparison.Ordinal)));

    private static string CodexMessage(JsonElement line) =>
        Text(line, "message") + Text(line.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? error : default, "message");

    /// <summary>What the person does about a lost sign-in, for a vendor that has a cure the coder can name: codex refreshes its own file when it is run once by the person (a coder's job holds a copy that is never refreshed).</summary>
    private static string SignInCure(string vendor) =>
        vendor == "codex" ? "; run codex once yourself (its own CLI refreshes your own sign-in file), then start the job again — the other models of codex are skipped" : string.Empty;

    /// <summary>The recorded texts of a lost agy sign-in (fixtures <c>agy-no-sign-in</c> and <c>agy-copy-expired</c>, and <c>agy models</c>): <c>Authentication required</c>, <c>authentication timed out</c>, <c>Please sign in</c> on stderr, or a result whose error starts <c>authentication failed</c>. Whatever the case of the words.</summary>
    private static bool AgySignInLost(ProcessResult process) =>
        process.StdErr.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
        || process.StdErr.Contains("authentication timed out", StringComparison.OrdinalIgnoreCase)
        || process.StdErr.Contains("please sign in", StringComparison.OrdinalIgnoreCase)
        || Lines(process.StdOut).Any(line =>
            Text(line.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object ? result : default, "error").StartsWith("authentication failed", StringComparison.Ordinal));

    private static IEnumerable<JsonElement> Lines(string stdout) =>
        stdout.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith('{'))
            .Select(Parsed)
            .Where(parsed => parsed is { ValueKind: JsonValueKind.Object })
            .Select(parsed => parsed!.Value);

    private static JsonElement? Parsed(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text.Trim());

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
