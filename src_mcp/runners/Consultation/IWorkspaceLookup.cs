using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// coai's own read-only list and search over the roots a consultant was granted
/// (research/PLAN_agy_searches_through_coai.md §3) — what an antigravity consultant, which cannot list or search in plan
/// mode, asks for through a <c>coai-lookup</c> block.
/// </summary>
/// <remarks>
/// An interface here because the runtime that asks lives in the runners and the reader that answers
/// (<c>CoaiMcp.Server.WorkspaceLookup</c>) reuses the server's own link-resolving containment — the same inversion the
/// api rows' <see cref="QuestionMaterial"/> carries a resolver through.
/// </remarks>
public interface IWorkspaceLookup
{
    /// <summary>The granted roots, absolute — what the prompt names so the model can write a path that is served.</summary>
    IReadOnlyList<string> Roots { get; }

    /// <summary>Serves the lookups in order, within one turn's budget, and renders what each returned or why it was not served.</summary>
    LookupServed Serve(IReadOnlyList<LookupRequest> requests, CancellationToken ct);
}

/// <summary>One turn's results, rendered for the model.</summary>
/// <param name="Text">Every request's section, in order: what it returned, or <c>not served: … — why</c>.</param>
/// <param name="Served">How many requests returned something.</param>
/// <param name="Refused">How many were not served — refused, missing, or past the turn's budget.</param>
public sealed record LookupServed(string Text, int Served, int Refused);
