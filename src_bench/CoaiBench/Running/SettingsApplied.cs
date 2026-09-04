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

    /// <summary>Phrases that belong to exactly ONE order, so finding one proves which order it was.</summary>
    /// <remarks>
    /// A word is not a check. <c>COAI_SPLIT_PLAN</c> was looked for as the word "story", which also
    /// appears in the autonomy order — "re-read every epic and story you have written so far" — so
    /// the switch was reported working in runs where no split order was given at all. Every phrase
    /// here is quoted from the one command that can produce it.
    /// </remarks>
    private const string Autonomy = "Work AUTONOMOUSLY";
    private const string OrdersASplit = "Split this plan into";
    private const string AlreadySplit = "already under way";
    private const string WithFable = "Fable";

    /// <summary>The three switches, which are visible as the ORDERS a passing plan round hands back.</summary>
    /// <remarks>
    /// The split order is given ONCE per calling session — the floor under epics-of-epics — so a
    /// round can legitimately answer with the order NOT to split again. That order exists only
    /// because the switch is on, and reading it as a failure is how three runs of four came back
    /// marked SETTINGS NOT APPLIED on 2026-09-04 while the feature worked.
    /// </remarks>
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

        var said = (string phrase) => passed.Commands.Any(c => c.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        Expect(asked, "COAI_AUTONOMOUS", said(Autonomy), "an order to work autonomously", mismatches, examined);
        Expect(
            asked,
            "COAI_SPLIT_PLAN",
            said(OrdersASplit) || said(AlreadySplit),
            "an order about splitting the plan",
            mismatches,
            examined);

        // Fable's order rides on the split order and cannot appear without it. When the round said
        // "already split", its absence is not evidence about the switch — and an instrument reports
        // the absence of evidence as unchecked, never as a failure.
        if (said(OrdersASplit))
        {
            Expect(asked, "COAI_SPLIT_WITH_FABLE", said(WithFable), "an order naming Fable", mismatches, examined);
        }
    }

    /// <summary>A switch that is on must show its effect; one that is off is not this check's business.</summary>
    private static void Expect(
        IReadOnlyDictionary<string, string> asked,
        string key,
        bool seen,
        string what,
        List<string> mismatches,
        List<string> examined)
    {
        if (!asked.TryGetValue(key, out var value) || !value.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        examined.Add(key);
        if (!seen)
        {
            mismatches.Add($"{key} is on, but the round handed back no order that is {what}");
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
