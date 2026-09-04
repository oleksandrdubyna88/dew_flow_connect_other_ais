using System.Text.Json.Nodes;

namespace CoaiBench.Running;

/// <summary>
/// The operator's settings, ALL of them, as the server itself reads them.
/// </summary>
/// <remarks>
/// <para>The bench used to take only the vendors and leave everything else to the server's defaults
/// — so a measured round ran on default thresholds, default rounds per role, default prompts and a
/// default exhausted-policy, while the machine it claimed to describe runs on the operator's. Every
/// number produced that way is about a configuration nobody uses.</para>
/// <para>The same mistake as rebuilding vendors from ids, one level up: a bench that supplies its own
/// environment measures a machine nobody has. So the whole file is passed through, `--set` overrides
/// on top, and the effective set is printed and RECORDED with the run — a campaign has to be able to
/// say what it was measuring, months later, without anybody remembering.</para>
/// </remarks>
public static class PanelSettingsFile
{
    /// <summary>Every `COAI_*` the panel wrote. Empty when there is no file to read.</summary>
    public static IReadOnlyDictionary<string, string> Read(string settingsFile)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(settingsFile))
        {
            return settings;
        }

        try
        {
            foreach (var (key, value) in JsonNode.Parse(File.ReadAllText(settingsFile)) as JsonObject ?? [])
            {
                // Only the server's own vocabulary: anything else in that file is not ours to pass on.
                if (key.StartsWith("COAI_", StringComparison.Ordinal) && value is not null)
                {
                    settings[key] = value.ToString();
                }
            }
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return settings;
    }

    /// <summary>
    /// What a server is actually given: the operator's file, then this run's overrides.
    /// </summary>
    /// <remarks>
    /// The order is the whole point. The file is the baseline so the measurement is of the real
    /// machine; `--set` wins so a settings COMBINATION can still be asked for, which is the other
    /// run that kept being written by hand.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Effective(
        IReadOnlyDictionary<string, string> fromFile,
        IReadOnlyDictionary<string, string> overrides)
    {
        var effective = new Dictionary<string, string>(fromFile, StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
        {
            effective[key] = value;
        }

        return effective;
    }

    /// <summary>
    /// The settings as one readable block — printed at the start and kept with the run.
    /// </summary>
    /// <remarks>
    /// `COAI_VENDORS` is shown as a summary rather than its JSON: a line nobody can read is a line
    /// nobody checks, and the vendors are already listed above it by id, runtime and model.
    /// </remarks>
    public static string Describe(IReadOnlyDictionary<string, string> settings) =>
        string.Join(
            "\n",
            settings.OrderBy(s => s.Key, StringComparer.Ordinal)
                .Select(s => $"  {s.Key} = {Short(s.Key, s.Value)}"));

    private static string Short(string key, string value) =>
        key == "COAI_VENDORS" || value.Length <= 120 ? Summarised(key, value) : value[..117] + "…";

    private static string Summarised(string key, string value) =>
        key == "COAI_VENDORS" ? $"<{Count(value)} vendor(s), listed above>" : value;

    private static int Count(string vendors)
    {
        try
        {
            return (JsonNode.Parse(vendors) as JsonArray)?.Count ?? 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return 0;
        }
    }
}
