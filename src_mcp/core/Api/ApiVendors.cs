using System.Collections.Frozen;
namespace CoaiMcp.Core.Api;

/// <summary>
/// The registry: the ONE place a name becomes a vendor module.
/// </summary>
/// <remarks>
/// <para>A row names its module directly (<c>qwen</c>, <c>deepseek</c>, <c>glm</c>, <c>xai</c>,
/// <c>openai</c>) or by the dialect row it was written with before the modules existed — and every
/// calibrated row was: <c>dashscope</c> with the MODEL a module was measured on (<c>qwen3.8-max</c>,
/// <c>deepseek-v4-pro</c>, <c>glm-5.3</c>) resolves to that module, so a settings file from 2026-09-26 runs
/// exactly as it did, and <c>dashscope</c> with any other model runs generically on the same row.</para>
/// <para><b>By the exact model, never by a family prefix</b> — the GLM consultation's catch (2026-09-27):
/// a module's capabilities are what was measured and read for ONE model, and the vendor documents
/// <c>glm-5.2</c> with a thinking switch and a <c>medium</c> level that <c>glm-5.3</c> has not. Matched by
/// prefix, the 5.3 module would have refused settings the 5.2 model accepts. The same through the other
/// entrance (the consultation's second turn): a row that NAMES a module with a model it was not measured
/// on is set aside to the generic module over the module's own row — the measured transport kept (xAI's
/// routing header for a grok-4.6 row), nothing declared about the model — and <see cref="SetAside"/> says so
/// on <c>providers</c>, so the downgrade is never silent.</para>
/// <para>A name that is a row of the dialect table and nothing more (<c>local</c>) is the generic module
/// over that row. Null is the one legitimate answer for a name this build has never heard of: the shim
/// refuses it with 65 and the list of what it knows, before any request is sent.</para>
/// </remarks>
public static class ApiVendors
{
    /// <summary>The calibrated modules, by their own names — what a row may name directly.</summary>
    private static readonly IReadOnlyDictionary<string, IApiVendor> Modules = new Dictionary<string, IApiVendor>(StringComparer.OrdinalIgnoreCase)
    {
        [OpenAiCompatibleVendor.Generic.Name] = OpenAiCompatibleVendor.Generic,
        [XaiVendor.Instance.Name] = XaiVendor.Instance,
        [QwenVendor.Instance.Name] = QwenVendor.Instance,
        [DeepSeekVendor.Instance.Name] = DeepSeekVendor.Instance,
        [GlmVendor.Instance.Name] = GlmVendor.Instance,
    };

    /// <summary>The calibrated modules of the shared Alibaba row, by the ONE model each was measured on — read off the modules, never a second list.</summary>
    private static readonly FrozenDictionary<string, IApiVendor> MeasuredOnTheAlibabaRow =
        Modules.Values
            .Where(m => m.MeasuredModel.Length > 0 && string.Equals(m.Dialect.Name, DashScopeTransport.RowName, StringComparison.OrdinalIgnoreCase))
            .ToFrozenDictionary(m => m.MeasuredModel, m => m, StringComparer.OrdinalIgnoreCase);

    /// <summary>Every module name, in registry order — what the panel may offer and the shim says it knows.</summary>
    public static IReadOnlyList<string> Names => [.. Modules.Keys];

    /// <summary>Every module, in registry order — for a test or a listing that asks each one the same question.</summary>
    public static IReadOnlyList<IApiVendor> All => [.. Modules.Values];

    /// <summary>
    /// The module for a row: by module name (set aside for a model it was not measured on), by the
    /// <c>dashscope</c> row and the exact measured model, by any other row of the dialect table generically
    /// — or null for a name this build does not know.
    /// </summary>
    /// <param name="dialectOrName">The row's dialect field: a module name or a dialect-table row; empty is the generic <c>openai</c>.</param>
    /// <param name="model">The row's model — what tells the calibrated models apart on a shared row, and what a named module is held to.</param>
    public static IApiVendor? Resolve(string dialectOrName, string model)
    {
        var name = NameOf(dialectOrName);
        if (Modules.TryGetValue(name, out var module))
        {
            return ForModel(module, model);
        }

        return string.Equals(name, DashScopeTransport.RowName, StringComparison.OrdinalIgnoreCase) ? MeasuredModel(model) : Generic(name);
    }

    /// <summary>
    /// The sentence <c>providers</c> shows when a row named a module for a model it was not measured on —
    /// or empty when the row's module speaks for its model.
    /// </summary>
    public static string SetAside(string dialectOrName, string model) =>
        Modules.TryGetValue(NameOf(dialectOrName), out var module) && Mismatch(module, model)
            ? $"the '{module.Name}' module was measured on {module.MeasuredModel} and declares that model's levels and defaults — "
              + ModelPhrase(model)
              + $" runs on the same '{module.Dialect.Name}' row with nothing declared (any effort sent verbatim, no thinking switch); "
              + $"name {module.MeasuredModel}, or a module measured on the model, for calibrated settings"
            : string.Empty;

    private static string ModelPhrase(string model) =>
        model.Trim().Length > 0 ? $"'{model.Trim()}'" : "a row that names no model (the endpoint picks one)";

    private static string NameOf(string dialectOrName) =>
        dialectOrName.Trim().Length > 0 ? dialectOrName.Trim() : ApiDialects.OpenAiName;

    /// <summary>The named module for its own model, or the generic module over its row for any other.</summary>
    private static IApiVendor ForModel(IApiVendor module, string model) =>
        Mismatch(module, model) ? new OpenAiCompatibleVendor(module.Dialect) : module;

    /// <summary>
    /// A model that is not the module's measured one — an EMPTY model included (the GLM consultation's third
    /// turn): the endpoint then picks a model this build cannot name, and a module's declarations would be
    /// claims about a model nobody measured.
    /// </summary>
    private static bool Mismatch(IApiVendor module, string model) =>
        module.MeasuredModel.Length > 0
        && !string.Equals(module.MeasuredModel, model.Trim(), StringComparison.OrdinalIgnoreCase);

    private static IApiVendor MeasuredModel(string model) =>
        MeasuredOnTheAlibabaRow.TryGetValue(model.Trim(), out var measured) ? measured : new OpenAiCompatibleVendor(DashScopeTransport.Row);

    private static OpenAiCompatibleVendor? Generic(string row) =>
        ApiDialects.Named(row) is { } dialect ? new OpenAiCompatibleVendor(dialect) : null;
}
