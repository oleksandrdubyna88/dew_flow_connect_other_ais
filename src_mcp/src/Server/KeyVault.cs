using System.Text.Json;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>Per-vendor keys, or the named reason there are none. Never an exception, never a log line.</summary>
public sealed record VaultKeys(IReadOnlyDictionary<string, string> Keys, string Unavailability)
{
    public static VaultKeys None(string reason) =>
        new(new Dictionary<string, string>(), reason);

    public bool Available => Unavailability.Length == 0;
}

/// <summary>
/// The one sanctioned key path: a CredsForDevs <c>config</c> entry read ONCE at startup via
/// <c>creds config &lt;key&gt;</c>. An agent is never in this chain — the config route is the
/// vault's app-reads-its-own-secrets door, authenticated by a key only the person can mint.
/// </summary>
/// <remarks>
/// <para>Missing binary, missing key, a 401 (wrong and revoked are indistinguishable by the
/// vault's own design), a malformed body: each is a named per-vendor unavailability surfaced by
/// <c>providers</c> — never a crash, never a silent fallback to an unauthenticated CLI, and never
/// a partial apply.</para>
/// <para>Read once: rotation takes effect when the MCP client restarts the server, and
/// <c>providers</c> reports when the read happened.</para>
/// </remarks>
public sealed class KeyVault(IProcessLauncher launcher, string executable = "creds")
{
    public const string KeyVariable = "COAI_CREDS_KEY";

    public async Task<VaultKeys> ReadAsync(string? configKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(configKey))
        {
            return VaultKeys.None($"no {KeyVariable} configured — keyless vendors still work on their own auth");
        }

        ProcessResult result;
        try
        {
            result = await launcher.RunAsync(
                new ProcessRequest(executable, ["config", configKey], Environment.CurrentDirectory)
                {
                    Timeout = TimeSpan.FromSeconds(30),
                },
                ct);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return VaultKeys.None("the `creds` CLI is not installed on this machine");
        }

        if (result.TimedOut)
        {
            return VaultKeys.None("creds config timed out — is a VS Code window with the vault open?");
        }

        if (result.ExitCode != 0)
        {
            return VaultKeys.None($"creds config refused (exit {result.ExitCode}) — the key may be revoked, or no unlocked window holds the entry");
        }

        return Parse(result.StdOut);
    }

    internal static VaultKeys Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return VaultKeys.None("the config entry is valid JSON but not an object of vendor keys");
            }

            var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { Length: > 0 } value)
                {
                    keys[property.Name] = value;
                }
            }

            return new VaultKeys(keys, string.Empty);
        }
        catch (JsonException)
        {
            return VaultKeys.None("the config entry's body is not valid JSON — nothing was applied");
        }
    }
}
