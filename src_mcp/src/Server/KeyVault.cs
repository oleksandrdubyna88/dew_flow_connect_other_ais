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
public sealed class KeyVault(IProcessLauncher launcher, string executable = CredsCli.OnPath, IReadOnlyList<string>? fallbacks = null)
{
    public const string KeyVariable = "COAI_CREDS_KEY";

    private readonly IReadOnlyList<string> _fallbacks = fallbacks ?? [];

    /// <summary>
    /// The vault as this machine has it: <c>creds</c> from PATH, then the CLI the CredsForDevs
    /// extension installed (§9.10). The production road — every mode that reads the vault builds it here.
    /// </summary>
    public static KeyVault ForThisMachine(IProcessLauncher launcher, Func<string, string?> env) =>
        new(launcher, CredsCli.OnPath, CredsCli.Present(env, CredsCli.ThisOs, File.Exists));

    /// <summary>
    /// The vault, unlocked with the <see cref="KeyVariable"/> the LAYERED configuration holds — the
    /// production road for every mode that reads it.
    /// </summary>
    /// <remarks>
    /// <para><b>Layered, not the environment.</b> The panel writes the config key into the settings
    /// file and nowhere else, so <paramref name="configuration"/> is what <see cref="SettingsFile.Layer"/>
    /// returns: the file, with the client's environment outranking it key by key. Every vault read took
    /// the key from the raw environment until 2026-10-01, three lines after building that layer for every
    /// other setting — a key saved in the panel was never seen, and every vendor needing one was dropped
    /// from every round. <c>TheVaultKeyIsReadWhereThePanelWritesItTests</c> forbids the raw lookup.</para>
    /// <para>It is the ONLY public read: the key-taking one below is private, so a caller cannot hand the
    /// vault a key it looked up for itself — which is the shape the defect had, at all three sites.</para>
    /// <para>Where the <c>creds</c> CLI is found is still the environment's business — that is a fact
    /// about this machine, not a setting — which is why <see cref="ForThisMachine"/> keeps taking it.</para>
    /// </remarks>
    public Task<VaultKeys> ReadFromConfigurationAsync(Func<string, string?> configuration, CancellationToken ct = default) =>
        // Qualified on purpose: this is the one sanctioned lookup, and the scan that forbids every other
        // one proves it is still alive by finding THIS line.
        ReadAsync(configuration(KeyVault.KeyVariable), ct);

    private async Task<VaultKeys> ReadAsync(string? configKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configKey))
        {
            return VaultKeys.None($"no {KeyVariable} configured — keyless vendors still work on their own auth");
        }

        if (await RunFirstInstalledAsync(configKey, ct) is not { } result)
        {
            return VaultKeys.None(NotInstalled());
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

    /// <summary>
    /// <c>creds config &lt;key&gt;</c> through the first CLI that exists: PATH's, then each fallback in
    /// order — or null when none could be started at all.
    /// </summary>
    /// <remarks>
    /// Only "could not be started" moves on to the next place. A CLI that started and refused, timed out
    /// or printed junk has answered, and that answer is the vault's — asking a second copy would turn one
    /// refusal into two reads of a person's vault.
    /// </remarks>
    private async Task<ProcessResult?> RunFirstInstalledAsync(string configKey, CancellationToken ct)
    {
        foreach (var candidate in (string[])[executable, .. _fallbacks])
        {
            try
            {
                return await launcher.RunAsync(
                    new ProcessRequest(candidate, ["config", configKey], Environment.CurrentDirectory)
                    {
                        Timeout = TimeSpan.FromSeconds(30),
                    },
                    ct);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not there, or not startable: the next place is asked.
            }
        }

        return null;
    }

    /// <summary>The sentence for a CLI found nowhere, naming where it was looked for.</summary>
    private string NotInstalled() =>
        "the `creds` CLI is not installed on this machine — looked on PATH and in the CredsForDevs extension's folder"
        + (_fallbacks.Count == 0 ? " (not present)" : $" ({string.Join(", ", _fallbacks)})");

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
