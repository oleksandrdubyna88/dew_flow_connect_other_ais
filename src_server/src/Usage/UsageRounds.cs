using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>What a group of ledger lines says about ROUNDS, beside what it cost (story 5.1 of todo/PLAN_every_round_is_counted.md).</summary>
/// <remarks>Folded into each <see cref="VendorTotal"/> — the company's rows and each person's alike, by the one fold.</remarks>
internal sealed record RoundCounts(
    int Rounds,
    int RunsWithoutRound,
    int Retries,
    int Repairs,
    int FollowUps,
    int Cancelled,
    int NoBudget,
    IReadOnlyDictionary<string, int> LaunchesByKind);

/// <summary>How the rounds and the requests in a group of ledger lines are counted.</summary>
/// <remarks>
/// <para><b>A round is the CLIENT's id, counted once per vendor.</b> One round launches one reviewer per vendor on a plan
/// stage and up to four on a code stage, plus retries, repairs and follow-ups; every one of those requests carries the
/// round the client minted (story 4.1), so the DISTINCT non-empty ids on a vendor's lines are its rounds, however many
/// launches they took. Only REVIEW lines count: a chat carries no round, and the deploy's canary is the deploy's check,
/// not a person's round (story 3.5) — it is in neither number.</para>
/// <para><b>A line without a round is its own number</b> (<see cref="RoundCounts.RunsWithoutRound"/>), never folded into a
/// guess: a review line from a client older than story 4.1, or one written before story 4.2's server read the field.</para>
/// <para><b>Retries, repairs and follow-ups are the CLIENT's words</b> — the line's <c>clientAttempt</c> — and are counted
/// per REQUEST: a request is one job on this server, and one job can write several lines (a launch an account refused
/// before another ran it — <c>rotated</c> — and the server's own continuation — <c>followUp</c> in the SERVER's word).
/// Those two server words are launches inside one request and are counted by none of the three; they stay in
/// <c>runs</c> and <see cref="RoundCounts.LaunchesByKind"/>. A line with neither job nor turn is a request of its own.</para>
/// <para><b>A budget refusal launched nothing</b> — a line with outcome <c>NoBudget</c> is an honest zero (story 4.3) — so it
/// is counted in <see cref="RoundCounts.NoBudget"/>, kept in its round, and left out of the launches per kind.</para>
/// </remarks>
internal static class UsageRounds
{
    private static readonly string CancelledOutcome = nameof(FailureKind.Cancelled);
    private static readonly string NoBudgetOutcome = nameof(FailureKind.NoBudget);

    public static RoundCounts Of(IReadOnlyCollection<UsageLine> lines)
    {
        var reviews = lines.Where(line => line.Kind == JobKind.Review).ToList();

        return new RoundCounts(
            reviews.Select(line => line.Round).Where(round => round.Length > 0).Distinct(StringComparer.Ordinal).Count(),
            reviews.Count(line => line.Round.Length == 0),
            Requests(lines, LaunchAttempts.Retry),
            Requests(lines, LaunchAttempts.Repair),
            Requests(lines, LaunchAttempts.FollowUp),
            lines.Count(line => Ended(line, CancelledOutcome)),
            lines.Count(line => Ended(line, NoBudgetOutcome)),
            LaunchesByKind(lines));
    }

    /// <summary>How many REQUESTS the client called <paramref name="word"/> — distinct jobs, not lines.</summary>
    private static int Requests(IEnumerable<UsageLine> lines, string word) =>
        lines
            .Select((line, index) => (line, index))
            .Where(one => one.line.ClientAttempt.Equals(word, StringComparison.Ordinal))
            .Select(one => RequestOf(one.line, one.index))
            .Distinct(StringComparer.Ordinal)
            .Count();

    /// <summary>The request a line belongs to: its job, else its turn, else the line itself.</summary>
    /// <remarks>The prefixes keep a job id, a turn id and a line's position from ever naming the same request.</remarks>
    private static string RequestOf(UsageLine line, int index) => (line.Job, line.Turn) switch
    {
        ({ Length: > 0 } job, _) => $"job:{job}",
        (_, { Length: > 0 } turn) => $"turn:{turn}",
        _ => $"line:{index}",
    };

    private static bool Ended(UsageLine line, string outcome) => line.Outcome.Equals(outcome, StringComparison.Ordinal);

    /// <summary>Launches per kind, in the wire's spelling and ordinal order — every line but a budget refusal.</summary>
    private static Dictionary<string, int> LaunchesByKind(IEnumerable<UsageLine> lines) =>
        lines
            .Where(line => !Ended(line, NoBudgetOutcome))
            .GroupBy(line => JobKinds.Wire(line.Kind), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
