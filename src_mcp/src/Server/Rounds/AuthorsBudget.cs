namespace CoaiMcp.Server;

/// <summary>
/// What the authors step may spend on ONE round, as one named value (E5.S4 of
/// <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>): the time of the WHOLE step and the bytes of the transcript
/// SLICE the reader may take.
/// </summary>
/// <remarks>
/// <para>The 2 s is one deadline for the whole <see cref="RoundAuthorsStep.For"/>, taken from the clock when it starts and passed down:
/// the reader's own time limit is what is left of it at the reader's start, and the peer lookup, the <c>.gitmodules</c> parse, E4's
/// attribution and the snapshot's construction each check the same deadline between phases. The 64 MiB is the reader's own byte limit, and
/// E3 charges it on the SLICE — the bytes its byte gate YIELDS after the <c>fromUtc</c> bound, never the bytes it skipped — while the time
/// runs over the whole read, skipped bytes included (E3 §7 F1).</para>
/// <para><b>Where the numbers come from.</b> E1 measured the read on Node over the largest Windows session (650 MB): a day-gated parse, p50
/// 19.9 s — the prediction of ≤ 300 ms refuted by two orders of magnitude (<c>research/RESULTS_caller_transcripts_on_both_sides.md</c>, Q6),
/// which is why the budget stands on the slice and on the C# measurement this story made over E1's largest fixture slice, recorded in the
/// plan's implementation record, and never on the whole file. Hitting either limit is <c>partial: read budget</c>, with the rows read so far.</para>
/// <para>A parameter of the step with <see cref="Default"/> as its default, so a test can pass a one-kilobyte budget without changing a
/// constant.</para>
/// </remarks>
internal sealed record AuthorsBudget(long MaxBytes, TimeSpan MaxTime)
{
    /// <summary>2 s for the step, 64 MiB of the slice.</summary>
    public static AuthorsBudget Default { get; } = new(64L * 1024 * 1024, TimeSpan.FromSeconds(2));
}
