namespace CoaiMcp.Core.Collecting;

/// <summary>One send, as the database remembers it.</summary>
/// <remarks>
/// <para><b>Why a send needs a row at all, when the pairs already record what was sent.</b> A pair is
/// marked only on the server's acknowledgement — the rule that makes a killed upload safe to retry —
/// so for the whole of a multi-minute run the funnel says exactly what it said before the run began.
/// A panel reading it shows an idle button, a reload shows an idle button, and a second Send starts a
/// second process against the same waiting pairs. Three plan reviewers arrived at that independently;
/// the durable-status rule is what it violates.</para>
/// <para><b>An empty <see cref="Id"/> means no send has ever happened here</b> — a state the section
/// renders, and one that must never be confused with "this build cannot tell you". An empty record
/// rather than a null, per the family's rule about nulls in business logic.</para>
/// <para>The shape is <see cref="CollectRunRow"/>'s, deliberately: the two are the same problem, and a
/// second vocabulary for it would be a second thing the panel has to learn.</para>
/// </remarks>
/// <param name="Server">Where it was sent. A send is only meaningful against an address.</param>
/// <param name="Offered">How many pairs this run set out to send, so progress has a denominator.</param>
/// <param name="Duplicate">
/// How many the server already held. A SUCCESS, and counted apart from <paramref name="Sent"/>
/// because the two answer different questions: a person who retried a killed send needs to read
/// that the pairs arrived the first time, not to wonder whether they have sent them twice.
/// </param>
/// <param name="Trouble">
/// The CLI's own sentence when a run could not finish — "the server answered 502" sends a person
/// somewhere, and "failed" sends them nowhere.
/// </param>
public sealed record UploadRunRow(
    string Id = "",
    string StartedUtc = "",
    string FinishedUtc = "",
    string HeartbeatUtc = "",
    string State = "",
    string Server = "",
    int Offered = 0,
    int Sent = 0,
    int Duplicate = 0,
    int Refused = 0,
    string Trouble = "")
{
    /// <summary>Whether a send is happening — what the button is disabled by.</summary>
    /// <remarks>
    /// Defined as "not finished" rather than "equal to running", for the reason
    /// <see cref="CollectRunRow.Running"/> gives: an unknown state means keep waiting, because the
    /// cost of a stale line is a stale line and the cost of declaring a live run finished is a second
    /// process sending the same pairs.
    /// </remarks>
    public bool Running => Any && !UploadRunState.IsFinished(State);

    /// <summary>Whether any send has ever been recorded here.</summary>
    public bool Any => Id.Length > 0;
}

/// <summary>The `state` column's vocabulary for a send, spelled once.</summary>
/// <remarks>
/// Constants rather than an enum, as <see cref="CollectRunState"/> is and for its reason: the value is
/// written to a database column and read back by a panel that is not this program.
/// </remarks>
public static class UploadRunState
{
    /// <summary>Started, and still saying so.</summary>
    public const string Running = "running";

    /// <summary>The server answered about every pair this run offered.</summary>
    public const string Done = "done";

    /// <summary>
    /// It could not finish. NOT "the pairs were bad" — <see cref="UploadRunRow.Refused"/> is that,
    /// and it is a count rather than a failure.
    /// </summary>
    public const string Failed = "failed";

    /// <summary>It stopped saying it was alive — a closed window, a killed process, a slept machine.</summary>
    /// <remarks>
    /// Nothing is lost by it: a pair is marked only on an acknowledgement, so whatever was in the air
    /// is simply offered again, and the server is idempotent on the derived pair id, so a pair that
    /// WAS acknowledged before the process died is a no-op the second time.
    /// </remarks>
    public const string Interrupted = "interrupted";

    /// <summary>The states that mean nothing more will happen.</summary>
    public static readonly IReadOnlyList<string> Finished = [Done, Failed, Interrupted];

    /// <summary>Whether this state means the send is over.</summary>
    public static bool IsFinished(string state) => Finished.Contains(state, StringComparer.Ordinal);
}
