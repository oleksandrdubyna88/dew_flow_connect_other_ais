using System.Globalization;
using System.Text.Json.Nodes;
using CoaiBench.Model;

namespace CoaiBench.Running;

/// <summary>
/// Whether the settings a run ASKED for are the settings it actually got.
/// </summary>
/// <remarks>
/// <para>Asked for after a campaign produced numbers on `maxRounds 3, threshold 2, onExhausted
/// Human` while the operator's configuration said 1, 6 and good_enough — and nothing in the output
/// said so. Every table was about a machine nobody runs, and it looked exactly like a table about
/// the right one.</para>
/// <para>Only what is OBSERVABLE is checked, and it is checked against the two places that cannot
/// flatter it: the session file the server wrote, and the answer the round returned. A setting whose
/// effect cannot be seen from outside is reported as unchecked rather than as passing.</para>
/// </remarks>
public sealed record SettingsApplied(IReadOnlyList<string> Mismatches, IReadOnlyList<string> Checked)
{
    public bool Ok => Mismatches.Count == 0;

    public static readonly SettingsApplied Unchecked = new([], []);
}

public static class SettingsCheck
{
    /// <summary>
    /// What the asked-for settings should have produced, against what the run shows.
    /// </summary>
    /// <param name="asked">The effective settings handed to the server.</param>
    /// <param name="config">`state.config` out of the session file, or null when there is none.</param>
    /// <param name="stages">What the rounds answered — where the switches become visible.</param>
    public static SettingsApplied Of(
        IReadOnlyDictionary<string, string> asked,
        JsonObject? config,
        IReadOnlyList<StageResult> stages)
    {
        var mismatches = new List<string>();
        var examined = new List<string>();
        CheckGate(asked, config, mismatches, examined);
        CheckSwitches(asked, stages, mismatches, examined);

        return new SettingsApplied(mismatches, examined);
    }

    /// <summary>Rounds, thresholds and the exhausted policy — all of them in the session's own config.</summary>
    private static void CheckGate(
        IReadOnlyDictionary<string, string> asked,
        JsonObject? config,
        List<string> mismatches,
        List<string> examined)
    {
        if (config is null)
        {
            return; // nothing was written; the run's own result already says that
        }

        foreach (var role in (string[])["PlanCritique", "Architecture", "SecurityReliability", "UxDxPerformance"])
        {
            var gate = config["roles"]?[role] as JsonObject;
            Compare(asked, $"COAI_ROUNDS_{role.ToUpperInvariant()}", gate?["maxRounds"], role + " rounds", mismatches, examined);
            Compare(asked, $"COAI_THRESHOLD_{role.ToUpperInvariant()}", gate?["threshold"], role + " threshold", mismatches, examined);
        }

        if (asked.TryGetValue("COAI_ON_EXHAUSTED", out var policy))
        {
            examined.Add("on-exhausted");
            var seen = config["onExhausted"]?.GetValue<string>() ?? string.Empty;
            // `good_enough` in the file, `GoodEnough` in the state: the same decision, spelled twice.
            if (!seen.Replace("_", "", StringComparison.Ordinal)
                .Equals(policy.Replace("_", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add($"on-exhausted asked '{policy}', session says '{seen}'");
            }
        }
    }

    /// <summary>The three switches, which are visible as the ORDERS a passing plan round hands back.</summary>
    private static void CheckSwitches(
        IReadOnlyDictionary<string, string> asked,
        IReadOnlyList<StageResult> stages,
        List<string> mismatches,
        List<string> examined)
    {
        // Only a plan round that PASSED carries them: the order to build follows permission to build,
        // so a stage that never passed proves nothing either way.
        var passed = stages.FirstOrDefault(s =>
            s.Stage.StartsWith("plan", StringComparison.Ordinal) && RoundRunner.Passed(s.Verdict));
        if (passed is null)
        {
            return;
        }

        foreach (var (key, word) in ((string Key, string Word)[])
            [("COAI_AUTONOMOUS", "AUTONOMOUSLY"), ("COAI_SPLIT_PLAN", "story"), ("COAI_SPLIT_WITH_FABLE", "Fable")])
        {
            if (!asked.TryGetValue(key, out var value) || !value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            examined.Add(key);
            if (!passed.Commands.Any(c => c.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                mismatches.Add($"{key} is on, but the round handed back no order mentioning '{word}'");
            }
        }
    }

    private static void Compare(
        IReadOnlyDictionary<string, string> asked,
        string key,
        JsonNode? seen,
        string what,
        List<string> mismatches,
        List<string> examined)
    {
        if (!asked.TryGetValue(key, out var value)
            || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wanted))
        {
            return;
        }

        examined.Add(what);
        var got = seen?.GetValue<int>();
        if (got != wanted)
        {
            mismatches.Add($"{what} asked {wanted}, session says {(got?.ToString(CultureInfo.InvariantCulture) ?? "nothing")}");
        }
    }
}
