using CoaiMcp.Core.Catalog;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A claude row's fast mode, as the CLI is told it (research/PLAN_fast_mode.md): <c>--settings &lt;file&gt;</c>, the file
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
    /// <summary>
    /// The arguments for <paramref name="settings"/>' fast mode — none for "As the CLI is set", for a model without the
    /// tier, and for a launch with no data folder of its own: a shared temp folder is a path another local user can plant
    /// first (the code round), so such a launch sends no flag rather than read a file somebody else controls.
    /// </summary>
    public static IReadOnlyList<string> Args(ReviewerSettings settings) =>
        settings.Fast == FastMode.Cli || settings.DataDir.Length == 0 || !HasTheTier(settings.Model)
            ? []
            : ["--settings", FileFor(settings.DataDir, settings.Fast == FastMode.On)];

    /// <summary>Whether <paramref name="model"/> has a fast tier — read from shared/feature-availability.json, the list the card reads too.</summary>
    public static bool HasTheTier(string model) => FeatureAvailability.Builtin.HasFastTier("claude", model);

    /// <summary>The one-key file for the state, under the data folder — written only when it is missing or says something else.</summary>
    private static string FileFor(string dataDir, bool on)
    {
        var path = Path.Combine(dataDir, "fast-mode", on ? "fast-on.json" : "fast-off.json");
        WriteIfDifferent(path, on ? "{\"fastMode\":true}" : "{\"fastMode\":false}");

        return path;
    }

    /// <summary>
    /// Through a temporary file of this write's OWN — a name shared by every thread of the process made one parallel launch
    /// fail on another's move (the code round) — moved into place; a move refused because a sibling is replacing the file
    /// with the same text is no failure.
    /// </summary>
    private static void WriteIfDifferent(string path, string text)
    {
        if (Says(path, text))
        {
            return;
        }

        // One writer at a time in this process: on Windows a file another thread is replacing this instant refuses both
        // the move and the read (ManyLaunchesAtOnce_AllGetTheFile_NoneFails). Writes are rare — only when the file
        // differs — so the lock costs nothing; another PROCESS is met by the tolerant move in Replace.
        lock (WriteLock)
        {
            Replace(path, text);
        }
    }

    private static readonly Lock WriteLock = new();

    private static void Replace(string path, string text)
    {
        if (Says(path, text))
        {
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, text);
        try
        {
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && Says(path, text))
        {
            File.Delete(temporary);
        }
    }

    /// <summary>Whether the file is there and says exactly this — a file being replaced this instant reads as not yet.</summary>
    private static bool Says(string path, string text)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path) == text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
