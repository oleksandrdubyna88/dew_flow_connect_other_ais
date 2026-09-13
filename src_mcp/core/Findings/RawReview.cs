namespace CoaiMcp.Core.Findings;

/// <summary>The wire shape both vendors are asked for. Nullable throughout — the model fills it.</summary>
/// <param name="Notes">
/// This reviewer's prose about the whole artefact, when the prompt asked for one. Null on every code
/// round, because no code prompt asks — which is why nullable-throughout was already the rule here
/// and this field needed no exception to it.
/// </param>
internal sealed record RawReview(List<RawFinding>? Findings, string? Notes = null);

internal sealed record RawFinding(
    string? Severity,
    string? Category,
    string? File,
    int? Line,
    string? Title,
    string? Why,
    string? Fix);
