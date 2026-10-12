namespace CoaiMcp.Runners.Reviewers;

/// <summary>One reported window of one limit, as a ledger line carries it — identified by the limit and the length.</summary>
/// <param name="LimitId">The vendor's limit identity (<c>codex</c>, or another id <c>rateLimitsByLimitId</c> named).</param>
/// <param name="Kind"><c>primary</c> or <c>secondary</c> — where the vendor filed it, kept for the record and never used to identify it.</param>
/// <param name="Mins">The window's length — null when the vendor did not say, never 0.</param>
/// <param name="UsedPercent">Of the window, as reported — null when the vendor did not report it, never 0.</param>
/// <param name="ResetsUtc">When the window resets, UTC (<c>O</c>) — empty when the vendor did not say.</param>
public sealed record GaugeWindowOnLine(string LimitId, string Kind, long? Mins, double? UsedPercent, string ResetsUtc);

/// <summary>
/// The vendor's own gauge as a ledger line carries it: the reading taken on the account right after the launch, EVERY
/// window of EVERY limit it reported (story 6.1a of todo/PLAN_every_round_is_counted.md; widened by epic 6's code round).
/// </summary>
/// <param name="ObservedUtc">When the reading arrived, UTC (<c>O</c>) — empty for no reading.</param>
/// <param name="PlanType">The vendor's plan word, or empty.</param>
/// <param name="Windows">Every window the reading reported, each with its limit — empty for no reading, never a 0 %.</param>
/// <remarks>
/// <para>A COLLECTION, not one limit's two flat windows: codex answers <c>rateLimitsByLimitId</c> with several limits,
/// the slot keeps them all, and a line that kept only the preferred one would hide every other limit from
/// <see cref="RoundsPerPercent"/>. Epic 6's code round found it before anything shipped, so the shape changed rather than
/// gaining a second one beside it.</para>
/// <para>Every field is "not reported" by default: a line written by a local run, by a server older than the field, or
/// after a capture that produced nothing says NOTHING about the gauge. The Team server writes it from the slot's
/// snapshot; the extension's parser reads each window only when it is whole.</para>
/// <para>Here, in the library both halves compile against, because <c>UsageLedger</c> writes it and the server builds
/// it — the shape <c>LaunchOptions</c> and <c>EndCauses</c> already take.</para>
/// </remarks>
public sealed record GaugeOnLine(string ObservedUtc, string PlanType, IReadOnlyList<GaugeWindowOnLine> Windows = null!)
{
    /// <summary>Never null, whatever a deserialiser or a caller did with the property.</summary>
    public IReadOnlyList<GaugeWindowOnLine> Windows { get; init; } = Windows ?? [];

    /// <summary>No reading on this line: a local run, an older server, or a capture that produced nothing.</summary>
    public static readonly GaugeOnLine None = new(string.Empty, string.Empty, []);

    /// <summary>Value equality over the windows too — a record compares a list by reference otherwise.</summary>
    public bool Equals(GaugeOnLine? other) =>
        other is not null
        && ObservedUtc == other.ObservedUtc
        && PlanType == other.PlanType
        && Windows.SequenceEqual(other.Windows);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(ObservedUtc, PlanType, Windows.Count);
}
