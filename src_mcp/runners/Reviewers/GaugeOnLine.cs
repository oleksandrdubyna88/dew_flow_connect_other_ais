namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The vendor's own gauge as a ledger line carries it: the reading taken on the account right after the launch,
/// flattened to the preferred limit's two windows (story 6.1a of todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <param name="ObservedUtc">When the reading arrived, UTC (<c>O</c>) — empty for no reading.</param>
/// <param name="PlanType">The vendor's plan word, or empty.</param>
/// <param name="LimitId">The limit the two windows belong to (<c>codex</c>), or empty.</param>
/// <param name="PrimaryMins">The primary window's length — null when there is no reading or the vendor did not say.</param>
/// <param name="PrimaryUsedPercent">The primary window's use — null, never 0, when it was not reported.</param>
/// <param name="PrimaryResetsUtc">When the primary window resets, UTC (<c>O</c>) — empty when not reported.</param>
/// <param name="SecondaryMins">The secondary window's length — null when the plan reports none (the measured plan reports none).</param>
/// <param name="SecondaryUsedPercent">The secondary window's use — null when the plan reports none.</param>
/// <param name="SecondaryResetsUtc">When the secondary window resets — empty when the plan reports none.</param>
/// <remarks>
/// <para>Every field is "not reported" by default: a line written by a local run, by a server older than the field, or
/// after a capture that produced nothing says NOTHING about the gauge — never a 0 %, which would read as an empty
/// window. The Team server writes it from the slot's snapshot; the extension's parser reads each field only when the
/// line says it.</para>
/// <para>Here, in the library both halves compile against, because <c>UsageLedger</c> writes it and the server builds
/// it — the shape <c>LaunchOptions</c> and <c>EndCauses</c> already take.</para>
/// </remarks>
public sealed record GaugeOnLine(
    string ObservedUtc,
    string PlanType,
    string LimitId,
    long? PrimaryMins,
    double? PrimaryUsedPercent,
    string PrimaryResetsUtc,
    long? SecondaryMins,
    double? SecondaryUsedPercent,
    string SecondaryResetsUtc)
{
    /// <summary>No reading on this line: a local run, an older server, or a capture that produced nothing.</summary>
    public static readonly GaugeOnLine None =
        new(string.Empty, string.Empty, string.Empty, null, null, string.Empty, null, null, string.Empty);
}
