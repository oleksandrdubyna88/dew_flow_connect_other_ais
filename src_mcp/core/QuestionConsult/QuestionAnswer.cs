using System.Collections.Immutable;
using System.Text.Json;
using CoaiMcp.Core.Findings;

namespace CoaiMcp.Core.QuestionConsult;

/// <summary>
/// What a consultant's raw answer came to: the advice, and — for a route bound to
/// <see cref="QuestionAnswerSchema"/> — the source it asked for (PLAN_question_consultant.md, A9).
/// </summary>
/// <param name="Answer">The advice: the envelope's <c>answer</c>, or the raw text when the vendor answered prose.</param>
/// <param name="Requests">Requests the parser accepted — validated as a feature reviewer's are, by the same rule.</param>
/// <param name="RejectedRequests">Requests it refused, each named with its index and reason — never dropped.</param>
/// <param name="Structured">Whether the answer was the schema's JSON at all; prose is still an answer.</param>
public sealed record QuestionAnswer(
    string Answer,
    ImmutableArray<SourceRequest> Requests,
    ImmutableArray<RejectedEntry> RejectedRequests,
    bool Structured)
{
    public bool AsksForSource => !Requests.IsEmpty || !RejectedRequests.IsEmpty;

    /// <summary>
    /// The ONE reader of a consultant's answer envelope — the api question row's and the local
    /// consultant's alike (<c>ConsultantAnswer.TextOf</c> delegates here).
    /// </summary>
    /// <remarks>
    /// <para>Lenient the way every vendor answer is read: the balanced object is taken out of fences
    /// and banners (<see cref="GeminiPayload"/>), and anything that is not the envelope — prose, prose
    /// that happens to begin with a brace, an object with no <c>answer</c> — is the ANSWER as it
    /// arrived, with no requests. A consultant asked about a configuration file may well answer JSON
    /// with an <c>answer</c> property of its own; that is why an empty <c>answer</c> is returned as
    /// empty and never as the envelope.</para>
    /// <para>The requests go through <see cref="ReviewParser"/>'s own validation, so a path that could
    /// climb out of the repository is refused by one rule for every route that may ask for source.</para>
    /// </remarks>
    public static QuestionAnswer Parse(string raw)
    {
        if (GeminiPayload.Extract(raw) is not ExtractOutcome.Payload payload)
        {
            return Prose(raw);
        }

        RawQuestionAnswer? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(payload.Json, CoreJsonContext.Default.RawQuestionAnswer);
        }
        catch (JsonException)
        {
            return Prose(raw);
        }

        return parsed?.Answer is { } answer ? WithRequests(answer, parsed.SourceRequests) : Prose(raw);
    }

    private static QuestionAnswer WithRequests(string answer, List<RawSourceRequest?>? raw)
    {
        var requests = ImmutableArray.CreateBuilder<SourceRequest>();
        var refused = ImmutableArray.CreateBuilder<RejectedEntry>();
        ReviewParser.ReadRequests(raw, requests, refused);

        return new QuestionAnswer(answer, requests.ToImmutable(), refused.ToImmutable(), Structured: true);
    }

    private static QuestionAnswer Prose(string raw) => new(raw, [], [], Structured: false);
}
