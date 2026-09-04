using System.Text.Json.Nodes;

namespace CoaiBench.Running;

/// <summary>One vendor as the operator has actually configured it.</summary>
public sealed record VendorConfig(
    string Id,
    string Runtime = "",
    string Model = "",
    string BaseUrl = "",
    string ExecutablePath = "");

/// <summary>
/// The vendors the bench runs are the operator's own, never a list rebuilt from names.
/// </summary>
/// <remarks>
/// <para><b>This exists because a bench that invents its vendors measures a machine nobody has.</b>
/// The first campaign passed `COAI_PROVIDERS=codex,gemini,local` — bare ids — and the server did
/// exactly what it was told: it built a vendor called `gemini` on the RETIRED Gemini CLI, and a
/// local vendor with no model. Six of nine reviewers in a code round failed, and the report blamed
/// the release.</para>
/// <para>The operator's real configuration had been right for days: a vendor NAMED gemini whose
/// runtime is `antigravity`, and a local one with its model. An id is not a vendor — the runtime and
/// the model are the vendor, and the id is only what it is called.</para>
/// <para>So an arm names ids, and every id is looked up in the configuration. One that is not there
/// is refused by name rather than invented, because inventing it is the whole of the mistake.</para>
/// </remarks>
public static class Vendors
{
    /// <summary>Where the panel mirrors the settings the server reads.</summary>
    public static string DefaultSettingsFile =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "coai-mcp",
            "settings.json");

    public static IReadOnlyList<VendorConfig> Read(string settingsFile)
    {
        if (!File.Exists(settingsFile))
        {
            return [];
        }

        try
        {
            var settings = JsonNode.Parse(File.ReadAllText(settingsFile)) as JsonObject;
            // COAI_VENDORS is a STRING holding JSON, which is how the panel writes it.
            var vendors = settings?["COAI_VENDORS"]?.GetValue<string>() ?? string.Empty;

            return vendors.Length == 0 ? [] : [.. (JsonNode.Parse(vendors) as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(v => new VendorConfig(
                    Text(v, "id"), Text(v, "runtime"), Text(v, "model"),
                    Text(v, "baseUrl"), Text(v, "executablePath")))
                .Where(v => v.Id.Length > 0)];
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The arm's ids, as configured — or a sentence naming what could not be found.
    /// </summary>
    /// <param name="overrides">`--model vendor=model`, which is how local-against-hosted is asked.</param>
    public static (IReadOnlyList<VendorConfig> Vendors, string Refusal) For(
        string arm,
        IReadOnlyList<VendorConfig> configured,
        IReadOnlyDictionary<string, string> overrides)
    {
        var wanted = arm.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = new List<VendorConfig>();
        var missing = new List<string>();
        foreach (var id in wanted)
        {
            var found = configured.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                missing.Add(id);
                continue;
            }

            chosen.Add(overrides.TryGetValue(id, out var model) ? found with { Model = model } : found);
        }

        if (missing.Count > 0)
        {
            return ([], $"no vendor called {string.Join(", ", missing)} is configured — an id is not a "
                + "vendor, and inventing its runtime is how a bench ends up measuring a machine "
                + $"nobody has. Configured: {string.Join(", ", configured.Select(v => v.Id))}");
        }

        return (chosen, string.Empty);
    }

    /// <summary>The vendor list as the server reads it: one JSON string under `COAI_VENDORS`.</summary>
    public static string AsSetting(IReadOnlyList<VendorConfig> vendors)
    {
        var array = new JsonArray();
        foreach (var vendor in vendors)
        {
            array.Add(new JsonObject
            {
                ["id"] = vendor.Id,
                ["runtime"] = vendor.Runtime,
                ["model"] = vendor.Model,
                ["baseUrl"] = vendor.BaseUrl,
                ["executablePath"] = vendor.ExecutablePath,
                ["enabled"] = true,
            });
        }

        return array.ToJsonString();
    }

    private static string Text(JsonObject from, string name) =>
        from[name]?.GetValue<string>() ?? string.Empty;
}
