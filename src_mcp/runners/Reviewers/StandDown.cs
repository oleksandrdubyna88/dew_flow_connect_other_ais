namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// Whether the cloud reviewers of ONE round were quiet enough for the local reviewer to stand down —
/// issue #485, behind <c>COAI_STOP_LOCAL_WHEN_QUIET</c>.
/// </summary>
/// <remarks>
/// <para><b>Quiet</b> means: the round has at least one cloud (non-engine) reviewer, EVERY one of them has
/// finished, every one answered <see cref="ReviewerOutcome.Ok"/>, and their findings — any severity, counted
/// before de-duplication — add up to at most <see cref="MostRemarks"/>. A failed cloud reviewer's zero is
/// not "found little", so one failure keeps the local reviewer running; so does a round with no cloud
/// reviewer at all.</para>
/// <para>Recorded from the fan-out's threads, read from the engine lane's: every read and write is
/// under one lock.</para>
/// </remarks>
public sealed class StandDown(int cloudRows)
{
    /// <summary>"No more than one remark" — the operator's number in the issue.</summary>
    public const int MostRemarks = 1;

    private readonly Lock _gate = new();
    private int _finished;
    private int _remarks;
    private bool _anyFailed;

    /// <summary>One per round, counting that round's cloud rows.</summary>
    public static StandDown For(IReadOnlyList<ReviewerWork> work) => new(work.Count(w => !w.Invocation.IsOnEngine));

    /// <summary>A cloud reviewer finished — answered, or failed.</summary>
    public void Record(ReviewerOutcome outcome)
    {
        lock (_gate)
        {
            _finished++;
            if (outcome is ReviewerOutcome.Ok ok)
            {
                _remarks += ok.Review.Findings.Count();
            }
            else
            {
                _anyFailed = true;
            }
        }
    }

    public bool Quiet
    {
        get
        {
            lock (_gate)
            {
                return cloudRows > 0 && _finished == cloudRows && !_anyFailed && _remarks <= MostRemarks;
            }
        }
    }

    /// <summary>What a stood-down row says — in the round, the log and the extension's round view.</summary>
    public string Reason
    {
        get
        {
            lock (_gate)
            {
                return $"the cloud reviewers found {_remarks} remark(s) between them, so the local reviewer was not started";
            }
        }
    }
}
