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
            if (PolicyMismatch(policy, config["onExhausted"]?.GetValue<string>() ?? string.Empty, config) is { } mismatch)
            {
                mismatches.Add(mismatch);
            }
        }
    }

    /// <summary>
    /// The asked policy against what the session stored: the word AND the session's <c>autonomous</c> member — or null when they agree.
    /// </summary>
    /// <remarks>
    /// Policy auto is stored as <c>GoodEnough</c> with <c>autonomous: true</c>; a named <c>good_enough</c> is the same word with <c>false</c>
    /// (or no member, from a server before policy auto). The word alone cannot tell them apart, so asked <c>auto</c> is satisfied only by
    /// the marker, and any other asked word is contradicted by <c>autonomous: true</c>.
    /// </remarks>
    private static string? PolicyMismatch(string asked, string seen, JsonObject config)
    {
        var autonomous = config["autonomous"] is JsonValue marker && marker.TryGetValue<bool>(out var value) ? value : (bool?)null;
        var markerWords = autonomous switch
        {
            true => " with autonomous true",
            false => " with autonomous false",
            _ => " with no autonomous member",
        };
        // `good_enough` in the file, `GoodEnough` in the state: the same decision, spelled twice.
        static string Plain(string word) => word.Replace("_", "", StringComparison.Ordinal);
        var disagrees = asked.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? !Plain(seen).Equals("GoodEnough", StringComparison.OrdinalIgnoreCase) || autonomous != true
            : !Plain(seen).Equals(Plain(asked), StringComparison.OrdinalIgnoreCase) || autonomous == true;

        return disagrees
            ? $"on-exhausted asked '{asked}', session says '{seen}'"
                + (asked.Equals("auto", StringComparison.OrdinalIgnoreCase) || autonomous == true ? markerWords : string.Empty)
            : null;
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

    // The words every split order of a first plan round carries, whatever size it names — the one
    // phrase the "build it as it stands" order shares with the others (issue #131). The server's copy
    // was `GateCommands.GateOrderMarker`; v2 removed the orders and the shared file that held both copies, so this copy now only
    // recognises the orders of a PUBLISHED server older than v2. An older
    // server's order has none, which is why "Split this plan into" still counts beside it.
    internal const string SplitOrder = "THE GATE runs once";
    private const string AlreadySplit = "already under way";
    // The words the model order OPENS with, whichever models it names. The server's copy is
    // `GateCommands.ModelOrderMarker` (removed in v2, with `shared/command-models.json`); the bench references nothing of the
    // server and keeps its copy for the published servers that still order it. It was "Fable" until issue
    // #117 made the models a per-caller choice, and a Codex round would have read as a dead switch.
    internal const string ModelOrder = "Do the SPLIT itself with ";

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
            said(SplitOrder) || said(OrdersASplit) || said(AlreadySplit),
            "an order about splitting the plan",
            mismatches,
            examined);

        // The model order rides on the split order and cannot appear without it. When the round said
        // "already split", its absence is not evidence about the switch — and an instrument reports
        // the absence of evidence as unchecked, never as a failure.
        if (said(SplitOrder) || said(OrdersASplit))
        {
            Expect(
                asked, "COAI_SPLIT_WITH_FABLE", said(ModelOrder), "an order naming the strongest model", mismatches, examined);
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
