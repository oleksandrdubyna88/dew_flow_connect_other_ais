using System.Text.Json;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// The files in a cached Vibe plugin package that make Vibe START something when the package is resolved — an MCP server,
/// an LSP server, a hook, a session-start command — and so may not be admitted by their place alone.
/// </summary>
/// <remarks>
/// <para><b>Why the place is not enough.</b> Vibe 2.26.1's store hands back an existing package folder without re-digesting
/// it (<c>mistralai_vibe_local_harness/vibe/plugins/_store.py:265-266</c>: <c>if final.is_dir(): return final</c>); every
/// session — create and restore alike — checks every pinned package out that way (<c>_session.py:359-368</c>); the
/// checkouts are resolved as the plugin folders outright (<c>vibe/app_server/_plugins.py:421-423</c>, <c>:541-560</c>) and
/// the servers they declare are connected before the session runs (<c>_plugins.py:204-213</c>: "where the declared servers
/// are connected"), which is before <c>--enabled-tools</c> filters anything (research/RESULTS_vibe_confinement.md §6, §7). The
/// declarations are read from <c>mcp.json</c> (<c>vibe/core/plugins/_native.py:1661</c>), <c>.mcp.json</c>
/// (<c>_claude.py:283</c>, <c>_codex.py:203</c>) and the foreign manifests' <c>mcpServers</c>/<c>hooks</c>/<c>lspServers</c>/
/// <c>sessionStart</c> fields (<c>_claude.py:59-62</c>, <c>_codex.py:45-47</c>, <c>_kimi.py:56-58</c>; 2.26.1 keeps
/// <c>lspServers</c> as unconverted package data, <c>_claude.py:339</c> — refused anyway, a later version may start them);
/// hooks from <c>ai.mistral.vibe/hooks.toml</c> (<c>_native.py:1159</c>), <c>hooks/hooks.json</c> (<c>_claude.py:486</c>)
/// and <c>hooks.json</c> (<c>_codex.py:409</c>). Paths are under Vibe 2.26.1's <c>Lib/site-packages</c>.</para>
/// <para>On a MEASURED Vibe every package file is admitted by its SHA-256 alone, so this only decides the shape check —
/// all that guards an unmeasured version. Names compare without case, because the Windows file system answers
/// <c>MCP.json</c> when Vibe asks for <c>mcp.json</c>; a declaration that does not parse is refused, never guessed at.</para>
/// </remarks>
internal static class VibePluginStarters
{
    /// <summary>Files a plugin root declares servers or hooks in, as their trailing path segments.</summary>
    private static readonly string[][] Declarations =
        [["mcp.json"], [".mcp.json"], [".claude-plugin", "plugin.json"], [".codex-plugin", "plugin.json"], ["kimi.plugin.json"], [".kimi-plugin", "plugin.json"]];

    /// <summary>Files whose every line is a hook.</summary>
    private static readonly string[][] HookFiles = [["ai.mistral.vibe", "hooks.toml"], ["hooks", "hooks.json"], ["hooks.json"]];

    /// <summary>The fields of a declaration that start a process when they are not empty.</summary>
    private static readonly string[] StartingFields = ["mcpServers", "hooks", "lspServers", "sessionStart"];

    /// <summary>What this package file would make Vibe start, as the refusal's clause — or empty when it starts nothing.</summary>
    public static string Problem(string relative, string path) =>
        EndsWithAny(relative, HookFiles) ? "is a hook file in a cached plugin package, and Vibe runs the hooks a package carries"
        : EndsWithAny(relative, Declarations) && Declares(path) ? "declares a server or a hook a cached plugin package would start"
        : string.Empty;

    private static bool EndsWithAny(string relative, string[][] tails)
    {
        var parts = relative.Split('/');

        return tails.Any(tail => parts.Length >= tail.Length && parts[^tail.Length..].SequenceEqual(tail, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Whether the declaration starts anything: a non-empty starting field, or text that is not a JSON object.</summary>
    private static bool Declares(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));

            return document.RootElement.ValueKind != JsonValueKind.Object
                   || document.RootElement.EnumerateObject().Any(field => StartingFields.Contains(field.Name, StringComparer.Ordinal) && !IsEmpty(field.Value));
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => true,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        _ => false,
    };
}
