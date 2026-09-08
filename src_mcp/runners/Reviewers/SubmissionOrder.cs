namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The order a round's reviewers are DISPATCHED in — which, for a Team-server reviewer, is the
/// order its review reaches the shared queue.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A Team server holds one company account per vendor, and everybody's
/// client submits a round's reviewers in the same order — because everybody builds their vendor list
/// the same way: the catalog offers vendors in a fixed order, the panel appends them as they are
/// added, the settings file preserves that, and the fan-out walks it. With one person the order is
/// invisible. With ten submitting inside a minute, the vendor that happens to sit first in everyone's
/// list takes ten reviews into a queue of depth one while the third vendor's account sits idle — and
/// each of those ten waits behind nine strangers for an account that could have answered them at
/// once. The load is skewed by a list order nobody chose.</para>
/// <para><b>What it does NOT touch.</b> Only REMOTE reviewers move, and they move only among the
/// positions remote reviewers already occupy: a local reviewer keeps its place exactly, so nothing
/// about a round that never speaks to a server changes. The server's own <c>JobStore.TryClaim</c>
/// stays strictly FIFO — fairness there is what makes a queue position mean anything, and
/// randomising the CLAIM would turn a long wait into an unpredictable one. The fix belongs entirely
/// on the submitting side.</para>
/// <para>Pure, and taking its randomness as a parameter, so the distribution is a unit test rather
/// than something only ten machines in one minute could show.</para>
/// </remarks>
public static class SubmissionOrder
{
    /// <summary>
    /// Positions into <paramref name="work"/>, in the order the reviewers should be started.
    /// </summary>
    /// <param name="roll">
    /// A source of values in [0,1) — <c>Random.Shared.NextDouble</c> in production, pinned in tests.
    /// </param>
    public static IReadOnlyList<int> For(IReadOnlyList<ReviewerWork> work, Func<double> roll)
    {
        var order = Enumerable.Range(0, work.Count).ToArray();
        var remote = order.Where(i => IsRemote(work[i])).ToArray();
        if (remote.Length < 2)
        {
            return order;
        }

        var shuffled = Shuffled(remote, roll);
        for (var i = 0; i < remote.Length; i++)
        {
            order[remote[i]] = shuffled[i];
        }

        return order;
    }

    /// <summary>
    /// Fisher-Yates over the remote entries alone, written back into the positions they held.
    /// </summary>
    /// <remarks>
    /// Shuffling the whole list would move local reviewers for no reason, and the shape of a round
    /// is something a person watching has learned to read. The index is clamped because
    /// <paramref name="roll"/> is a parameter: a test that pins it to exactly 1.0 would otherwise
    /// index one past the end, and a guard is cheaper than a contract nobody can enforce.
    /// </remarks>
    private static int[] Shuffled(int[] remote, Func<double> roll)
    {
        var shuffled = remote.ToArray();
        for (var i = shuffled.Length - 1; i > 0; i--)
        {
            var j = Math.Clamp((int)(roll() * (i + 1)), 0, i);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return shuffled;
    }

    /// <summary>
    /// Whether this reviewer's review is submitted to a Team server.
    /// </summary>
    /// <remarks>
    /// The job file is the marker because only <see cref="RemoteRuntime"/> writes one — it is the
    /// handle the parent uses to cancel a review still running on the team's subscription, and no
    /// local runtime has anything to cancel. A second flag saying the same thing is a second thing
    /// to forget.
    /// </remarks>
    private static bool IsRemote(ReviewerWork work) => work.Invocation.JobFile.Length > 0;
}
