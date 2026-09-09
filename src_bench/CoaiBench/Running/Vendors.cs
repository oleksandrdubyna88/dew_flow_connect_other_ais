using System.Text.Json.Nodes;

namespace CoaiBench.Running;

/// <summary>One vendor as the operator has actually configured it.</summary>
public static class VendorRuntimes
{
    /// <summary>The runtime word that means "a Team server answers this", as the panel writes it.</summary>
    public const string Remote = "remote";
}

public sealed record VendorConfig(
    string Id,
    string Runtime = "",
    string Model = "",
    string BaseUrl = "",
    string ExecutablePath = "")
{
    /// <summary>
    /// What a Team server calls this vendor — which is NOT the row's id.
    /// </summary>
    /// <remarks>
    /// <para>A row is named <c>&lt;server&gt;-&lt;vendor&gt;</c> so that two Team servers each
    /// offering <c>codex</c> do not collide on one id. The server has only ever heard of
    /// <c>codex</c>, so sending it the row id is refused with a 400 — <i>"'remsoftdev-claude' is not
    /// a vendor here"</i>.</para>
    /// <para>Measured rather than reasoned about: a campaign of twelve plan rounds produced twelve
    /// `call_human` verdicts in seven seconds each and zero tokens, with that sentence repeated
    /// thirty-six times. The bench could not measure a Team server at all. It is the same field, the
    /// same failure and the same discovery route as the extension's own version of this bug.</para>
    /// </remarks>
    public string RemoteVendor { get; init; } = string.Empty;
}

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
                    Text(v, "baseUrl"), Text(v, "executablePath"))
                {
                    RemoteVendor = Text(v, "remoteVendor"),
                })
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
            var row = new JsonObject
            {
                ["id"] = vendor.Id,
                ["runtime"] = vendor.Runtime,
                ["model"] = vendor.Model,
                ["baseUrl"] = vendor.BaseUrl,
                ["executablePath"] = vendor.ExecutablePath,
                ["enabled"] = true,
            };
            // Written only for a REMOTE row that has one, exactly as the panel writes it. The
            // length test alone was not enough: `Read` preserves whatever a settings file carries,
            // so a local row left holding a stale value from when it was remote would have had the
            // field written back out — the same shape of silent mismatch this whole change exists to
            // end, pointing the other way. (CodeRabbit, on the pull request.)
            if (vendor.Runtime == VendorRuntimes.Remote && vendor.RemoteVendor.Length > 0)
            {
                row["remoteVendor"] = vendor.RemoteVendor;
            }

            array.Add(row);
        }

        return array.ToJsonString();
    }

    private static string Text(JsonObject from, string name) =>
        from[name]?.GetValue<string>() ?? string.Empty;
}
