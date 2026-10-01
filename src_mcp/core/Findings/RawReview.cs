namespace CoaiMcp.Core.Findings;

/// <summary>The wire shape both vendors are asked for. Nullable throughout — the model fills it.</summary>
/// <param name="Notes">
/// This reviewer's prose about the whole artefact, when the prompt asked for one. Null on every code
/// round, because no code prompt asks — which is why nullable-throughout was already the rule here
/// and this field needed no exception to it.
/// </param>
/// <param name="SourceRequests">
/// A FEATURE reviewer's requests for code (plan §4.9). Only <see cref="FindingSchema.FeatureJson"/>
/// offers the field, so it is absent on every other stage — and an element may be null, because a
/// model's JSON is whatever the model wrote.
/// </param>
internal sealed record RawReview(
    List<RawFinding>? Findings, string? Notes = null, List<RawSourceRequest?>? SourceRequests = null);

internal sealed record RawSourceRequest(string? File, string? Symbol, int? StartLine, int? EndLine, string? Why);

/// <summary>
/// The wire shape an <c>api</c> QUESTION consultant answers in (PLAN_question_consultant.md, A9):
/// prose, and the same source requests a feature reviewer may make — read through the same
/// request validation, so a path that could climb out of the repository is refused by one rule.
/// </summary>
internal sealed record RawQuestionAnswer(string? Answer, List<RawSourceRequest?>? SourceRequests = null);

internal sealed record RawFinding(
    string? Severity,
    string? Category,
    string? File,
    int? Line,
    string? Title,
    string? Why,
    string? Fix);
