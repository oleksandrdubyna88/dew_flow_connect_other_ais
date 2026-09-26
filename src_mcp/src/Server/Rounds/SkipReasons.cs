using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// Why a round that may be SKIPPED had nobody to ask — the skip rows of the feature stage's truth
/// table (§4.4 of the feature-review plan), each as the sentence a person acts on.
/// </summary>
/// <remarks>
/// Pure, like <see cref="RoundRefusals"/>, and for the same reason: it reads the catalog and the
/// counts the engine hands it, and produces prose. The three rows are decided in order — the role
/// switch, then the vendor ticks, then whether any ticked vendor can run — because each is the
/// thing a person has to change before the next one matters.
/// </remarks>
internal static class SkipReasons
{
    /// <param name="rolesOn">How many of the stage's roles are switched on.</param>
    /// <param name="vendorsTicked">How many vendors serve the stage at all.</param>
    /// <param name="cannotRun">The ticked vendors this round could not run, each as <c>name: reason</c>.</param>
    /// <param name="work">What the roster came back with: nothing, and who it decided not to ask.</param>
    internal static string For(
        Stage stage,
        RoleCatalog catalog,
        int rolesOn,
        int vendorsTicked,
        IReadOnlyList<string> cannotRun,
        RoundWork work)
    {
        var phrase = Stages.Of(stage).Phrase;
        if (rolesOn == 0)
        {
            return $"the {phrase} is switched off — every {Stages.Of(stage).Kind}-review role is unticked; "
                + $"tick {new RoundRefusals(catalog).Named(new RoundRefusals(catalog).Tickable(stage))} in the panel to run it";
        }

        if (vendorsTicked == 0)
        {
            return $"no vendor is ticked for the {phrase} — tick it on a vendor's card in the panel to run it";
        }

        return cannotRun.Count == vendorsTicked
            ? $"no ticked vendor can run the {phrase}: {string.Join("; ", cannotRun)}"
            : Otherwise(phrase, cannotRun, work);
    }

    /// <summary>
    /// The D17 row: a plan too small for the feature gate — decided from the <c>epics</c> count, before any
    /// git or model work (§4.4). Said as the plan says it, with the setting that moves the line.
    /// </summary>
    internal static string TooFewEpics(int epics, int minimum) =>
        $"a plan of {epics} epic(s) is covered by review_code; the feature gate runs for {minimum} or more "
        + "(COAI_FEATURE_MIN_EPICS)";

    /// <summary>A roster that came back empty for a reason the three rows do not name — said whole.</summary>
    private static string Otherwise(string phrase, IReadOnlyList<string> cannotRun, RoundWork work) =>
        $"nothing could review the {phrase}"
        + RoundRefusals.Clause(": ", cannotRun)
        + RoundRefusals.Clause(" Before that, ", work.NotAsked.Select(r => $"{r.Role} was not asked: {r.Reason}"))
        + RoundRefusals.Clause(" And ", work.Excluded.Select(e => $"{e.Role} could not go to {e.Provider}: {e.Reason}"));
}
