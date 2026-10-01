using System.Text;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>What one follow-up turn of an <c>api</c> question row is handed: the tail (A9, after D25 of the feature review).</summary>
/// <param name="Turn">Which turn this tail opens — 2 for the first follow-up.</param>
/// <param name="Turns">How many turns the conversation may have in all: one plus the follow-ups.</param>
/// <param name="Requests">What the model asked for in the turn just answered, as the parser accepted them.</param>
/// <param name="Rejected">Requests the parser could not read, each with its index and reason.</param>
/// <param name="ServedEarlier">Every slice served in the turns before the one just answered, resent so nothing read is lost.</param>
/// <param name="ServedNow">What the turn just answered was served and refused.</param>
/// <param name="Final">Whether this is the last turn: the cap, or the source budget spent.</param>
public sealed record QuestionTailInput(
    int Turn,
    int Turns,
    IReadOnlyList<SourceRequest> Requests,
    IReadOnlyList<RejectedEntry> Rejected,
    IReadOnlyList<ServedSlice> ServedEarlier,
    ServedTurn ServedNow,
    bool Final);

/// <summary>
/// Renders the tail a follow-up turn appends to the question row's BASE prompt — and only the tail: the
/// base is resent byte for byte, which is what lets a vendor's prompt cache reuse it (D25).
/// </summary>
/// <remarks>
/// The feature reviewer's <c>TurnTail</c> is findings-shaped — it repeats the previous findings and asks
/// for <c>notes</c> again — so it is not reused here; what IS reused is every rendering of served source
/// (<see cref="ServedTurn.Render"/>, <see cref="ServedSlice.Render"/>) and the request line
/// (<see cref="SourceRequestNote.Line"/>), so a slice reads the same to a consultant as to a reviewer.
/// </remarks>
public static class QuestionTail
{
    public const string HeadingPrefix = "## Turn ";

    public const string OnlyThisTurnCounts =
        "Only THIS turn's `answer` counts: answer the question again in full, from everything served so far.";

    public const string Final =
        "FINAL: this is the last turn — `sourceRequests` will be ignored. Answer from what you have.";

    public const string AskAgain =
        "If you still need source, ask again in `sourceRequests`; it is served in the next turn.";

    public static string Render(QuestionTailInput input)
    {
        var text = new StringBuilder("\n\n");
        text.Append(HeadingPrefix).Append(input.Turn).Append(" of ").Append(input.Turns).Append("\n\n");
        text.Append("### What you asked for\n\n").Append(Asked(input.Requests, input.Rejected)).Append('\n');
        text.Append(Earlier(input.ServedEarlier));
        text.Append("### Served for turn ").Append(input.Turn).Append("\n\n");
        text.Append(input.ServedNow.IsEmpty ? "nothing\n" : input.ServedNow.Render()).Append('\n');
        text.Append(OnlyThisTurnCounts).Append("\n\n");
        text.Append(input.Final ? Final : AskAgain).Append('\n');

        return text.ToString();
    }

    private static string Asked(IReadOnlyList<SourceRequest> requests, IReadOnlyList<RejectedEntry> rejected) =>
        requests.Count == 0 && rejected.Count == 0
            ? "nothing\n"
            : string.Concat(
                requests.Select(request => $"- {SourceRequestNote.Line(request)}\n")
                    .Concat(rejected.Select(r => $"- request {r.Index} could not be read: {r.Reason}\n")));

    private static string Earlier(IReadOnlyList<ServedSlice> served) =>
        served.Count == 0
            ? string.Empty
            : "### Served in earlier turns\n\n" + string.Concat(served.Select(slice => slice.Render() + "\n"));
}
