using System.Text.RegularExpressions;

namespace CoaiMcp.Store;

/// <summary>
/// What the caller's Claude session and every subagent it launched actually did, read from the session's own transcript files
/// (E3.S4 of <c>PLAN_a_round_records_who_wrote_the_code_and_how_much_E3.md</c>; the contract E4 reads, §3 there).
/// </summary>
/// <remarks>Never a byte of prompt, description or message text: every string member is an id, a closed-shape value, a tool name
/// from <see cref="WriteTools.Names"/> or a path KEY — see <see cref="TranscriptShapes"/>.</remarks>
public sealed record ObservedSession(
    CallerSessionId Session,
    ObservationState State,
    DateTime? FirstLineUtc,
    IReadOnlyList<ObservedAgent> Agents,
    IReadOnlyList<ObservedMessage> Messages,
    IReadOnlyList<ObservedWrite> Writes,
    IReadOnlyList<UnobservedLaunch> UnobservedLaunches,
    ReadDiagnostics Diagnostics)
{
    /// <summary>Bumped by ANY change to a counting rule — E5 stores it as <c>authors_reader</c>.</summary>
    public const int ReaderVersion = 1;

    public bool IsObservedZero => State.Kind == ObservationKind.Observed && Agents.Count == 1;
}

/// <param name="Key"><c>main</c>, or the agent id taken from the FILE name.</param>
/// <param name="ParentKey"><c>main</c>, an agent id, or empty when no meta says who launched it.</param>
/// <param name="RequestedAlias">The meta's <c>model</c> when it is on the alias shape, else empty.</param>
/// <param name="LaunchedUtc">The agent file's first parseable stamped line; null for main and whenever <paramref name="TranscriptPresent"/> is false.</param>
/// <param name="CalledUtc">The launching <c>Agent</c> call's message instant, when that call was parsed.</param>
/// <param name="TranscriptPresent">True only when the agent's file yielded a parseable stamped line (S4-23), so <c>TranscriptPresent</c> implies <c>LaunchedUtc != null</c>.</param>
public sealed record ObservedAgent(
    string Key,
    string ParentKey,
    string ToolUseId,
    string AgentType,
    string RequestedAlias,
    DateTime? LaunchedUtc,
    DateTime? CalledUtc,
    bool TranscriptPresent);

/// <param name="Utc">The earliest line of the id.</param>
/// <param name="Requested">The meta's alias, else the earliest line's <c>requestedModel</c> when it is on the model-ref shape, else empty.</param>
/// <param name="Model"><c>message.model</c> when it is on the model-ref shape, else empty.</param>
/// <param name="Effort">One of <c>low|medium|high|xhigh|max</c>, else <c>unknown</c> — never carried from another line.</param>
/// <param name="Tokens">The per-term MAXIMUM over the id's lines.</param>
/// <param name="UsageSeen">False when no line of the id carried a usage object; the tokens are then zeros, not evidence.</param>
public sealed record ObservedMessage(
    string AgentKey,
    string MessageId,
    DateTime Utc,
    string Requested,
    string Model,
    string Effort,
    TokenUsage Tokens,
    bool UsageSeen);

public sealed record TokenUsage(long Input, long CacheCreation, long CacheRead, long Output);

/// <param name="Utc">Its message's instant (S4-8).</param>
/// <param name="AnsweredUtc">The stamp of the <c>user</c> line whose <c>tool_result</c> answered it — E4's <c>AnsweredAt</c> (S4-22).</param>
public sealed record ObservedWrite(string AgentKey, DateTime Utc, DateTime AnsweredUtc, string Tool, WriteTarget Target);

/// <summary>An <c>Agent</c> call or a meta with no readable transcript. Exactly ONE per meta without a readable file (S4-23).</summary>
public sealed record UnobservedLaunch(string ParentKey, string ToolUseId, DateTime? Utc, string Reason);

/// <summary>Counts only, never a string. <c>LinesTooLong</c> and <c>ReadFailures</c> are mirrored from the scan meter and each is also a reason.</summary>
public sealed record ReadDiagnostics(
    int FilesRead,
    int FilesSkippedByLastWrite,
    long BytesScanned,
    long BytesInSlice,
    int LinesSkippedBeforeParse,
    int LinesTooLong,
    int ReadFailures,
    int TornTails,
    int SyntheticLines,
    int MetasWithoutModel,
    int ValuesOffShape,
    int WriteCallsRefused,
    int WriteCallsUnanswered,
    int MessagesWithoutUsage,
    int MessagesDroppedAtBudget,
    int AgentFilesEmpty)
{
    /// <summary>Nothing was opened.</summary>
    public static ReadDiagnostics None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

public enum ObservationKind
{
    Observed,
    Partial,
    Unobserved,
}

public sealed record ObservationReason(string Code, int Count);

/// <summary>
/// <c>observed</c>, <c>partial: &lt;code&gt;[ ×N][, &lt;code&gt;…]</c> or <c>unobserved: &lt;code&gt;</c>. A reason with a count above one
/// carries it as <c> ×N</c>; <see cref="Render"/> never pluralises — grammar over a closed vocabulary is text manipulation.
/// </summary>
public sealed record ObservationState(ObservationKind Kind, IReadOnlyList<ObservationReason> Reasons)
{
    public static ObservationState Observed { get; } = new(ObservationKind.Observed, []);

    public static ObservationState Unobserved(string code) => new(ObservationKind.Unobserved, [new ObservationReason(code, 1)]);

    public static ObservationState Partial(IReadOnlyList<ObservationReason> reasons) => new(ObservationKind.Partial, reasons);

    public string Render() => Kind switch
    {
        ObservationKind.Observed => "observed",
        ObservationKind.Partial => "partial: " + Joined(Reasons),
        _ => "unobserved: " + Joined(Reasons),
    };

    private static string Joined(IReadOnlyList<ObservationReason> reasons) => string.Join(", ", reasons.Select(Part));

    private static string Part(ObservationReason reason) => reason.Count > 1 ? reason.Code + " ×" + reason.Count : reason.Code;
}

/// <summary>What one read may spend. <see cref="MaxBytes"/> is charged on the SLICE the byte gate yielded, <see cref="MaxTime"/> on the whole read.</summary>
public sealed record ReadBudget(long MaxBytes, TimeSpan MaxTime, TimeProvider Clock)
{
    /// <summary>Handed to the scan meter: a line over it is skipped, never buffered whole (plan gate G2).</summary>
    public long MaxLineBytes { get; init; } = 64L * 1024 * 1024;

    public static ReadBudget Unbounded { get; } = new(long.MaxValue, TimeSpan.MaxValue, TimeProvider.System);
}

/// <summary>THE closed vocabulary of reasons. E3.S5 reflects over this one class; nothing read from a transcript can become one.</summary>
public static class ObservationReasons
{
    // unobserved — E3.S2's, re-exposed by const aliasing so one class holds every code
    public const string NoSessionId = CallerTranscriptLocation.NoSessionId;
    public const string StatedId = CallerTranscriptLocation.StatedId;
    public const string TranscriptNotFound = CallerTranscriptLocation.TranscriptNotFound;
    public const string TranscriptAmbiguous = CallerTranscriptLocation.TranscriptAmbiguous;

    /// <summary>A main file with no parseable timestamped line.</summary>
    public const string TranscriptEmpty = "transcript empty";

    // partial — a meta without a model is NOT here (F6): it is Diagnostics.MetasWithoutModel
    public const string AgentCallWithoutTranscript = "agent call without transcript";
    public const string MetaWithoutTranscript = "meta without transcript";
    public const string AgentWithoutMeta = "agent without meta";
    public const string TornLine = "torn line";

    /// <summary>A meta file that does not parse (G1).</summary>
    public const string TornMeta = "torn meta";

    /// <summary>A line over <see cref="ReadBudget.MaxLineBytes"/>, skipped by the scan (G2).</summary>
    public const string LineTooLong = "line too long";

    /// <summary>A file whose read failed after the open (G2).</summary>
    public const string ReadFailed = "read failed";
    public const string ReadBudget = "read budget";

    /// <summary>One code, the five triggers listed in §3.2 of the plan: each is a layout assumption that broke.</summary>
    public const string InconsistentTranscript = "inconsistent transcript";
    public const string SidechainWithoutAgentId = "sidechain without agent id";

    public static string NoReaderFor(string vendor) => CallerTranscriptLocation.NoReaderFor(vendor);
}

/// <summary>
/// The ONLY place a transcript string becomes a member (S4-18): a value is admitted when it matches a closed shape and is empty
/// otherwise, so the reader is text-free by construction whatever a future CLI writes into these fields.
/// </summary>
internal static partial class TranscriptShapes
{
    /// <summary>One of these, else the effort reads <c>unknown</c>. <c>perTurnEffort</c> is never read.</summary>
    public static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>A model id or an alias, either with the long-context suffix.</summary>
    [GeneratedRegex(@"^(claude-[a-z0-9-]{1,40}|[a-z]{2,12})(\[1m\])?\z", RegexOptions.CultureInvariant)]
    public static partial Regex ModelRef();

    /// <summary>The alias half of <see cref="ModelRef"/>: what a meta's <c>model</c> may be.</summary>
    [GeneratedRegex(@"^[a-z]{2,12}(\[1m\])?\z", RegexOptions.CultureInvariant)]
    public static partial Regex Alias();

    /// <summary>At most one leading capital, then lower-case name characters: <c>general-purpose</c>, <c>Explore</c>, <c>custom-1</c>, <c>feature-dev:code-reviewer</c>.</summary>
    [GeneratedRegex(@"^[A-Z]?[a-z0-9][a-z0-9:._-]{0,62}\z", RegexOptions.CultureInvariant)]
    public static partial Regex AgentType();

    /// <summary>
    /// An id a transcript or a meta carries — <c>msg_…</c>, <c>toolu_…</c>, a hex agent id: one token of letters, digits, <c>_</c> and
    /// <c>-</c>, at most 128 characters. It bounds what an id can carry to one such token, so a sentence, a path or a prompt read where an
    /// id belongs is refused (plan §3.4; E3 code gate, finding 15).
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]{0,127}\z", RegexOptions.CultureInvariant)]
    public static partial Regex Id();

    public static bool IsModelRef(string value) => ModelRef().IsMatch(value);

    public static bool IsId(string value) => Id().IsMatch(value);

    public static bool IsAlias(string value) => Alias().IsMatch(value);

    public static bool IsAgentType(string value) => AgentType().IsMatch(value);

    public static bool IsEffort(string value) => Efforts.Contains(value);

    /// <summary>
    /// The value when it matches <paramref name="shape"/>; empty when it is absent (not off-shape: nothing was written) or when it
    /// fails the shape, which is counted so the diagnostics can say a value was refused.
    /// </summary>
    public static Admitted Admit(string raw, Func<string, bool> shape)
    {
        if (raw.Length == 0)
        {
            return new Admitted(string.Empty, 0);
        }

        return shape(raw) ? new Admitted(raw, 0) : new Admitted(string.Empty, 1);
    }
}

/// <summary>A transcript string after its shape gate: the string itself or empty, and 1 when a PRESENT value was refused.</summary>
internal readonly record struct Admitted(string Value, int OffShape);
