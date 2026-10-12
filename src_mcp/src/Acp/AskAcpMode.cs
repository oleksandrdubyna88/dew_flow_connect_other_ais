using System.Text.Json.Nodes;
using CoaiMcp.Runners.Acp;

namespace CoaiMcp.Acp;

/// <summary>
/// <c>coai-mcp --ask-acp</c> — one turn of an ACP agent (Kimi Code over <c>kimi acp</c>), the prompt on stdin, the
/// answer and the trace as one JSON object on stdout.
/// </summary>
/// <remarks>
/// <para>The third self-invocation shim, after <c>--ask-local</c> and <c>--ask-api</c>, and for the same reason: the
/// future <c>KimiRuntime</c> returns a <c>ProcessRequest</c> for THIS binary, so the executor launches, times out and
/// kills it like any CLI, and the round's own code never learns that the vendor spoke ACP. What is different is that
/// this process is a CLIENT with a child of its own — the agent — and owns the three blocks of D15
/// (<c>PLAN_vibe_and_kimi_runtimes.md</c>): every permission refused, reads served only through the boundary, nothing
/// else offered.</para>
/// <para><b>Stdin EOF is "the prompt is complete", never "cancel".</b> <c>ProcessLauncher</c> writes the prompt and
/// closes stdin (<c>WriteStdInAsync</c>), so the prompt is read whole here before the agent is started; the agent's
/// own stdin stays open for the replies for as long as the turn runs.</para>
/// <para><b>Exit codes are the contract.</b> 0 answered; 65 a request this shim will not make — no profile, an
/// unknown one, no model, no folder, an empty prompt — and <b>never 64</b>, which means "this binary has never heard
/// of that mode" (<c>.agents/PROJECT.md</c>); 69 ended before an answer — the agent could not be started, the
/// deadline struck, or the caller cancelled — read by the parent as "usage not captured"; 70 the agent refused or
/// failed in its own words; 75 a known SPENT sentence (an allowance, D14); 76 (EX_PROTOCOL) the stream broke — the
/// agent closed its output early, stopped listening, or wrote something that is not an answer; 77 a known REFUSED
/// sentence — not signed in, or a plan without the model (the 401 of RESULTS §2).</para>
/// <para><b>The stdout shape</b> (<see cref="Report"/>) is printed on EVERY exit, so a failed turn's trace and usage
/// cross the process boundary as an answered one's do: <c>answer</c>, <c>end</c>, <c>stopReason</c>, <c>reason</c>,
/// <c>sessionId</c>, <c>failure</c> (a known failure's name, when one matched), <c>usage</c> in the api shim's
/// field names — <c>tokensIn</c>/<c>tokensOut</c> are 0 with <c>notCaptured: true</c>, because ACP reports context
/// tokens with no split (D15); <c>contextUsed</c>/<c>contextSize</c> carry the last <c>usage_update</c> when one
/// came — and the trace: <c>refusals</c> (tool, kind, option), <c>toolCalls</c> (id, title, kind, status) and
/// <c>reads</c> (path, served, reason).</para>
/// </remarks>
internal static class AskAcpMode
{
    internal const int Ok = 0;
    internal const int BadRequest = 65; // EX_DATAERR — a known mode refusing its arguments; never 64
    internal const int Unavailable = 69; // EX_UNAVAILABLE — ended before an answer: usage not captured
    internal const int Failed = 70; // EX_SOFTWARE — the agent's own failure
    internal const int Spent = 75; // EX_TEMPFAIL — a known spent-allowance sentence
    internal const int Protocol = 76; // EX_PROTOCOL — the stream itself failed
    internal const int Refused = 77; // EX_NOPERM — a known sign-in or plan refusal

    /// <summary>Without <c>--deadline-seconds</c>: the same ten minutes the api shim gives a turn.</summary>
    internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(10);

    /// <summary>The most <c>--deadline-seconds</c> may ask for — a day, the client's own ceiling (<see cref="AcpClient.MaxDeadline"/>).</summary>
    internal static readonly int MaxDeadlineSeconds = (int)AcpClient.MaxDeadline.TotalSeconds;

    private const string Mode = "--ask-acp";

    /// <param name="args">The argv from the mode on: <c>--profile</c>, <c>--model</c>, <c>--cwd</c>, <c>--deadline-seconds</c>, and an optional <c>--executable</c> (a row's configured path).</param>
    /// <param name="stdin">The prompt, read to its end.</param>
    /// <param name="note">Stderr: the refusal or the reason, as sentences.</param>
    /// <param name="output">Stdout: the one JSON object.</param>
    /// <param name="profiles">The profiles this run knows — the build's, unless a test hands in a fake agent's.</param>
    internal static async Task<int> RunAsync(
        string[] args,
        TextReader stdin,
        Action<string> note,
        TextWriter output,
        IReadOnlyList<IAcpProfile>? profiles = null,
        CancellationToken ct = default)
    {
        var flags = Program.Flags(args);
        var known = profiles ?? AcpProfiles.All;
        // The one "not found" this mode has, and it is answered before anything else is looked at.
        if (AcpProfiles.Find(known, flags.GetValueOrDefault("--profile", string.Empty)) is not { } profile)
        {
            note($"{Mode}: no profile '{flags.GetValueOrDefault("--profile", string.Empty)}' — this build knows {string.Join(", ", AcpProfiles.Names(known))}");

            return BadRequest;
        }

        var cwd = flags.GetValueOrDefault("--cwd", string.Empty);
        var model = flags.GetValueOrDefault("--model", string.Empty);
        if (Refusal(flags, cwd, model) is { Length: > 0 } refused)
        {
            note(refused);

            return BadRequest;
        }

        var prompt = await stdin.ReadToEndAsync(ct);
        if (prompt.Trim().Length == 0)
        {
            note($"{Mode}: the prompt on stdin is empty — nothing to ask");

            return BadRequest;
        }

        var ask = new AcpAsk(Executable(flags, profile), Path.GetFullPath(cwd), model, prompt, Deadline(flags));
        var outcome = await new AcpClient(profile, new FolderReadBoundary(ask.Cwd, profile.ExcludedNames)).AskAsync(ask, ct);
        var diagnosis = AcpDiagnosis.Of(outcome.Reason + "\n" + outcome.StdErr, profile.KnownFailures);
        await output.WriteLineAsync(Report(outcome, diagnosis));

        return Exit(outcome, diagnosis, note);
    }

    /// <summary>Why this shim will not even start the agent — or empty when it will. Every one of these is 65.</summary>
    internal static string Refusal(IReadOnlyDictionary<string, string> flags, string cwd, string model)
    {
        if (cwd.Length == 0 || !Directory.Exists(cwd))
        {
            return $"{Mode} needs --cwd <an existing folder the agent works in>" + (cwd.Length == 0 ? string.Empty : $" — '{cwd}' is not one");
        }

        if (model.Length == 0)
        {
            return $"{Mode} needs --model <id> — the model is set on the session before the prompt, never left to the agent's default";
        }

        // Given, it must be a number of seconds the clock can hold: a value past the ceiling used to throw after the
        // agent had started and leave it running (finding 4 of the security review).
        var given = flags.GetValueOrDefault("--deadline-seconds", string.Empty);

        return given.Length > 0 && !(int.TryParse(given, out var seconds) && seconds >= 1 && seconds <= MaxDeadlineSeconds)
            ? $"{Mode}: --deadline-seconds must be a whole number from 1 to {MaxDeadlineSeconds} — '{given}' is not"
            : string.Empty;
    }

    /// <summary>A configured path is the whole answer; otherwise the profile's own.</summary>
    private static string Executable(IReadOnlyDictionary<string, string> flags, IAcpProfile profile) =>
        flags.GetValueOrDefault("--executable", string.Empty) is { Length: > 0 } configured ? configured : profile.DefaultExecutable;

    /// <summary>The given seconds — already checked by <see cref="Refusal"/> — or the default.</summary>
    private static TimeSpan Deadline(IReadOnlyDictionary<string, string> flags) =>
        int.TryParse(flags.GetValueOrDefault("--deadline-seconds", string.Empty), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : DefaultDeadline;

    /// <summary>The exit, and the sentence that goes with it: a known failure's name and cure first, else the end's own reason.</summary>
    private static int Exit(AcpOutcome outcome, AcpDiagnosis diagnosis, Action<string> note)
    {
        if (outcome.End == AcpEnd.Answered)
        {
            return Ok;
        }

        if (diagnosis.Known && outcome.End is not (AcpEnd.Deadline or AcpEnd.Cancelled))
        {
            note($"{diagnosis.Name}: {outcome.Reason} — {diagnosis.Cure}");

            return diagnosis.Kind == AcpFailureKind.Spent ? Spent : Refused;
        }

        note(outcome.Reason);

        return outcome.End switch
        {
            AcpEnd.AgentFailed => Failed,
            AcpEnd.Protocol => Protocol,
            _ => Unavailable,
        };
    }

    /// <summary>The one JSON object on stdout — see the type's remarks for the shape.</summary>
    internal static string Report(AcpOutcome outcome, AcpDiagnosis diagnosis)
    {
        var report = new JsonObject
        {
            ["answer"] = outcome.Answer,
            ["end"] = EndWord(outcome.End),
            ["stopReason"] = outcome.StopReason,
            ["reason"] = outcome.Reason,
            ["sessionId"] = outcome.SessionId,
            ["usage"] = UsageObject(outcome.Usage),
            ["refusals"] = new JsonArray([.. outcome.Refusals.Select(r => (JsonNode?)new JsonObject { ["tool"] = r.Tool, ["kind"] = r.Kind, ["option"] = r.Option })]),
            ["toolCalls"] = new JsonArray([.. outcome.ToolCalls.Select(t => (JsonNode?)new JsonObject { ["id"] = t.Id, ["title"] = t.Title, ["kind"] = t.Kind, ["status"] = t.Status })]),
            ["reads"] = new JsonArray([.. outcome.Reads.Select(r => (JsonNode?)new JsonObject { ["path"] = r.Path, ["served"] = r.Served, ["reason"] = r.Reason })]),
            // The three lists are capped (AcpSession.MaxTraceEntries); what they could not hold is counted here.
            ["droppedTraceEntries"] = outcome.DroppedTraceEntries,
        };
        if (diagnosis.Known)
        {
            report["failure"] = diagnosis.Name;
        }

        return report.ToJsonString();
    }

    /// <summary>The api shim's usage field names, so one reader serves both: unknown tokens, plus the context numbers when reported.</summary>
    private static JsonObject UsageObject(AcpUsage usage)
    {
        var tokens = new JsonObject { ["tokensIn"] = 0, ["tokensOut"] = 0, ["tokensCached"] = 0, ["tokensReasoning"] = 0, ["notCaptured"] = true };
        if (usage.Captured)
        {
            tokens["contextUsed"] = usage.ContextUsed;
            tokens["contextSize"] = usage.ContextSize;
        }

        return tokens;
    }

    internal static string EndWord(AcpEnd end) => end switch
    {
        AcpEnd.Answered => "answered",
        AcpEnd.NotStarted => "not_started",
        AcpEnd.AgentFailed => "agent_failed",
        AcpEnd.Deadline => "deadline",
        AcpEnd.Protocol => "protocol",
        _ => "cancelled",
    };
}
