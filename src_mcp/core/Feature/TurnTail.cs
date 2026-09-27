using System.Text;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Feature;

/// <summary>
/// What one follow-up turn of a feature reviewer's conversation is handed: the tail (plan §4.9, D25;
/// story S3.2).
/// </summary>
/// <param name="Turn">Which turn this tail opens — 2 for the first follow-up.</param>
/// <param name="Turns">How many turns the conversation may have in all: one plus the follow-ups.</param>
/// <param name="PreviousFindings">The reviewer's own findings from the turn just answered, repeated compactly so it can stand by them or drop them.</param>
/// <param name="Requests">What it asked for in that turn, as the parser accepted them.</param>
/// <param name="Rejected">Requests the parser could not read, each with its index and reason.</param>
/// <param name="ServedEarlier">Every slice served in the turns BEFORE the one just answered — the conversation's source so far, resent so nothing read is lost.</param>
/// <param name="ServedNow">What the turn just answered was served and refused.</param>
/// <param name="Final">Whether this is the conversation's last turn: the cap, or the source budget spent.</param>
public sealed record TurnTailInput(
    int Turn,
    int Turns,
    IReadOnlyList<Finding> PreviousFindings,
    IReadOnlyList<SourceRequest> Requests,
    IReadOnlyList<RejectedEntry> Rejected,
    IReadOnlyList<ServedSlice> ServedEarlier,
    ServedTurn ServedNow,
    bool Final);

/// <summary>
/// Renders the tail a follow-up turn appends to the BASE prompt — and only the tail: the base is
/// resent byte for byte, which is what lets a vendor's prompt cache reuse it (D25), and "turn k of N"
/// appears here and nowhere above.
/// </summary>
/// <remarks>
/// <para>The order is the plan's: the reviewer's previous findings, compact; what it asked for; the
/// served code, fenced with its path, lines and commit (<see cref="ServedTurn.Render"/>); the "not
/// served" lines; the instruction that only this turn's answer counts; and, on the last turn, FINAL.
/// Source served in EARLIER turns is repeated before this turn's, so a reviewer three turns in still
/// holds everything it read — the reviewer's allowance (<see cref="SourceBudget.ReviewerBytes"/>) bounds
/// the whole of it.</para>
/// <para>Pure, so every line of it is a test without a repository or a reviewer.</para>
/// </remarks>
public static class TurnTail
{
    /// <summary>The heading every follow-up turn opens with — the one place "turn k of N" is said.</summary>
    public const string HeadingPrefix = "## Turn ";

    public const string OnlyThisTurnCounts =
        "Only THIS turn's answer counts: the findings above are not carried over. Repeat every finding "
        + "you still stand by, revise the ones the source changed, and drop the ones it refuted — and fill "
        + "`notes` again in full.";

    public const string Final =
        "FINAL: this is the last turn — `sourceRequests` will be ignored. Answer from what you have.";

    public const string AskAgain =
        "If you still need source, ask again in `sourceRequests`; it is served in the next turn.";

    public static string Render(TurnTailInput input)
    {
        // Opens with its own separator, so `base + tail` reads as two sections however the base ends —
        // and the base is never touched, which is the whole of D25.
        var text = new StringBuilder("\n\n");
        text.Append(HeadingPrefix).Append(input.Turn).Append(" of ").Append(input.Turns).Append("\n\n");
        text.Append("### Your findings from turn ").Append(input.Turn - 1).Append(" (compact)\n\n");
        text.Append(Findings(input.PreviousFindings)).Append('\n');
        text.Append("### What you asked for\n\n");
        text.Append(Asked(input.Requests, input.Rejected)).Append('\n');
        text.Append(Earlier(input.ServedEarlier));
        text.Append("### Served for turn ").Append(input.Turn).Append("\n\n");
        text.Append(input.ServedNow.IsEmpty ? "nothing\n" : input.ServedNow.Render()).Append('\n');
        text.Append(OnlyThisTurnCounts).Append("\n\n");
        text.Append(input.Final ? Final : AskAgain).Append('\n');

        return text.ToString();
    }

    /// <summary>One line per finding — severity, category, place, title — or "none".</summary>
    private static string Findings(IReadOnlyList<Finding> findings) =>
        findings.Count == 0
            ? "none\n"
            : string.Concat(findings.Select(finding => $"- {Compact(finding)}\n"));

    /// <summary>A finding on one line: <c>[major/reliability] src/Shop.cs:3 — the title</c>.</summary>
    public static string Compact(Finding finding) =>
        $"[{finding.Severity.ToString().ToLowerInvariant()}/{finding.Category.ToString().ToLowerInvariant()}] "
        + (finding.File.Length > 0 ? $"{finding.File}{(finding.Line > 0 ? $":{finding.Line}" : string.Empty)} — " : string.Empty)
        + SourceFence.OneLine(finding.Title);

    private static string Asked(IReadOnlyList<SourceRequest> requests, IReadOnlyList<RejectedEntry> rejected) =>
        requests.Count == 0 && rejected.Count == 0
            ? "nothing\n"
            : string.Concat(requests.Select(request => $"- {SourceRequestNote.Line(request)}\n"))
              + string.Concat(rejected.Select(entry => $"- request {entry.Index} could not be read: {entry.Reason}\n"));

    private static string Earlier(IReadOnlyList<ServedSlice> served) =>
        served.Count == 0
            ? string.Empty
            : "### Served in earlier turns\n\n" + string.Concat(served.Select(slice => slice.Render() + "\n"));
}
