namespace CoaiMcp.Core.Collecting;

/// <summary>
/// Which models may be shown a finding's own words.
/// </summary>
/// <remarks>
/// <para><b>This is a boundary, not a dropdown.</b> Two reviewers of the plan round found the same
/// thing independently: narrowing the list in the panel narrows a PICKER, and a picker is not an
/// enforcement. <c>--collect-bugs</c> can be invoked from a terminal, a setting persisted before the
/// list narrowed survives it, and a webview that has not repainted posts whatever it last rendered.
/// So the list lives here, the collector refuses anything outside it <i>before it reads a single
/// finding field</i>, and the panel derives its options FROM this list rather than keeping a second
/// one that agrees today.</para>
/// <para><b>What is at stake.</b> A finding's <c>title</c>, <c>why</c> and <c>fix</c> are the
/// reviewers' own prose about somebody's code — class names, product names, the shape of a private
/// system — and they are <b>not</b> sanitised. The normaliser runs later and only on source. So the
/// ranking pass is the one step in this pipeline that handles un-anonymised text, and it is the step
/// where pointing at the wrong endpoint leaks before anything has been normalised.</para>
/// <para><b>Why local-only, and the honest complication.</b> A finding's words were frequently
/// WRITTEN by a cloud model — the reviewer that produced them read the diff to do it — so sending
/// them back to a cloud vendor is not categorically a new exposure. But it is an uncontrolled one:
/// the corpus spans fifteen repositories and rounds whose vendor sets differ, and a round answered
/// only by the local reviewer produced words that have never left this machine. Local-only is the
/// defensible default until a person decides otherwise, and this type is where that decision would
/// be recorded.</para>
/// </remarks>
public static class RankingModels
{
    /// <summary>The RUNTIMES whose engines run on the machine that asks them.</summary>
    /// <remarks>
    /// A runtime rather than a whole model name, because the model half changes whenever somebody pulls a new one and a
    /// list of exact names would refuse a model the person installed this morning. And a runtime rather than the row id
    /// it used to be (todo/PLAN_one_model_catalog.md, epic 2, story 1): the id is a name a person chose — `local-2`, the
    /// migration's `bugz-local` — while the runtime is what decides whether the text leaves the machine.
    /// </remarks>
    /// <remarks>
    /// <c>IReadOnlyList</c> rather than an array: a public <c>string[]</c> protects the reference
    /// and nothing else, so any caller could rewrite an element and move the boundary this type
    /// exists to hold. (Code round, codex.)
    /// </remarks>
    public static readonly IReadOnlyList<string> Local = ["local"];

    /// <summary>Whether this model, on this runtime, may be shown un-anonymised finding text.</summary>
    /// <remarks>
    /// An EMPTY model is allowed and means "no ranking pass": the collector does not call a model at
    /// all, and refusing the absence of one would make `--collect-bugs` impossible to run from a
    /// terminal. What is refused is a model that was NAMED and is not local.
    /// </remarks>
    /// <param name="model"><c>rowId/model</c>, as the Bugz setting holds it.</param>
    /// <param name="runtime">
    /// The runtime of the row the model names, as the caller resolved it from the catalog (`--runtime`). Empty when the
    /// caller did not say — a terminal, or an extension from before the catalog — and then the row id stands in for it,
    /// which is what <c>local/qwen</c> has always meant.
    /// </param>
    public static bool IsAllowed(string model, string runtime) =>
        model.Length == 0 || Local.Contains(RuntimeOf(model, runtime), StringComparer.Ordinal);

    /// <summary>The runtime said, lower case — or, when none was, the row id that stood in for it.</summary>
    private static string RuntimeOf(string model, string runtime) =>
        runtime.Trim().Length > 0 ? runtime.Trim().ToLowerInvariant() : VendorOf(model);

    /// <summary>The vendor half of <c>vendor/model</c>, lower case.</summary>
    /// <remarks>
    /// A name with no slash is all vendor and no model, which is what a bare `local` is. A name with
    /// several is split at the FIRST, because a model name may legitimately carry one — `local/
    /// qwen3.5:35b-a3b` does not, but a Team server's `remsoftdev-claude/some/path` would.
    /// </remarks>
    public static string VendorOf(string model)
    {
        var slash = model.IndexOf('/', StringComparison.Ordinal);

        return (slash < 0 ? model : model[..slash]).ToLowerInvariant();
    }

    /// <summary>Why a model was refused, in the words the person needs to fix it — its runtime named.</summary>
    public static string Refusal(string model, string runtime) =>
        $"'{model}' is not a local model: it runs on '{RuntimeOf(model, runtime)}'. A finding's title, why and fix "
        + "are not anonymised — the normaliser runs later and only on source — so the ranking pass reads them on this "
        + $"machine or not at all. Allowed runtimes: {string.Join(", ", Local)}.";
}
