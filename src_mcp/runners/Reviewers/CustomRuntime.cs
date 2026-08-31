namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A vendor the operator added: any OpenAI-compatible endpoint, driven through the Codex CLI.
/// </summary>
/// <remarks>
/// <para>This is <see cref="DeepseekRuntime"/> generalised, and it makes that one a preset rather
/// than a special case. The panel can add a reviewer because adding one is data — a name, a base
/// URL, and a key under that name in the vault entry — and not a release.</para>
/// <para>The key variable is derived from the id (<c>mistral</c> → <c>MISTRAL_API_KEY</c>) so that
/// the vault entry, the environment and the panel all agree without a mapping table to keep in
/// step.</para>
/// </remarks>
public sealed class CustomCodexRuntime(string id, string baseUrl) : CodexRuntime
{
    public override string Provider => id;

    /// <summary>`mistral` → `MISTRAL_API_KEY`; a hyphen is not legal in an environment name.</summary>
    public static string KeyVariableFor(string id) =>
        $"{id.ToUpperInvariant().Replace('-', '_').Replace('.', '_')}_API_KEY";

    private protected override string KeyVariable => KeyVariableFor(id);

    private protected override IEnumerable<string> ProviderOverrides =>
    [
        "-c", $"model_provider={id}",
        "-c", $"model_providers.{id}.name={id}",
        "-c", $"model_providers.{id}.base_url={baseUrl}",
        "-c", $"model_providers.{id}.env_key={KeyVariableFor(id)}",
    ];
}
