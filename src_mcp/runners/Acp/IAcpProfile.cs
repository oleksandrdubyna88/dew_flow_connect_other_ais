using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// One vendor's ACP agent: how it is started, which config option carries the model, which names its
/// shim refuses to read, and which of its failure sentences coai can name.
/// </summary>
/// <remarks>
/// <para>One module per vendor behind one interface (the owner's rule, D1 of
/// <c>PLAN_vibe_and_kimi_runtimes.md</c>): the client (<see cref="AcpClient"/>) speaks the Agent Client
/// Protocol and knows no vendor; everything a vendor decides is a member here, so a second ACP agent is a
/// second class and never an <c>if</c> on a name. Today the only profile is <see cref="KimiAcpProfile"/>
/// (D15).</para>
/// </remarks>
public interface IAcpProfile
{
    /// <summary>The name a launch asks for it by — <c>--profile kimi</c>.</summary>
    string Name { get; }

    /// <summary>The executable to start when the launch names none — a bare command for the PATH, or the installer's own path.</summary>
    string DefaultExecutable { get; }

    /// <summary>The arguments that put the CLI into ACP mode on stdio.</summary>
    IReadOnlyList<string> Arguments { get; }

    /// <summary>The <c>session/set_config_option</c> id that selects the model.</summary>
    string ModelOptionId { get; }

    /// <summary>
    /// The names the read boundary refuses at every depth — the vendor's own project-instruction and
    /// configuration paths, as relative fragments (<c>AGENTS.md</c>, <c>.kimi-code</c>, <c>.agents/skills</c>).
    /// </summary>
    IReadOnlyList<string> ExcludedNames { get; }

    /// <summary>The failure sentences this vendor is known to write, each with its name and its cure.</summary>
    IReadOnlyList<AcpKnownFailure> KnownFailures { get; }

    /// <summary>
    /// Whether the agent runs only on an owned home the launch hands the shim (<c>--home</c>, <c>--persons-bin</c>) and only under the tree
    /// hold on the sign-in (<c>--locks</c>) — a shim without them is refused 65 (E4.3). False by default: a stand-in needs none.
    /// </summary>
    bool NeedsAHome => false;

    /// <summary>
    /// The request that starts the agent. By default the executable, <see cref="Arguments"/> and the working folder, inheriting this
    /// process's environment as every ACP agent did before a home existed; a profile that needs a home builds the whole environment
    /// itself (D19) and never inherits.
    /// </summary>
    ProcessRequest AgentRequest(AcpAsk ask) => new(ask.Executable, Arguments, ask.Cwd);

    /// <summary>Why this request must not start the agent — or empty. Asked before anything is held or started; a sentence is the shim's 65.</summary>
    string AgentRefusal(ProcessRequest request, AcpAsk ask) => string.Empty;

    /// <summary>
    /// The hold on the sign-in the agent's tree keeps, taken immediately before the agent starts, for as long as <paramref name="within"/>
    /// allows. Synchronous on purpose: nothing may yield between the take and the start (decision 10). By default there is nothing to hold.
    /// </summary>
    AcpHolding Hold(AcpAsk ask, TimeSpan within) => AcpHolding.Nothing;
}

/// <summary>What a known failure means for the launch: the account cannot have this (a sign-in or a plan), or it is spent.</summary>
public enum AcpFailureKind
{
    /// <summary>The agent will not serve this account or model — not signed in, or a plan without the model.</summary>
    Refused,

    /// <summary>An allowance ran out — a billing or quota refusal; waiting does not clear it.</summary>
    Spent,

    /// <summary>The agent failed in its own words — a known shape with no cure; the agent's own exit (70).</summary>
    Failed,
}

/// <summary>One sentence a vendor writes, the name coai records it under, and what a person does about it.</summary>
/// <param name="Phrase">The substring that identifies it, matched ordinally and case-insensitively.</param>
/// <param name="Name">The diagnosis name a reader of the trace sees — <c>subscription_model</c>, <c>not_signed_in</c>.</param>
/// <param name="Cure">The sentence that tells a person what to do.</param>
/// <param name="Kind">Which exit the shim answers with.</param>
public sealed record AcpKnownFailure(string Phrase, string Name, string Cure, AcpFailureKind Kind);

/// <summary>A known failure found in what the agent said — or <see cref="None"/>.</summary>
/// <param name="Name">The failure's name.</param>
/// <param name="Cure">What a person does about it.</param>
/// <param name="Kind">Which exit the shim answers with.</param>
/// <param name="Line">
/// The MATCHED EVIDENCE: the one line of the agent's stderr that held the row's phrase, when the match came from stderr (the reason named no row) — capped at <see cref="LineCap"/>.
/// Empty when the reason itself held the phrase (it already carries the sentence) and when nothing matched. A reader that must say WHICH sentence the vendor wrote
/// (a shared name such as <c>kimi_quota_spent</c> covers a 5-hour and a monthly row) quotes this, never the table's first phrase for the name.
/// </param>
public sealed record AcpDiagnosis(string Name, string Cure, AcpFailureKind Kind, string Line = "")
{
    /// <summary>Nothing recognised: the agent's own words stand.</summary>
    public static readonly AcpDiagnosis None = new(string.Empty, string.Empty, AcpFailureKind.Refused);

    /// <summary>The maximum length, in characters, of a matched line carried in the report.</summary>
    public const int LineCap = 400;

    /// <summary>True when a phrase matched.</summary>
    public bool Known => Name.Length > 0;

    /// <summary>
    /// The known failure the agent's REASON names, or — only when the reason names none — the one its stderr does (K4 of the code gate). The two used to be matched in
    /// one pass, so a bare sign-in reason plus any stderr line holding a longer row's phrase took that row's name, whatever the agent had said about the turn itself.
    /// </summary>
    public static AcpDiagnosis Of(string reason, string stderr, IReadOnlyList<AcpKnownFailure> known, bool stderrTruncated = false) =>
        Of(reason, known) is { Known: true } named ? named : FromStderr(stderr, stderrTruncated, known);

    /// <summary>
    /// The row stderr names, carrying the line it matched in (<see cref="Line"/>) — or <see cref="None"/>. The ROW is read from the whole kept head: the phrase is intact whenever it matches, and the
    /// capture's truncation sentence is not in the text at all (it is a flag, <paramref name="truncated"/>). Only the QUOTE is read from complete lines (<see cref="CompleteLines"/>): the line the capture
    /// boundary broke is never quoted. A phrase only on that line therefore names its row with an empty line, and the reason falls back to the table's phrases — still hopeless, so the ladder does not retry a spent quota.
    /// </summary>
    private static AcpDiagnosis FromStderr(string stderr, bool truncated, IReadOnlyList<AcpKnownFailure> known) =>
        Hit(stderr, known) is { } hit ? new AcpDiagnosis(hit.Name, hit.Cure, hit.Kind, LineWith(CompleteLines(stderr, truncated), hit.Phrase)) : None;

    /// <summary>
    /// The lines of <paramref name="stderr"/> that are COMPLETE. Whether the capture was cut is STATE the capture carries (<c>BoundedText.Truncated</c>, <c>AcpOutcome.StdErrTruncated</c>), never inferred from the text:
    /// a cut capture ends in a line the boundary broke — possibly inside a credential shorter than the redactor's minimum — so everything after its last line break is dropped. A truncated capture with NO line
    /// break holds no complete line at all and yields empty: a single unbroken truncated line is never quoted, by design. An untruncated stream ended, so its last line is complete, newline or not.
    /// </summary>
    private static string CompleteLines(string stderr, bool truncated)
    {
        if (!truncated)
        {
            return stderr;
        }

        var lastBreak = stderr.LastIndexOfAny(LineBreaks);

        return lastBreak < 0 ? string.Empty : stderr[..(lastBreak + 1)];
    }

    /// <summary>
    /// The line of <paramref name="text"/> that holds the first <paramref name="phrase"/> (case-blind). The ORDER is the contract (feature gate F1, risk consult R1): the WHOLE text is
    /// redacted FIRST (<see cref="Redaction.SafeSource"/>: the layout kept, nothing cut — a private-key block spans lines, and a line cut from its block is key material the redactor can no longer
    /// name), THEN the phrase is found in the redacted text, its line isolated at the nearest <c>\r</c> or <c>\n</c> on each side (a CR-only progress line and a CRLF never join lines or leak a
    /// control character) and trimmed, THEN a window is cut around the phrase in the REDACTED line. Cut first, a window can start exactly at a credential's value and take its label away. LAST, the
    /// window is made one line of a notice by <see cref="Redaction.SafeText"/>: <c>SafeSource</c> keeps a tab (a source's layout), and the quoted line holds no control character at all (CodeRabbit on
    /// PR #917) — a second pass over at most <see cref="LineCap"/> characters of already-redacted text, which can only take more out. The text
    /// is the agent's stderr, bounded by the capture to 64 Ki characters, so this is one whole-text redaction pass over at most that on a failure path — and none when the text cannot hold the phrase:
    /// redaction neither creates nor destroys a table phrase (<c>RedactionNeverTouchesAnyPhraseOfTheTable</c>), so the check before it is exact. A redaction that fails closed answers the marker, which holds
    /// no phrase: the line is then empty and nothing of the raw text is quoted. Empty also when the text holds no phrase.
    /// </summary>
    private static string LineWith(string text, string phrase) => LineWith(text, phrase, Redaction.SafeSource);

    /// <summary><see cref="LineWith(string, string)"/> with the redactor passed in, so a test can make it fail closed and count its calls.</summary>
    internal static string LineWith(string text, string phrase, Func<string, string> redact) =>
        text.Contains(phrase, StringComparison.OrdinalIgnoreCase) && redact(text) is var redacted && redacted.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) is var at and >= 0
            ? Redaction.SafeText(Window(Around(redacted, at).Trim(), phrase), LineCap)
            : string.Empty;

    /// <summary>The line of <paramref name="text"/> that contains index <paramref name="at"/>: from after the previous <c>\r</c> or <c>\n</c> to the next one.</summary>
    private static string Around(string text, int at)
    {
        var start = text.LastIndexOfAny(LineBreaks, at) + 1;
        var stop = text.IndexOfAny(LineBreaks, at);

        return text[start..(stop < 0 ? text.Length : stop)];
    }

    private static readonly char[] LineBreaks = ['\r', '\n'];

    /// <summary>A line of at most <see cref="LineCap"/> characters that still holds the phrase: the line whole when it fits, else a window that starts shortly before the phrase.</summary>
    private static string Window(string line, string phrase)
    {
        if (line.Length <= LineCap)
        {
            return line;
        }

        var start = Math.Max(0, line.IndexOf(phrase, StringComparison.OrdinalIgnoreCase) - WindowLeadIn);
        var end = Math.Min(line.Length, start + LineCap);

        return line[WholeStart(line, start)..WholeEnd(line, end)];
    }

    /// <summary>How many characters before the phrase a window starts.</summary>
    internal const int WindowLeadIn = 60;

    /// <summary>A start that does not land in the middle of a surrogate pair.</summary>
    private static int WholeStart(string text, int start) => start < text.Length && char.IsLowSurrogate(text[start]) ? start + 1 : start;

    /// <summary>An end that does not cut a surrogate pair in two.</summary>
    private static int WholeEnd(string text, int end) => end < text.Length && char.IsHighSurrogate(text[end - 1]) ? end - 1 : end;

    /// <summary>What stands between the agent's reason and the cure in the shim's note.</summary>
    public const string CureSeparator = " — ";

    /// <summary>
    /// The shim's note for this failure: its name, the agent's reason, and the cure — when there is one (a known shape with no cure ends at the reason).
    /// The ONE place the note's shape is written; <see cref="WithoutCure"/> reads it back.
    /// </summary>
    public string Note(string reason) => Cure.Length > 0 ? $"{Name}: {reason}{CureSeparator}{Cure}" : $"{Name}: {reason}";

    /// <summary>
    /// <paramref name="note"/> without the cure the shim appended (<see cref="Note"/>), so a reader that puts its OWN cure in front of the vendor's words
    /// does not say a cure twice. The note unchanged when it names no row of <paramref name="known"/> or does not end with that row's cure.
    /// </summary>
    public static string WithoutCure(string note, IReadOnlyList<AcpKnownFailure> known) =>
        known.FirstOrDefault(failure => EndsWithItsCure(note, failure)) is { } hit ? note[..^(CureSeparator.Length + hit.Cure.Length)] : note;

    /// <summary>
    /// The agent's reason out of a note whose row has NO cure (a known shape the shim quotes in the vendor's own words, such as <c>provider_error</c>) — the note without our
    /// failure-name prefix; empty for a note that names no such row. That text is the vendor's, whatever tag carried it.
    /// </summary>
    public static string CurelessReason(string note, IReadOnlyList<AcpKnownFailure> known) =>
        known.FirstOrDefault(failure => failure.Cure.Length == 0 && note.StartsWith(failure.Name + ": ", StringComparison.Ordinal)) is { } hit
            ? note[(hit.Name.Length + 2)..]
            : string.Empty;

    private static bool EndsWithItsCure(string note, AcpKnownFailure failure) =>
        failure.Cure.Length > 0
        && note.StartsWith(failure.Name + ": ", StringComparison.Ordinal)
        && note.EndsWith(CureSeparator + failure.Cure, StringComparison.Ordinal);

    /// <summary>
    /// The FIRST known failure whose phrase is in <paramref name="text"/>, in the profile's order — so a
    /// profile lists the specific sentence before the general one it contains (the subscription 401 begins
    /// with the words of the sign-in one).
    /// </summary>
    public static AcpDiagnosis Of(string text, IReadOnlyList<AcpKnownFailure> known) =>
        Hit(text, known) is { } hit ? new AcpDiagnosis(hit.Name, hit.Cure, hit.Kind) : None;

    private static AcpKnownFailure? Hit(string text, IReadOnlyList<AcpKnownFailure> known) =>
        known.FirstOrDefault(failure => text.Contains(failure.Phrase, StringComparison.OrdinalIgnoreCase));
}
