using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Roles;

/// <summary>
/// The model names a role job may launch with (<c>shared/role-model-names.json</c>, embedded): the claude aliases and every id of the shape <c>claude-[a-z0-9.-]+</c>, and the names of the codex and agy lists.
/// A name that is not here is refused BEFORE a folder or a launch is made, with a sentence that names the setting that holds it.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> claude 2.1.330 does not refuse an unknown model name: it runs its default model and bills it (measured twice on 2026-11-23, about 1.62 USD; <c>SIGNALS.md</c> of story s3, section 6). So for
/// the one CLI that bills a typo the check is the only barrier. Codex and agy refused an unknown model before any paid call (recorded), so for them the list costs a refusal and not money — it is kept for the same reason the
/// owner asked for: a role never runs on a name nobody ever saw work.</para>
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
        private readonly Regex _claudeId;
        private readonly Dictionary<string, IReadOnlyList<string>> _lists;

        internal Table(IReadOnlyList<string> claudeAliases, string claudeIdPattern, IReadOnlyList<string> claudeKnownIds, IReadOnlyDictionary<string, IReadOnlyList<string>> lists)
        {
            ClaudeAliases = claudeAliases;
            ClaudeIdPattern = claudeIdPattern;
            ClaudeKnownIds = claudeKnownIds;
            _claudeAliases = new HashSet<string>(claudeAliases, StringComparer.Ordinal);
            _claudeId = new Regex(claudeIdPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
            _lists = new Dictionary<string, IReadOnlyList<string>>(lists, StringComparer.Ordinal);
        }

        internal IReadOnlyList<string> ClaudeAliases { get; }

        internal string ClaudeIdPattern { get; }

        internal IReadOnlyList<string> ClaudeKnownIds { get; }

        /// <summary>The names of a list vendor (<c>codex</c>, <c>agy</c>); empty for any other.</summary>
        internal IReadOnlyList<string> NamesOf(string vendor) => _lists.GetValueOrDefault(vendor.Trim().ToLowerInvariant()) ?? [];

        internal bool ClaudeAccepts(string model) => _claudeAliases.Contains(model) || (model.All(c => c is >= ' ' and <= '~') && _claudeId.IsMatch(model));

        internal string Refusal(string model, string vendor, string key)
        {
            var cli = vendor.Trim().ToLowerInvariant();
            if (cli == "claude")
            {
                return ClaudeAccepts(model)
                    ? string.Empty
                    : $"model '{model}' is not a name the claude CLI is known to accept: use one of {string.Join(", ", _claudeAliases)}, or an id that matches {ClaudeIdPattern} (for example claude-opus-5-5). "
                      + $"The CLI does NOT refuse an unknown name - it runs its default model and bills it - so a typo is refused here before any launch; fix it in {key}";
            }

            if (_lists.TryGetValue(cli, out var names))
            {
                return names.Contains(model, StringComparer.Ordinal)
                    ? string.Empty
                    : $"model '{model}' is not in the list of names known for the {cli} CLI ({string.Join(", ", names)}); fix it in {key}, or add a name you measured to shared/role-model-names.json";
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

        return new Table(Strings(claude.GetProperty("aliases")), claude.GetProperty("idPattern").GetString() ?? string.Empty, Strings(claude.GetProperty("knownIds")), lists);
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
