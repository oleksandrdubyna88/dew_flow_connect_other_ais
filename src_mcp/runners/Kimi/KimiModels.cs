namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The models coai launches Kimi on (D10 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>): a curated list, checked BEFORE the
/// launch — a <c>moonshot-ai/*</c> id bills the person's own API key, and an id the Kimi Code plan does not include answers a
/// 401 only after the paid call (research/RESULTS_kimi_confinement.md section 2).
/// </summary>
public static class KimiModels
{
    /// <summary>What a row that names no model is launched on (RESULTS section 17 rung 3: answered 3 of 3).</summary>
    public const string Default = "kimi-code/k3-256k";

    /// <summary>The Kimi version the curated list was seen on.</summary>
    public const string SeenOn = "2.1.1";

    /// <summary>The prefix of every id on the Kimi Code plan; anything else is another provider's.</summary>
    public const string PlanPrefix = "kimi-code/";

    /// <summary>
    /// The curated ids, in the table's order: <c>shared/kimi-model-blocks.json</c>'s ids — ONE source, so the list and the model
    /// blocks cannot drift. Empty when the embedded table is broken, which <c>KimiLaunch</c> refuses by name before it asks.
    /// </summary>
    public static IReadOnlyList<string> Curated { get; } =
        KimiModelBlocks.Builtin is BlocksLoad.Loaded { Blocks: var table } ? [.. table.Blocks.Select(block => block.Id)] : [];

    /// <summary>
    /// D10, before the launch: why <paramref name="model"/> is not launched on — an id outside <c>kimi-code/</c> (another provider's, which bills the person's own API key) — or empty. An id ON the plan that coai has not seen answer is NOT
    /// refused here (the owner's decision of 2026-10-23: "not measured" is a note, never a refusal): it runs on the person's own <c>[models."id"]</c> block, with <see cref="NoteFor"/>; with no block anywhere it cannot run, and
    /// <see cref="NoBlockRefusal"/> says that.
    /// </summary>
    public static string RefusalFor(string model, IReadOnlyList<string> curated) => model switch
    {
        _ when curated.Contains(model, StringComparer.Ordinal) => string.Empty,
        _ when !model.StartsWith(PlanPrefix, StringComparison.Ordinal) =>
            $"the model '{model}' is not on the Kimi Code plan (only {PlanPrefix}* ids are: {string.Join(", ", curated)}); a moonshot-ai/* id bills your own API key, so coai sends nothing (D10)",
        _ => string.Empty,
    };

    /// <summary>
    /// The note a launch on an id of the plan that coai has not seen answer carries — one line — or empty (a curated id, or an id off the plan, which is refused instead). The same sentence for every id, with the measured list.
    /// </summary>
    public static string NoteFor(string model, IReadOnlyList<string> curated) =>
        model.StartsWith(PlanPrefix, StringComparison.Ordinal) && !curated.Contains(model, StringComparer.Ordinal)
            ? $"the model '{model}' is not in coai's measured list ({string.Join(", ", curated)}); it runs on your own config.toml block, not measured here — admitted by the owner's decision of 2026-10-23"
            : string.Empty;

    /// <summary>
    /// The REAL reason an uncurated id cannot run when neither the person's config nor the pinned table holds a block for it: coai writes a model block (context size, capabilities) into the owned config and has none to write.
    /// </summary>
    public static string NoBlockRefusal(string model, IReadOnlyList<string> curated) =>
        $"the model '{model}' has no model block: coai pins one only for {string.Join(", ", curated)}, and your config.toml has no [models.\"{model}\"] block (provider, model, max_context_size and a one-line capabilities array) — add that block, or choose one of {string.Join(", ", curated)} (D10)";
}
