using System.Text;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The <c>config.toml</c> coai WRITES into an owned Kimi home (D19 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>; E4.2 brief
/// section 7): the managed provider by name, its OAuth slot (the person's, so the junctioned credentials file is the one their own
/// launch uses), the default model and the one model block — and nothing else. No <c>api_key</c>, no hooks, no services.
/// </summary>
/// <remarks>
/// <para><b>Rendered from values, never copied</b>, and byte for byte the probe's rung-3 text
/// (<c>scripts/lib/kimi-home.mjs</c> <c>minimalConfigToml</c> with <c>withModels</c>; research/RESULTS_kimi_confinement.md section
/// 17): LF-separated, one trailing LF, no byte-order mark.</para>
/// <para><b>No escaping is invented.</b> A value holding a quote, a backslash or a control character would need an escape coai
/// never measured, so <see cref="Render"/> refuses it (<see cref="ArgumentException"/> naming the KEY, never the value) and the
/// caller turns that into a refusal.</para>
/// </remarks>
public static class KimiConfig
{
    /// <summary>The text of the home's <c>config.toml</c> for this slot and this model block.</summary>
    /// <exception cref="ArgumentException">A value would need escaping.</exception>
    public static string Render(KimiOauthSlot slot, KimiModelBlock block)
    {
        string[] lines =
        [
            $"default_model = {Quoted("id", block.Id)}",
            string.Empty,
            "[providers.\"managed:kimi-code\"]",
            "type = \"kimi\"",
            $"base_url = {Quoted("base_url", slot.BaseUrl)}",
            string.Empty,
            "[providers.\"managed:kimi-code\".oauth]",
            "storage = \"file\"",
            $"key = {Quoted("key", slot.Key)}",
            $"oauth_host = {Quoted("oauth_host", slot.OauthHost)}",
            string.Empty,
            $"[models.{Quoted("id", block.Id)}]",
            $"provider = {Quoted("provider", block.Provider)}",
            $"model = {Quoted("model", block.Model)}",
            $"max_context_size = {block.MaxContextSize}",
            $"capabilities = [ {string.Join(", ", block.Capabilities.Select(capability => Quoted("capabilities", capability)))} ]",
            string.Empty,
        ];

        return string.Join("\n", lines);
    }

    /// <summary>UTF-8 with no byte-order mark.</summary>
    public static byte[] Bytes(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    private static string Quoted(string name, string value) =>
        value.Any(character => character is '"' or '\\' || char.IsControl(character))
            ? throw new ArgumentException($"the value of '{name}' holds a quote, a backslash or a control character, which coai does not escape", name)
            : $"\"{value}\"";
}
