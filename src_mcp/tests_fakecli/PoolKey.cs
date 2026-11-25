using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The fake as a claude launched on a pool key (epoch 3 E2 s7, KEY_POOL_SPEC.md section 10): it reads <c>CLAUDE_CODE_OAUTH_TOKEN</c>, answers by a marker in the made-up token, records WHICH key and which variable names it was started with,
/// and can print its token or write it into a file — what a CLI that does not keep its promise would do.
/// </summary>
/// <remarks>
/// <para><b>The record holds the key's ID, never the text</b>: a <c>.keyid</c> file beside the launch's <c>.argv</c> says <c>k-</c> and 12 hex characters of the same hash the product makes (<c>coai-claude-key-v1</c> and a line break, then the text), and an <c>.envnames</c> file lists the
/// variable NAMES the launch had, and an <c>.envhome</c> file the values of the few names that say where its home is. A test reads those; the evidence folder of a job holds no key text.</para>
/// <para><b>Markers.</b> A model-map entry may carry <c>byKey</c>: an object of marker to an entry of the usual shape. When the token contains a marker the entry replaces the launch's own — so one scripted route has a key that is limited and one that answers.</para>
/// <para><b>The placeholder <c>{{claude-key}}</c></b> in <c>stdout</c>, <c>stderr</c> or the text of a <c>writeFiles</c> entry becomes the token: printing it and writing it into the worktree are the two ways a key could leave.</para>
/// </remarks>
internal static class PoolKey
{
    public const string Variable = "CLAUDE_CODE_OAUTH_TOKEN";

    /// <summary>The names whose VALUE is recorded (<c>.envhome</c>): where this launch's home is. None of them can hold a secret.</summary>
    private static readonly string[] HomeNames = ["HOME", "CLAUDE_CONFIG_DIR", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME"];

    private const string Placeholder = "{{claude-key}}";

    public static string Token => Environment.GetEnvironmentVariable(Variable) ?? string.Empty;

    /// <summary>The text with the placeholder replaced by the token (by nothing, when there is no token).</summary>
    public static string Fill(string text) => text.Contains(Placeholder, StringComparison.Ordinal) ? text.Replace(Placeholder, Token, StringComparison.Ordinal) : text;

    /// <summary>The key id the product gives a token.</summary>
    public static string IdOf(string token) =>
        "k-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("coai-claude-key-v1\n" + token)))[..12];

    /// <summary>Writes the sidecars of one recorded launch, in <c>sidecars/</c> beside the record: <c>&lt;stem&gt;.keyid</c> (the id, or empty when no key came) and <c>&lt;stem&gt;.envnames</c> (every variable name, one per line).</summary>
    public static void Record(string folder, string stem)
    {
        // In a folder of their own: the record folder holds the argv files and nothing else, and every reader of it counts them.
        var dir = Path.Combine(folder, "sidecars");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, stem + ".keyid"), Token.Length == 0 ? string.Empty : IdOf(Token));
        File.WriteAllLines(Path.Combine(dir, stem + ".envnames"), Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.Ordinal));
        File.WriteAllLines(Path.Combine(dir, stem + ".envhome"), HomeNames.Select(name => $"{name}={Environment.GetEnvironmentVariable(name)}"));
    }

    /// <summary>The entry of <c>byKey</c> whose marker the token holds, else the entry itself.</summary>
    public static JsonElement Choose(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("byKey", out var byKey) && byKey.ValueKind == JsonValueKind.Object && Token.Length > 0)
        {
            foreach (var marker in byKey.EnumerateObject())
            {
                if (Token.Contains(marker.Name, StringComparison.Ordinal))
                {
                    return marker.Value;
                }
            }
        }

        return entry;
    }
}
