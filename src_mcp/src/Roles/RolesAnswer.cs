using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Server;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Roles;

/// <summary>The settings keys of the roles table. <c>COAI_ROLES</c> itself is the REVIEW roles' key (v1) and is not touched; these are the v2 external roles'.</summary>
internal static class RolesKeys
{
    /// <summary>A JSON object, role name to a list of <c>{model, vendor}</c> — the ORDER is the failover order.</summary>
    internal const string Models = "COAI_ROLES_MODELS";

    /// <summary>A JSON list of <c>{min, max, model, vendor}</c>.</summary>
    internal const string Bands = "COAI_ROLES_BANDS";

    /// <summary>A JSON list of task kinds a Haiku-class model may take.</summary>
    internal const string HaikuKinds = "COAI_ROLES_HAIKU_KINDS";

    /// <summary>A JSON object over the work method's numbers.</summary>
    internal const string WorkMethod = "COAI_ROLES_WORK_METHOD";

    /// <summary>Which main AI the table is for — <c>claude</c>, <c>codex</c> or <c>agy</c> — when it cannot be told from the client.</summary>
    internal const string MainAi = "COAI_ROLES_MAIN_AI";

    internal static readonly IReadOnlyList<string> All = [Models, Bands, HaikuKinds, WorkMethod, MainAi];
}

/// <summary>
/// The roles answer (v2 E3.S7 t1, server.md §2, §19, §20): the main AI, an ORDERED model list per role, the complexity bands, the Haiku kinds, the work method and a
/// revision of the settings it was made from — the defaults of <c>shared/role-defaults.json</c> for the main AI's vendor, with a person's <c>COAI_ROLES_*</c> over them.
/// </summary>
/// <remarks>
/// <para><b>One bad key costs that key.</b> A setting that does not read keeps its default and is named in <c>unrecognised</c> — the way the vendor rows are read — because a table
/// half-applied from a broken setting would send a task to a model nobody chose.</para>
/// <para><b>Pure over its inputs:</b> the environment lookup (already layered over the settings file by the caller), what the client said it is, and the configured vendor rows.</para>
/// </remarks>
internal static class RolesAnswer
{
    private static readonly IReadOnlyList<string> MainVendors = ["claude", "codex", "agy"];

    /// <summary>The answer for <paramref name="env"/>. <paramref name="claudeHook"/> says the caller is Claude's SessionStart hook, which settles the main AI when nothing else does.</summary>
    internal static RolesResult Compute(Func<string, string?> env, string clientName, string clientVersion, bool claudeHook)
    {
        var notes = new List<string>();
        var problems = new List<RoleSettingProblem>();
        var vendor = MainVendor(env, clientName, claudeHook, notes, problems);
        var defaults = RoleDefaults.Current;
        var table = defaults.MainAi[vendor];
        var rows = ConfiguredRuntimes(env);

        var roles = table.Roles
            .Select(role => (role.Role, Models: (IReadOnlyList<ModelRef>)[.. role.Models.Where(model => model.WhenVendorRow.Length == 0 || rows.Contains(model.WhenVendorRow)).Select(model => new ModelRef(model.Model, model.Vendor))]))
            .ToList();
        var bands = (IReadOnlyList<Band>)table.Bands;
        var kinds = defaults.HaikuKinds;
        var method = defaults.WorkMethod;

        roles = RolesSettings.Models(env(RolesKeys.Models), roles, problems);
        bands = RolesSettings.Bands(env(RolesKeys.Bands), bands, problems);
        kinds = RolesSettings.HaikuKinds(env(RolesKeys.HaikuKinds), kinds, problems);
        method = RolesSettings.WorkMethodOf(env(RolesKeys.WorkMethod), method, problems);

        if (table.Note.Length > 0)
        {
            notes.Add(table.Note);
        }

        return new RolesResult(
            new MainAi(vendor, table.Cli, clientVersion),
            roles, bands, kinds, method, Revision(env), notes, problems);
    }

    private static string MainVendor(Func<string, string?> env, string clientName, bool claudeHook, List<string> notes, List<RoleSettingProblem> problems)
    {
        var set = env(RolesKeys.MainAi)?.Trim().ToLowerInvariant() ?? string.Empty;
        if (set.Length > 0)
        {
            if (MainVendors.Contains(set))
            {
                return set;
            }

            problems.Add(new RoleSettingProblem(RolesKeys.MainAi, $"{RolesKeys.MainAi} is '{set}', which is not one of {string.Join(", ", MainVendors)}; the main AI is found from the client instead."));
        }

        var name = clientName.ToLowerInvariant();
        if (name.Contains("claude"))
        {
            return "claude";
        }

        if (name.Contains("codex"))
        {
            return "codex";
        }

        if (name.Contains("antigravity") || name.Contains("agy") || name.Contains("gemini"))
        {
            return "agy";
        }

        if (claudeHook || Has(env, "CLAUDECODE") || Has(env, "CLAUDE_CODE_ENTRYPOINT"))
        {
            return "claude";
        }

        if (Has(env, "CODEX_THREAD_ID") || Has(env, "CODEX_SANDBOX"))
        {
            return "codex";
        }

        if (Has(env, "GEMINI_CLI"))
        {
            return "agy";
        }

        notes.Add($"the main AI could not be told from the client or the environment, so claude is assumed; set {RolesKeys.MainAi} to say which");

        return "claude";
    }

    private static bool Has(Func<string, string?> env, string name) => env(name) is { Length: > 0 };

    /// <summary>The runtimes of the person's enabled reviewer rows: the row's runtime, or its id when it names none (the rule the vendor rows are read by).</summary>
    private static HashSet<string> ConfiguredRuntimes(Func<string, string?> env)
    {
        var rows = PanelSettings.FromEnvironment(env).Providers
            .Where(provider => provider.Enabled)
            .Select(provider => (provider.Runtime.Length > 0 ? provider.Runtime : provider.Provider).ToLowerInvariant());

        return new HashSet<string>(rows, StringComparer.Ordinal);
    }

    /// <summary>A short, stable id of the settings the answer was made from: the same settings give the same id, any change to one of them a different one.</summary>
    private static string Revision(Func<string, string?> env)
    {
        var text = new StringBuilder();
        foreach (var key in RolesKeys.All)
        {
            if (env(key) is { Length: > 0 } value)
            {
                text.Append(key).Append('=').Append(value).Append('\n');
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..12];
    }

    // ---------- the answer as text ----------

    /// <summary>The answer as ONE JSON object.</summary>
    internal static string Json(RolesResult result)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, RolesJson.Options))
        {
            json.WriteStartObject();
            json.WriteStartObject("mainAi");
            json.WriteString("vendor", result.MainAi.Vendor);
            json.WriteString("cli", result.MainAi.Cli);
            json.WriteString("version", result.MainAi.Version);
            json.WriteEndObject();
            json.WriteStartObject("roles");
            foreach (var (role, models) in result.Roles)
            {
                json.WriteStartObject(role);
                json.WriteStartArray("models");
                foreach (var model in models)
                {
                    json.WriteStartObject();
                    json.WriteString("model", model.Model);
                    json.WriteString("vendor", model.Vendor);
                    json.WriteEndObject();
                }

                json.WriteEndArray();
                json.WriteEndObject();
            }

            json.WriteEndObject();
            json.WriteStartArray("complexityBands");
            foreach (var band in result.ComplexityBands)
            {
                json.WriteStartObject();
                json.WriteNumber("min", band.Min);
                json.WriteNumber("max", band.Max);
                json.WriteString("model", band.Model);
                json.WriteString("vendor", band.Vendor);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("haikuKinds");
            foreach (var kind in result.HaikuKinds)
            {
                json.WriteStringValue(kind);
            }

            json.WriteEndArray();
            json.WriteStartObject("workMethod");
            json.WriteStartObject("mvpEpics");
            json.WriteNumber("min", result.WorkMethod.MvpMin);
            json.WriteNumber("max", result.WorkMethod.MvpMax);
            json.WriteEndObject();
            json.WriteNumber("maxEpicsPerEpoch", result.WorkMethod.MaxEpicsPerEpoch);
            json.WriteNumber("maxStoriesPerEpic", result.WorkMethod.MaxStoriesPerEpic);
            json.WriteNumber("maxTasksPerStory", result.WorkMethod.MaxTasksPerStory);
            json.WriteNumber("minConsultsPerEpoch", result.WorkMethod.MinConsultsPerEpoch);
            json.WriteNumber("minConsultsPerEpic", result.WorkMethod.MinConsultsPerEpic);
            json.WriteEndObject();
            json.WriteString("settingsRevision", result.SettingsRevision);
            json.WriteStartArray("notes");
            foreach (var note in result.Notes)
            {
                json.WriteStringValue(note);
            }

            json.WriteEndArray();
            json.WriteStartArray("unrecognised");
            foreach (var problem in result.Unrecognised)
            {
                json.WriteStartObject();
                json.WriteString("key", problem.Key);
                json.WriteString("sentence", problem.Sentence);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Claude Code's SessionStart hook output: the role table and the work method as <c>additionalContext</c>, which Claude reads at the start of the session.</summary>
    internal static string HookJson(RolesResult result)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, RolesJson.Options))
        {
            json.WriteStartObject();
            json.WriteStartObject("hookSpecificOutput");
            json.WriteString("hookEventName", "SessionStart");
            json.WriteString("additionalContext", HookText(result));
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>The hook's text: a short role table, the bands, and the work-method sentence.</summary>
    internal static string HookText(RolesResult result)
    {
        var text = new StringBuilder();
        text.Append("coai roles (settings ").Append(result.SettingsRevision).Append("). Hand work to these models, in this order of failover:\n");
        foreach (var (role, models) in result.Roles)
        {
            text.Append("- ").Append(role).Append(": ").Append(models.Count == 0 ? "no model set" : string.Join(", then ", models.Select(model => model.Vendor == result.MainAi.Vendor ? model.Model : $"{model.Model} ({model.Vendor})"))).Append('\n');
        }

        if (result.ComplexityBands.Count > 0)
        {
            text.Append("Complexity bands: ").Append(string.Join("; ", result.ComplexityBands.Select(band => $"{band.Min}-{band.Max} {band.Model}"))).Append(".\n");
        }

        if (result.HaikuKinds.Count > 0)
        {
            text.Append("A small model may take: ").Append(string.Join(", ", result.HaikuKinds)).Append(".\n");
        }

        var method = result.WorkMethod;
        text.Append($"Work method: build the MVP in {method.MvpMin}-{method.MvpMax} epics first, then polish; at most {method.MaxEpicsPerEpoch} epics per epoch (more is a new epoch), at most {method.MaxStoriesPerEpic} stories per epic and {method.MaxTasksPerStory} tasks per story; at least {method.MinConsultsPerEpoch} consult per epoch and {method.MinConsultsPerEpic} per epic.\n");
        text.Append("Call the coai MCP tool `roles` for the full table.");

        return text.ToString();
    }
}
