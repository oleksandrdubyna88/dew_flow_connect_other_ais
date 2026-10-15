using System.Diagnostics;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;

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
/// unknown one, no model, no folder, an empty prompt, a profile that needs a home without <c>--home</c>/<c>--persons-bin</c>/<c>--locks</c>, an agent
/// request that is not coai's own environment, a hold that cannot be taken, a model the session does not offer — and <b>never 64</b>, which means "this binary has never heard
/// of that mode" (<c>.agents/PROJECT.md</c>); 69 ended before an answer — the agent could not be started, another
/// Kimi still holds the sign-in past the deadline, the deadline struck, or the caller cancelled — read by the parent as "usage not captured"; 70 the agent refused or
/// failed in its own words; 75 a known SPENT sentence (an allowance, D14); 76 (EX_PROTOCOL) the stream broke — the
/// agent closed its output early, stopped listening, or wrote something that is not an answer; 77 a known REFUSED
/// sentence — not signed in, or a plan without the model (the 401 of RESULTS §2).</para>
/// <para><b>A profile that needs a home (Kimi) runs only on one</b> (D19, E4.3): <c>--home</c> is the coai-owned ROOT holding <c>home/</c> and <c>os/</c>,
/// <c>--persons-bin</c> the person's tool folder, <c>--locks</c> the lock folder — all three arrive on the argv because inside this process
/// <c>USERPROFILE</c> is the owned <c>os/</c>, and the person's paths are never asked of an environment. The agent is started in an environment
/// built from scratch (<see cref="IAcpProfile.AgentRequest"/>), never by inheritance. <b>The tree hold GATES</b> (decision 10 of E4.2): it is taken
/// immediately before <see cref="AcpClient.AskAsync"/> — nothing awaited between — and disposed once that has returned; another tree still holding
/// the sign-in past the deadline is exit 69 with the reason in a sentence, a hold that cannot be taken exit 65. There is no warning-only fallback.</para>
/// <para><b>The stdout shape</b> (<see cref="Report"/>) is printed on EVERY exit, so a failed turn's trace and usage
/// cross the process boundary as an answered one's do: <c>answer</c>, <c>end</c>, <c>stopReason</c>, <c>reason</c>,
/// <c>sessionId</c>, <c>failure</c> (a known failure's name, when one matched), <c>usage</c> in the api shim's
/// field names — <c>tokensIn</c>/<c>tokensOut</c> are the session's totals from <c>/usage</c>, asked after an answered turn (D15, M-K2), or 0 with
/// <c>notCaptured: true</c> when it could not be had — never a guessed number; <c>contextUsed</c>/<c>contextSize</c> carry the last <c>usage_update</c> when one
/// came — and the trace: <c>refusals</c> (tool, kind, option), <c>toolCalls</c> (id, title, kind, status) and
/// <c>reads</c> (path, served, reason).</para>
/// </remarks>
internal static class AskAcpMode
{
    internal const int Ok = 0;
    internal const int BadRequest = 65; // EX_DATAERR — a known mode refusing its arguments; never 64
    internal const int Unavailable = 69; // EX_UNAVAILABLE — ended before an answer: usage not captured
    internal const int Failed = 70; // EX_SOFTWARE — the agent's own failure
    internal const int Spent = AcpWords.SpentExit; // EX_TEMPFAIL (75) — a known spent-allowance sentence; declared where KimiRuntime reads it
    internal const int Protocol = 76; // EX_PROTOCOL — the stream itself failed
    internal const int Refused = 77; // EX_NOPERM — a known sign-in or plan refusal

    /// <summary>Without <c>--deadline-seconds</c>: the same ten minutes the api shim gives a turn.</summary>
    internal static readonly TimeSpan DefaultDeadline = TimeSpan.FromMinutes(10);

    /// <summary>The most <c>--deadline-seconds</c> may ask for — a day, the client's own ceiling (<see cref="AcpClient.MaxDeadline"/>).</summary>
    internal static readonly int MaxDeadlineSeconds = (int)AcpClient.MaxDeadline.TotalSeconds;

    /// <summary>The least a turn may be left after a late hold: the client refuses a spent deadline outright, and a turn this short answers nothing.</summary>
    private static readonly TimeSpan MinimumTurn = TimeSpan.FromSeconds(1);

    private const string Mode = "--ask-acp";

    /// <summary>The lock folder the tree hold lives in — <see cref="KimiCredentialsLock.Root"/> of the runtime that started this shim.</summary>
    private const string LocksFlag = AcpWords.LocksFlag;

    /// <param name="args">The argv from the mode on: <c>--profile</c>, <c>--model</c>, <c>--cwd</c>, <c>--deadline-seconds</c>, an optional <c>--executable</c> (a row's configured path), and for a profile that needs a home <c>--home</c>, <c>--persons-bin</c> and <c>--locks</c>.</param>
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

        return await TurnAsync(profile, flags, stdin, note, output, ct);
    }

    /// <summary>The request checked, the prompt read, the turn run and reported — for the profile <see cref="RunAsync"/> found.</summary>
    private static async Task<int> TurnAsync(
        IAcpProfile profile, IReadOnlyDictionary<string, string> flags, TextReader stdin, Action<string> note, TextWriter output, CancellationToken ct)
    {
        var cwd = flags.GetValueOrDefault("--cwd", string.Empty);
        var model = flags.GetValueOrDefault("--model", string.Empty);
        if (Refusal(profile, flags, cwd, model) is { Length: > 0 } refused)
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

        var ask = new AcpAsk(
            Executable(flags, profile), Path.GetFullPath(cwd), model, prompt, Deadline(flags),
            flags.GetValueOrDefault(AcpWords.HomeFlag, string.Empty), flags.GetValueOrDefault(AcpWords.PersonsBinFlag, string.Empty));
        using var rooted = LocksScope(flags);

        return await AskedAsync(profile, ask, note, output, ct);
    }

    /// <summary>The lock folder the turn's holds live in, for the whole turn — the <c>--locks</c> flag, never a folder derived from a profile this process does not have.</summary>
    private static IDisposable LocksScope(IReadOnlyDictionary<string, string> flags) =>
        flags.GetValueOrDefault(LocksFlag, string.Empty) is { Length: > 0 } locks ? KimiCredentialsLock.RootFor(locks) : Unscoped.Instance;

    /// <summary>
    /// The agent's request refused, else the hold taken, else the turn run under it. The hold is taken IMMEDIATELY before
    /// <see cref="AcpClient.AskAsync"/> with nothing awaited between (so no other launch can slip between the take and the start) and disposed
    /// once that has returned, by which time its <c>finally</c> has ended the agent. The turn gets what is left of the deadline after the wait.
    /// </summary>
    private static async Task<int> AskedAsync(IAcpProfile profile, AcpAsk ask, Action<string> note, TextWriter output, CancellationToken ct)
    {
        if (profile.AgentRefusal(profile.AgentRequest(ask), ask) is { Length: > 0 } refused)
        {
            return await NotStartedAsync(refused, BadRequest, note, output);
        }

        var waiting = Stopwatch.StartNew();
        var holding = profile.Hold(ask, ask.Deadline);
        if (holding is not AcpHolding.Held held)
        {
            var (sentence, exit) = Declined(holding);

            return await NotStartedAsync(sentence, exit, note, output);
        }

        return await HeldAsync(profile, ask, held, ask.Deadline - waiting.Elapsed, note, output, ct);
    }

    /// <summary>
    /// The turn under a hold already taken. A hold taken late can leave the turn nothing — the client refuses "a deadline of 0s" — so under
    /// <see cref="MinimumTurn"/> the hold is given back at once and the end is the one a still-running tree gives: Busy, 69, nothing started.
    /// </summary>
    private static async Task<int> HeldAsync(
        IAcpProfile profile, AcpAsk ask, AcpHolding.Held held, TimeSpan left, Action<string> note, TextWriter output, CancellationToken ct)
    {
        if (left < MinimumTurn)
        {
            held.Hold.Dispose();

            return await NotStartedAsync(LateSentence(ask.Deadline), Unavailable, note, output);
        }

        AcpOutcome outcome;
        using (held.Hold)
        {
            outcome = await new AcpClient(profile, new FolderReadBoundary(ask.Cwd, profile.ExcludedNames)).AskAsync(ask with { Deadline = left }, ct);
        }

        return await ReportedAsync(profile, outcome, note, output);
    }

    private static string LateSentence(TimeSpan deadline) =>
        $"another Kimi started on this sign-in was still running until less than a second of this turn's {deadline.TotalSeconds:F0}s was left — it ends when its agent does; the turn is given up";

    /// <summary>What a hold that was not taken comes to: another tree holding on past the deadline is an end before an answer (69); a hold that cannot be taken at all is a request this shim will not make (65).</summary>
    private static (string Sentence, int Exit) Declined(AcpHolding holding) => holding switch
    {
        AcpHolding.Busy busy => (busy.Sentence, Unavailable),
        AcpHolding.Unusable unusable => (unusable.Sentence, BadRequest),
        _ => ("the sign-in hold came to an answer this shim has no word for — refused rather than started without it (D19)", BadRequest),
    };

    /// <summary>A turn that never started an agent, said as every other end is: the report on stdout, the sentence on stderr, the exit.</summary>
    private static async Task<int> NotStartedAsync(string sentence, int exit, Action<string> note, TextWriter output)
    {
        note(sentence);
        await output.WriteLineAsync(Report(AcpOutcome.NotStarted(sentence), AcpDiagnosis.None));

        return exit;
    }

    /// <summary>The report printed, the exit made from the outcome.</summary>
    private static async Task<int> ReportedAsync(IAcpProfile profile, AcpOutcome outcome, Action<string> note, TextWriter output)
    {
        var diagnosis = AcpDiagnosis.Of(outcome.Reason + "\n" + outcome.StdErr, profile.KnownFailures);
        await output.WriteLineAsync(Report(outcome, diagnosis));

        return Exit(outcome, diagnosis, note);
    }

    /// <summary>Why this shim will not even start the agent — or empty when it will. Every one of these is 65.</summary>
    internal static string Refusal(IAcpProfile profile, IReadOnlyDictionary<string, string> flags, string cwd, string model) =>
        new[] { FolderRefusal(cwd), ModelRefusal(model), DeadlineRefusal(flags), PlacesRefusal(profile, flags) }.FirstOrDefault(static reason => reason.Length > 0) ?? string.Empty;

    private static string FolderRefusal(string cwd) =>
        cwd.Length == 0 || !Directory.Exists(cwd)
            ? $"{Mode} needs --cwd <an existing folder the agent works in>" + (cwd.Length == 0 ? string.Empty : $" — '{cwd}' is not one")
            : string.Empty;

    private static string ModelRefusal(string model) =>
        model.Length == 0
            ? $"{Mode} needs --model <id> — the model is set on the session before the prompt, never left to the agent's default"
            : string.Empty;

    /// <remarks>
    /// Given, it must be a number of seconds the clock can hold: a value past the ceiling used to throw after the
    /// agent had started and leave it running (finding 4 of the security review).
    /// </remarks>
    private static string DeadlineRefusal(IReadOnlyDictionary<string, string> flags)
    {
        var given = flags.GetValueOrDefault("--deadline-seconds", string.Empty);

        return given.Length > 0 && !HoldableSeconds(given)
            ? $"{Mode}: --deadline-seconds must be a whole number from 1 to {MaxDeadlineSeconds} — '{given}' is not"
            : string.Empty;
    }

    /// <summary>
    /// A profile that needs a home (<see cref="IAcpProfile.NeedsAHome"/>) is refused without the three places the runtime resolved once in the
    /// unredirected parent: the owned root (which must exist), the person's tool folder, the lock folder. Inside this process none of them can be
    /// found again — the environment is the redirected one.
    /// </summary>
    private static string PlacesRefusal(IAcpProfile profile, IReadOnlyDictionary<string, string> flags)
    {
        if (!profile.NeedsAHome)
        {
            return string.Empty;
        }

        return new[]
        {
            HomeRefusal(flags.GetValueOrDefault(AcpWords.HomeFlag, string.Empty)),
            NamedRefusal(flags, AcpWords.PersonsBinFlag, "the person's tool folder, the first entry of the agent's PATH"),
            NamedRefusal(flags, LocksFlag, "the lock folder the tree hold lives in"),
        }.FirstOrDefault(static reason => reason.Length > 0) ?? string.Empty;
    }

    private static string HomeRefusal(string home) =>
        home.Length == 0 || !Directory.Exists(home)
            ? $"{Mode} needs {AcpWords.HomeFlag} <the coai-owned root holding home/ and os/>" + (home.Length == 0 ? string.Empty : $" — '{home}' is not an existing folder")
            : string.Empty;

    private static string NamedRefusal(IReadOnlyDictionary<string, string> flags, string flag, string what) =>
        flags.GetValueOrDefault(flag, string.Empty).Length == 0 ? $"{Mode} needs {flag} <{what}>" : string.Empty;

    private static bool HoldableSeconds(string given) =>
        int.TryParse(given, out var seconds) && seconds >= 1 && seconds <= MaxDeadlineSeconds;

    /// <summary>A configured path is the whole answer; otherwise the profile's own.</summary>
    private static string Executable(IReadOnlyDictionary<string, string> flags, IAcpProfile profile) =>
        flags.GetValueOrDefault(AcpWords.ExecutableFlag, string.Empty) is { Length: > 0 } configured ? configured : profile.DefaultExecutable;

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

        if (Diagnosed(outcome.End, diagnosis))
        {
            note($"{diagnosis.Name}: {outcome.Reason} — {diagnosis.Cure}");

            return DiagnosedExit(diagnosis.Kind);
        }

        note(outcome.Reason);

        return EndExit(outcome.End);
    }

    /// <summary>Whether a known failure names the end — never one the clock, the caller or this shim decided, whatever the agent said.</summary>
    private static bool Diagnosed(AcpEnd end, AcpDiagnosis diagnosis) =>
        diagnosis.Known && end is not (AcpEnd.Deadline or AcpEnd.Cancelled or AcpEnd.ModelNotOffered);

    /// <summary>A known failure's exit: a spent allowance, or a refusal.</summary>
    private static int DiagnosedExit(AcpFailureKind kind) => kind == AcpFailureKind.Spent ? Spent : Refused;

    /// <summary>An undiagnosed end's exit: the agent's own failure, a broken stream, or ended before an answer.</summary>
    private static int EndExit(AcpEnd end) => end switch
    {
        AcpEnd.AgentFailed => Failed,
        AcpEnd.Protocol => Protocol,
        AcpEnd.ModelNotOffered => BadRequest,
        _ => Unavailable,
    };

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
            ["usage"] = UsageObject(outcome),
            ["refusals"] = new JsonArray([.. outcome.Refusals.Select(r => (JsonNode?)new JsonObject { ["tool"] = r.Tool, ["kind"] = r.Kind, ["option"] = r.Option })]),
            ["toolCalls"] = new JsonArray([.. outcome.ToolCalls.Select(t => (JsonNode?)new JsonObject { ["id"] = t.Id, ["title"] = t.Title, ["kind"] = t.Kind, ["status"] = t.Status })]),
            ["reads"] = new JsonArray([.. outcome.Reads.Select(r => (JsonNode?)new JsonObject { ["path"] = r.Path, ["served"] = r.Served, ["reason"] = r.Reason })]),
            // The three lists are capped by count (AcpSession.MaxTraceEntries), by field (MaxTraceFieldChars) and by their
            // sum (MaxTraceChars); what they could not hold is counted here, so the report stays inside the same bounds.
            ["droppedTraceEntries"] = outcome.DroppedTraceEntries,
            ["truncatedTraceFields"] = outcome.TruncatedTraceFields,
        };
        if (diagnosis.Known)
        {
            report["failure"] = diagnosis.Name;
        }

        return report.ToJsonString();
    }

    /// <summary>The api shim's usage field names, so one reader serves both: the session's token totals when <c>/usage</c> gave them (unknown is <c>notCaptured</c> and 0, never a guess), plus the context numbers when reported.</summary>
    private static JsonObject UsageObject(AcpOutcome outcome)
    {
        var tokens = new JsonObject
        {
            ["tokensIn"] = outcome.Tokens.In,
            ["tokensOut"] = outcome.Tokens.Out,
            ["tokensCached"] = 0,
            ["tokensReasoning"] = 0,
            ["notCaptured"] = !outcome.Tokens.Captured,
        };
        if (outcome.Usage.Captured)
        {
            tokens["contextUsed"] = outcome.Usage.ContextUsed;
            tokens["contextSize"] = outcome.Usage.ContextSize;
        }

        return tokens;
    }

    internal static string EndWord(AcpEnd end) => end switch
    {
        AcpEnd.Answered => AcpWords.AnsweredWord,
        AcpEnd.NotStarted => AcpWords.NotStartedWord,
        AcpEnd.AgentFailed => "agent_failed",
        AcpEnd.Deadline => "deadline",
        AcpEnd.Protocol => "protocol",
        AcpEnd.ModelNotOffered => AcpWords.ModelNotOfferedWord,
        _ => "cancelled",
    };

    /// <summary>The scope of a turn with no lock folder to point at: nothing to restore.</summary>
    private sealed class Unscoped : IDisposable
    {
        public static readonly Unscoped Instance = new();

        public void Dispose()
        {
        }
    }
}
