using System.Globalization;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// How long a rate-limited reviewer waits before trying again, and when it stops trying.
/// </summary>
/// <remarks>
/// <para>It replaces a single retry at a single interval. Fifteen seconds was the wrong one number
/// for what vendors actually do: codex and claude meter a five-hour rolling window plus a weekly
/// cap and say so in words ("You've hit your usage limit", "resets 3:45pm"), antigravity meters a
/// per-plan quota, and a plain <c>429 Too Many Requests</c> or <c>503 ... high demand</c> clears in
/// seconds to a couple of minutes. One interval either gives up on the transient case too early or,
/// raised to cover it, makes every rate-limited reviewer of a round wait that long for nothing.</para>
///
/// <para><b>The jitter is not decoration.</b> A code round launches nine reviewers at once, so nine
/// reviewers meet the same limit in the same instant — and with a fixed interval they would all
/// retry in the same instant too, which is a synchronised second wave into the limit they just hit.
/// A fifth either way spreads them.</para>
///
/// <para>Everything here is pure, and the jitter's roll is a PARAMETER rather than an internal
/// <see cref="Random"/>: a wait cannot be asserted by sleeping through it without the assertion
/// becoming a stopwatch on a loaded runner.</para>
/// </remarks>
public static class RetryLadder
{
    /// <summary>Five seconds, then thirty, then a minute, then two.</summary>
    public static readonly IReadOnlyList<TimeSpan> Default =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(120),
    ];

    /// <summary>How far either way a step is spread — a fifth.</summary>
    public const double JitterFraction = 0.2;

    /// <summary>
    /// The wait before attempt <paramref name="attempt"/> + 2, or null when there is not going to
    /// be one — the ladder is spent, or the deadline would be.
    /// </summary>
    /// <param name="attempt">Zero for the wait after the first failure.</param>
    /// <param name="roll">A sample in [0,1]: 0 is the bottom of the jitter band, 1 the top.</param>
    /// <param name="elapsed">
    /// WALL CLOCK since the first launch — the launches, not only the earlier waits.
    /// </param>
    /// <param name="budget">
    /// What this reviewer has to spend, which is its own process timeout.
    /// </param>
    /// <remarks>
    /// The budget counts elapsed time rather than the sum of the waits, and that distinction is the
    /// finding three reviewers raised independently on this change's plan: five plus thirty seconds
    /// of waiting fits a sixty-second deadline, and the two failed launches that produced them have
    /// already taken ninety.
    /// </remarks>
    public static TimeSpan? NextWait(
        int attempt,
        IReadOnlyList<TimeSpan> steps,
        double roll,
        TimeSpan elapsed,
        TimeSpan budget)
    {
        if (attempt < 0 || attempt >= steps.Count)
        {
            return null;
        }

        var wait = Jittered(steps[attempt], roll);

        return elapsed + wait < budget ? wait : null;
    }

    /// <summary>One step, spread by up to a fifth either way.</summary>
    private static TimeSpan Jittered(TimeSpan step, double roll) =>
        step * (1 + ((Math.Clamp(roll, 0, 1) * 2) - 1) * JitterFraction);

    /// <summary>
    /// <c>"5,30,60,120"</c> — the steps in seconds — or nothing at all when any part of it cannot
    /// be read.
    /// </summary>
    /// <remarks>
    /// All or nothing, deliberately. A half-parsed ladder is a policy nobody wrote: it would run a
    /// different wait from the one in the config file and say nothing about it. Empty is the
    /// caller's signal to fall back AND to report, which is what <c>PanelSettings.Unrecognised</c>
    /// already exists for.
    /// </remarks>
    public static IReadOnlyList<TimeSpan> Parse(string? csv)
    {
        var parts = (csv ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return [];
        }

        var steps = new List<TimeSpan>(parts.Length);
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || seconds <= 0)
            {
                return [];
            }

            steps.Add(TimeSpan.FromSeconds(seconds));
        }

        return steps;
    }
}
