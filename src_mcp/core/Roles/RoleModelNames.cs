using System.Text.Json;

namespace CoaiMcp.Core.Roles;

/// <summary>
/// The model names a role job may launch with (<c>shared/role-model-names.json</c>, embedded): the four claude aliases and the full ids of the verified list (<c>claude.verifiedIds</c>, each with the place it was seen as real), and the names of the codex and agy lists.
/// A name that is not here is refused BEFORE a folder or a launch is made, with a sentence that names the setting that holds it.
/// For codex a name is also accepted when it is a FAMILY (<c>codex.families</c>: terra, astra, sol, luna — the newest model of the family that the installed CLI lists, resolved when a job reaches the entry) or has the exact shape
/// <c>gpt-&lt;version&gt;-&lt;family&gt;</c> (<see cref="CodexModelShape"/>), so a new model needs no hand edit of the file (epoch 3 E3 s2 t5; ledger entry 2.1).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> claude 2.1.330 does not refuse an unknown model name: it runs its default model and bills it (measured twice on 2026-11-23, about 1.62 USD; <c>SIGNALS.md</c> of story s3, section 6). So for
/// the one CLI that bills a typo the check is the only barrier. For that reason a claude id is never accepted by its shape: <c>claude-opuss-5-5</c>, <c>claude-sonet-5-5</c> and <c>claude-opus-5-55</c> have the shape of a real id, and the CLI would run and bill its default model. Only the aliases and the listed ids pass; a newly verified id is added to the file (its <c>howToAdd</c> says how). Codex and agy refused an unknown model before any paid call (recorded), so for them a wrong name costs a refusal and not money.
/// <b>The shape is right for codex ONLY</b>: the codex CLI refuses a name it does not know before any paid call, and the name must also be in the CLI's own list (asked when the job reaches the entry), so a typo of a codex name costs nothing; the claude CLI runs and bills its default model, so a claude id is never accepted by its shape.
/// The agy list does not change.</para>
/// <para><b>Case counts.</b> The CLI is handed the name exactly as written, and <c>Sonnet</c> is not a name claude is known to accept; the router's own lookup in a list is case-insensitive and hands on the LIST's spelling, so only
/// a name that reaches this check as typed is judged as typed.</para>
/// <para>A vendor this file does not name is not judged here (<see cref="VendorRouting"/> knows the three CLIs a route may use; an unknown vendor is refused by the launcher).</para>
/// </remarks>
internal static class RoleModelNames
{
    internal const string ResourceName = "CoaiMcp.Core.role-model-names.json";

    private static readonly Lazy<Table> Shipped = new(() => Parse(ReadSeed()));

    /// <summary>The shipped table — for the tests, which hold every default and vector to it.</summary>
    internal static Table Current => Shipped.Value;

    /// <summary>The empty string when <paramref name="model"/> may be launched on <paramref name="vendor"/>, else the sentence that says why not and names <paramref name="key"/>, the setting to fix.</summary>
    internal static string Refusal(string model, string vendor, string key) => Current.Refusal(model, vendor, key);

    /// <summary>What the file says; one instance is the embedded file, and a test may parse another.</summary>
    internal sealed class Table
    {
        private readonly HashSet<string> _claudeAliases;
        private readonly HashSet<string> _claudeIds;
        private readonly Dictionary<string, IReadOnlyList<string>> _lists;

        internal Table(IReadOnlyList<string> claudeAliases, IReadOnlyList<string> claudeKnownIds, IReadOnlyDictionary<string, IReadOnlyList<string>> lists, IReadOnlyList<string>? codexFamilies = null)
        {
            CodexFamilies = codexFamilies ?? [];
            ClaudeAliases = claudeAliases;
            ClaudeKnownIds = claudeKnownIds;
            _claudeAliases = new HashSet<string>(claudeAliases, StringComparer.Ordinal);
            _claudeIds = new HashSet<string>(claudeKnownIds, StringComparer.Ordinal);
            _lists = new Dictionary<string, IReadOnlyList<string>>(lists, StringComparer.Ordinal);
        }

        internal IReadOnlyList<string> ClaudeAliases { get; }

        /// <summary>The families of codex (<c>codex.families</c>): a name with no version, the newest model of which the installed CLI lists.</summary>
        internal IReadOnlyList<string> CodexFamilies { get; }

        /// <summary>Whether <paramref name="model"/> is a codex family — a name with no version.</summary>
        internal bool IsCodexFamily(string model) => CodexFamilies.Contains(model, StringComparer.Ordinal);

        /// <summary>Whether <paramref name="model"/> is a codex name with the exact shape <c>gpt-&lt;version&gt;-&lt;family&gt;</c>, a family of <see cref="CodexFamilies"/>.</summary>
        internal bool IsCodexShaped(string model) => CodexModelShape.Has(model, CodexFamilies);

        internal IReadOnlyList<string> ClaudeKnownIds { get; }

        /// <summary>The names of a list vendor (<c>codex</c>, <c>agy</c>); empty for any other.</summary>
        internal IReadOnlyList<string> NamesOf(string vendor) => _lists.GetValueOrDefault(vendor.Trim().ToLowerInvariant()) ?? [];

        internal bool ClaudeAccepts(string model) => _claudeAliases.Contains(model) || _claudeIds.Contains(model);

        internal string Refusal(string model, string vendor, string key)
        {
            var cli = vendor.Trim().ToLowerInvariant();
            if (cli == "claude")
            {
                return ClaudeAccepts(model)
                    ? string.Empty
                    : $"model '{model}' is not a name the claude CLI is known to accept: use one of {string.Join(", ", _claudeAliases)}, or a verified full id ({string.Join(", ", ClaudeKnownIds)}). "
                      + $"The CLI does NOT refuse an unknown name - it runs its default model and bills it - so a typo is refused here before any launch; fix it in {key}, or add an id you verified to shared/role-model-names.json";
            }

            if (_lists.TryGetValue(cli, out var names))
            {
                // codex only: a family, or the exact shape of a model with a family of the list, is a name the CLI itself is asked about when the job reaches the entry (a name it does not list ends that entry model-unavailable).
                var codexOwn = cli == "codex" && (IsCodexFamily(model) || IsCodexShaped(model));

                return codexOwn || names.Contains(model, StringComparer.Ordinal)
                    ? string.Empty
                    : $"model '{model}' is not in the list of names known for the {cli} CLI ({string.Join(", ", names)}){(cli == "codex" ? $", is not a family ({string.Join(", ", CodexFamilies)}) and has not the shape gpt-<version>-<family>" : string.Empty)}; fix it in {key}, or add a name you measured to shared/role-model-names.json";
            }

            return string.Empty;
        }
    }

    /// <summary>The table of <paramref name="json"/> (the shape of <c>shared/role-model-names.json</c>). A file that does not read is a build fault, so it throws.</summary>
    internal static Table Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var claude = root.GetProperty("claude");
        var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["codex"] = Strings(root.GetProperty("codex").GetProperty("names")),
            ["agy"] = Strings(root.GetProperty("agy").GetProperty("names")),
        };

        var verified = claude.GetProperty("verifiedIds").EnumerateArray().Select(row => row.GetProperty("id").GetString() ?? string.Empty);

        var families = root.GetProperty("codex").TryGetProperty("families", out var listed) ? Strings(listed) : [];

        return new Table(Strings(claude.GetProperty("aliases")), [.. verified], lists, families);
    }

    private static IReadOnlyList<string> Strings(JsonElement list) => [.. list.EnumerateArray().Select(item => item.GetString() ?? string.Empty)];

    private static string ReadSeed()
    {
        using var stream = typeof(RoleModelNames).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"the embedded {ResourceName} is missing — the build did not embed shared/role-model-names.json");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
