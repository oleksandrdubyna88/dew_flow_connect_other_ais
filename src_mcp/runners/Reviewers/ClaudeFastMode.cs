using CoaiMcp.Core.Catalog;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A claude row's fast mode, as the CLI is told it (todo/PLAN_fast_mode.md): <c>--settings &lt;file&gt;</c>, the file
/// holding exactly ONE key, <c>fastMode</c>. The documented headless spelling (code.claude.com/docs/en/fast-mode.md:
/// "<c>claude -p --settings '{"fastMode": true}'</c>", v2.1.205+), given as a PATH: an inline JSON argument carries double
/// quotes, which an npm <c>.cmd</c> shim re-tokenises. <c>--settings</c> loads ADDITIONAL settings merged over the
/// person's (help 2.1.258), and applies under <c>--restricted</c> too, so nothing else changes.
/// </summary>
/// <remarks>
/// Only the models <c>shared/feature-availability.json</c> lists have a fast tier (the Opus family, alias and <c>[1m]</c>
/// suffix included). Any other model, and an empty
/// one (the CLI picks its own default, which may not have the tier), is sent nothing in EITHER state. Measured
/// 2026-10-07 (research/RESULTS_fast_mode_measured_2026-10-07.md): Off reports <c>fast_mode_state: off</c>; On is held
/// off on an account whose own preference does not allow it, which the run reports, not this code.
/// </remarks>
public static class ClaudeFastMode
{
    /// <summary>The arguments for <paramref name="settings"/>' fast mode — none for "As the CLI is set" or a model without the tier.</summary>
    public static IReadOnlyList<string> Args(ReviewerSettings settings) =>
        settings.Fast == FastMode.Cli || !HasTheTier(settings.Model) ? [] : ["--settings", FileFor(settings.DataDir, settings.Fast == FastMode.On)];

    /// <summary>Whether <paramref name="model"/> has a fast tier — read from shared/feature-availability.json, the list the card reads too.</summary>
    public static bool HasTheTier(string model) => FeatureAvailability.Builtin.HasFastTier("claude", model);

    /// <summary>
    /// The one-key file for the state, under the data folder — written only when it is missing or says something else
    /// (several processes start launches), through a temporary file moved into place, so no launch reads half of it.
    /// </summary>
    private static string FileFor(string dataDir, bool on)
    {
        var dir = Path.Combine(dataDir.Length > 0 ? dataDir : Path.GetTempPath(), "fast-mode");
        var path = Path.Combine(dir, on ? "fast-on.json" : "fast-off.json");
        var text = on ? "{\"fastMode\":true}" : "{\"fastMode\":false}";
        if (!File.Exists(path) || File.ReadAllText(path) != text)
        {
            Directory.CreateDirectory(dir);
            var temporary = $"{path}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }

        return path;
    }
}
