using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.Feature;

/// <summary>
/// What a feature reviewer asked to see, written onto its note — how a source request is RECORDED
/// before the turn loop that answers it exists (S3.2).
/// </summary>
/// <remarks>
/// <para>The feature schema offers <c>sourceRequests</c> from S1.3 on, and the reviewer is told a
/// request is recorded and handed to the implementer. This is where that sentence is made true: the
/// requests ride on the reviewer's <c>notes</c> in the reply — the one field an AI that was never told
/// to look for a new one still reads — and a request the parser refused is named with its reason, so a
/// malformed one is visible rather than dropped.</para>
/// <para>Pure; empty when the reviewer asked for nothing, which is every other stage (their schema has
/// no such field) and most feature reviewers.</para>
/// </remarks>
public static class SourceRequestNote
{
    public const string Heading = "Asked for source (recorded; not served inside this review — the loop that answers it comes later):";

    /// <summary>The reviewer's own notes followed by its requests, or the notes alone when it asked for nothing.</summary>
    public static string With(NormalisedReview review)
    {
        var requests = Render(review);

        return (review.Notes.Trim().Length, requests.Length) switch
        {
            (_, 0) => review.Notes,
            (0, _) => requests,
            _ => review.Notes.TrimEnd() + "\n\n" + requests,
        };
    }

    /// <summary>The requests alone, one line each, then the refused ones — or empty.</summary>
    public static string Render(NormalisedReview review) =>
        review.SourceRequests.IsEmpty && review.RejectedSourceRequests.IsEmpty
            ? string.Empty
            : Heading + "\n" + string.Join("\n", [
                .. review.SourceRequests.Select(Line),
                .. review.RejectedSourceRequests.Select(r => $"- request {r.Index} could not be read: {r.Reason}")]);

    private static string Line(SourceRequest request) =>
        $"- {request.File}{What(request)}{(request.Why.Length > 0 ? $" — {request.Why}" : string.Empty)}";

    private static string What(SourceRequest request) =>
        request.Symbol.Length > 0 ? $" `{request.Symbol}`"
        : request.StartLine > 0 ? $" lines {request.StartLine}–{request.EndLine}"
        : " (whole file)";
}
