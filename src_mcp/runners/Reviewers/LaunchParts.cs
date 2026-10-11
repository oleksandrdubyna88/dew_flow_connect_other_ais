using CoaiMcp.Core.Findings;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// What one paid launch WAS inside its reviewer turn, as the ledger spells it (story 3.3 of
/// todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <remarks>
/// <para>Here, in the library both halves compile against, for the reason <see cref="UsageKinds"/> lives here: the
/// local executor and the Team server both write these words into one ledger, and two spellings of one field is how a
/// writer and a reader come to disagree about what a line means.</para>
/// <para><b>Why the ledger needs them at all.</b> A ledger line used to be one terminal TURN: a repair, a follow-up and
/// every step of the rate-limit ladder were folded into the line of the turn they belonged to, so "how many retries,
/// repairs and follow-ups did this week take" could only be inferred from sums. The cadence consultation for epics 1–3
/// found it, and the paid-path census (`research/RESULTS_vendor_quota_gauges.md` §5) listed every fold; one line per
/// launch, each naming what it was and sharing its turn's id, is what makes them countable.</para>
/// </remarks>
public static class LaunchAttempts
{
    /// <summary>A turn's first launch — and the only one of a turn that needed no other.</summary>
    public const string First = "first";

    /// <summary>A fresh launch of the same question after the first answered something that would not parse.</summary>
    public const string Repair = "repair";

    /// <summary>The adapter's continuation of the first launch in the SAME conversation (agy's auto-denied command, #504/#515).</summary>
    public const string FollowUp = "followUp";

    /// <summary>A step of the rate-limit ladder: the turn launched again after a refusal that was worth waiting out.</summary>
    public const string Retry = "retry";

    /// <summary>
    /// A Team-server attempt of a job that took more than one account: the attempt a rate-limited slot handed back to
    /// the queue, and the attempt that finally ran it.
    /// </summary>
    public const string Rotated = "rotated";

    /// <summary>Every attempt this build names, in the order a turn meets them.</summary>
    public static IReadOnlyList<string> Known { get; } = [First, Repair, FollowUp, Retry, Rotated];
}

/// <summary>One paid launch of a reviewer turn: what it was, what its vendor reported, how long it ran, how it ended.</summary>
/// <param name="Attempt">One of <see cref="LaunchAttempts"/>.</param>
/// <param name="Usage">What THIS launch reported — never the turn's running sum.</param>
/// <param name="Elapsed">How long this launch ran, as its builder measured it.</param>
/// <param name="Outcome">How this launch ended, in the ledger's words: <c>ok</c>, or the failure's own sentence.</param>
/// <remarks>
/// The LAST part of a turn is the launch the turn's outcome itself describes, and its words and seconds are written by
/// whoever records the turn (<see cref="LaunchLines.Of"/>): the outcome can still change after the launch — the ladder
/// stamps its attempt count on a refusal, the security answer limit turns an answer into a refusal, the Team server
/// maps a verdict to its failure kind — and the lines' seconds must still add up to the turn's.
/// </remarks>
public sealed record LaunchPart(string Attempt, Usage Usage, TimeSpan Elapsed, string Outcome);

/// <summary>How one turn's launches become ledger lines — the one rule both halves write by.</summary>
public static class LaunchLines
{
    /// <summary>How many hex characters of a GUID a local turn id keeps.</summary>
    private const int TurnLength = 12;

    /// <summary>The ledger's word for a launch that answered.</summary>
    public const string Answered = "ok";

    /// <summary>A fresh id for one local turn — twelve hex characters, unique among the lines it is written beside.</summary>
    public static string NewTurn() => Guid.NewGuid().ToString("N")[..TurnLength];

    /// <summary>How an outcome ends in the ledger: <c>ok</c>, or its failure WHOLE — never the summary's clipped sentence.</summary>
    public static string OutcomeOf(ReviewerOutcome outcome) =>
        outcome is ReviewerOutcome.Ok ? Answered : ReviewerSummaryFactory.DescribeWhole(outcome);

    /// <summary>
    /// The lines one turn is written as: one per launch when the turn took several, else one.
    /// </summary>
    /// <param name="parts">Every launch of the turn, in order — empty when it took one.</param>
    /// <param name="whole">What the turn consumed, every launch of it — what its one folded line used to say.</param>
    /// <param name="elapsed">The turn's seconds; the last line gets what the earlier launches did not take.</param>
    /// <param name="outcome">The turn's ending, which is the last line's.</param>
    /// <param name="firstAttempt">What the turn's first launch is called — <c>first</c>, or <c>rotated</c> on a Team server.</param>
    /// <remarks>
    /// <para><b>Parts that do not add up to the turn are not trusted.</b> The round total, the audit line and every
    /// figure on the panel read the turn's own sum (<see cref="ReviewerOutcome.LastTurnUsage"/>), and a ledger whose lines
    /// added up to something else would be a second answer to "what did this cost". So when the launches' usage does
    /// not sum to the turn's exactly, the turn is written as the one line it always was — the old record, never a
    /// double one — and a test holds the split to the sum for every path that builds parts.</para>
    /// </remarks>
    public static IReadOnlyList<LaunchPart> Of(
        IReadOnlyList<LaunchPart> parts, Usage whole, TimeSpan elapsed, string outcome, string firstAttempt = LaunchAttempts.First) =>
        parts.Count > 1 && AddsUpTo(parts, whole)
            ? Split(parts, elapsed, outcome, firstAttempt)
            : [new LaunchPart(firstAttempt, whole, elapsed, outcome)];

    /// <summary>
    /// The launches of one step of the rate-limit ladder: the step's own parts when it took several, else the step as
    /// one launch — its first launch named <paramref name="opening"/> (<c>first</c>, or <c>retry</c> after a wait).
    /// </summary>
    public static IReadOnlyList<LaunchPart> StepOf(ReviewerOutcome step, TimeSpan took, string opening) =>
        step.Launches.Count == 0
            ? [new LaunchPart(opening, step.LastTurnUsage, took, OutcomeOf(step))]
            : [step.Launches[0] with { Attempt = opening }, .. step.Launches.Skip(1)];

    private static IReadOnlyList<LaunchPart> Split(
        IReadOnlyList<LaunchPart> parts, TimeSpan elapsed, string outcome, string firstAttempt)
    {
        var earlier = parts.Take(parts.Count - 1).Select((part, at) => at == 0 ? Opening(part, firstAttempt) : part).ToList();
        var left = elapsed - TimeSpan.FromTicks(earlier.Sum(part => part.Elapsed.Ticks));

        return [.. earlier, parts[^1] with { Elapsed = left < TimeSpan.Zero ? TimeSpan.Zero : left, Outcome = outcome }];
    }

    /// <summary>The turn's first launch, renamed only when the caller calls first launches something else.</summary>
    private static LaunchPart Opening(LaunchPart part, string firstAttempt) =>
        part.Attempt == LaunchAttempts.First ? part with { Attempt = firstAttempt } : part;

    /// <summary>Whether the launches' usage sums to the turn's — every count and both markers, and the money to a hair.</summary>
    internal static bool AddsUpTo(IReadOnlyList<LaunchPart> parts, Usage whole)
    {
        var sum = parts.Aggregate(Usage.None, (total, part) => total.Add(part.Usage));

        return sum with { CostUsd = null } == whole with { CostUsd = null } && SameMoney(sum.CostUsd, whole.CostUsd);
    }

    /// <summary>Two sums of reported money: both absent, or the same to a millionth of a cent — the order of addition is not.</summary>
    private static bool SameMoney(double? one, double? other) => (one, other) switch
    {
        (null, null) => true,
        ({ } a, { } b) => Math.Abs(a - b) < 1e-8,
        _ => false,
    };
}
