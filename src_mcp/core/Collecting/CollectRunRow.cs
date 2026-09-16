namespace CoaiMcp.Core.Collecting;

/// <summary>How far a run has got — the three states, counted once.</summary>
/// <remarks>
/// A record rather than three loose integers because they travel together through four layers and are
/// meaningless apart: a `skipped` count without the `collected` beside it cannot say whether a run
/// went well. It is also what stops a parameter list growing to eight positional integers, where two
/// of them are eventually swapped by somebody reading the call site rather than the signature.
/// </remarks>
public readonly record struct CollectTally(int Collected = 0, int Skipped = 0, int Failed = 0)
{
    /// <summary>How many candidates this run has actually decided.</summary>
    public int Decided => Collected + Skipped + Failed;
}

/// <summary>One collector run, as the database remembers it.</summary>
/// <remarks>
/// <para><b>An empty <see cref="Id"/> means no run has ever happened here</b> — a state the panel
/// renders, and one that must never be confused with "this build cannot tell you". That is why this
/// is an empty record rather than a null: the family's rule about nulls in business logic exists
/// precisely so an absence has to be given a meaning rather than inherited from a reference type.</para>
/// <para>Times are ISO-8601 UTC strings, as every other column here is — sortable and comparable as
/// text, converted for a reader only in the UI.</para>
/// </remarks>
/// <param name="HeartbeatUtc">
/// When the run last said it was alive. What lets a sweep tell an abandoned run from one that is
/// happening right now, in a different process, while the sweep looks at it.
/// </param>
/// <param name="State">`running`, `done`, `failed` or `interrupted`.</param>
public sealed record CollectRunRow(
    string Id = "",
    string StartedUtc = "",
    string FinishedUtc = "",
    string HeartbeatUtc = "",
    string State = "",
    string Model = "",
    int Candidates = 0,
    int Picked = 0,
    int Collected = 0,
    int Skipped = 0,
    int Failed = 0,
    string Reasons = "")
{
    /// <summary>Whether a run is happening — what the button is disabled by.</summary>
    /// <remarks>
    /// <b>Defined as "not finished", not as "equal to running".</b> A state this build has never
    /// heard of — a later one that pauses for an approval, say — would read as FINISHED under an
    /// equality test, and the panel would re-enable Collect and let a second run start beside the
    /// first. Unknown means keep waiting, which is the safe direction. (Code round 2, codex.)
    /// </remarks>
    public bool Running => Any && !CollectRunState.IsFinished(State);

    /// <summary>Whether any run has ever been recorded here.</summary>
    public bool Any => Id.Length > 0;
}

/// <summary>The `state` column's vocabulary, spelled once.</summary>
/// <remarks>
/// Constants rather than an enum for the same reason <see cref="SkipReason"/> is: they are written to
/// a database column and read back by a panel that is not this program, and one spelling that means
/// the same thing in SQL, in JSON and in TypeScript beats three that nearly do.
/// </remarks>
public static class CollectRunState
{
    /// <summary>Started, and still saying so.</summary>
    public const string Running = "running";

    /// <summary>Every candidate was decided.</summary>
    public const string Done = "done";

    /// <summary>The run itself threw. Distinct from candidates that failed, which are a count.</summary>
    public const string Failed = "failed";

    /// <summary>
    /// It stopped saying it was alive — a closed window, a killed process, a machine that slept.
    /// </summary>
    /// <remarks>
    /// Not a failure and not a success: nothing is known about what it would have decided. The
    /// candidates it did claim keep their own outcomes, because those were written per candidate as
    /// they were decided, and `--all` is how somebody revisits the rest.
    /// </remarks>
    public const string Interrupted = "interrupted";

    /// <summary>The states that mean nothing more will happen.</summary>
    /// <remarks>
    /// The list is of ENDINGS rather than of beginnings, and everything is read against it: a state
    /// nobody here recognises is treated as still going, because the cost of waiting for a run that
    /// has ended is a stale line, and the cost of declaring one finished while it works is a second
    /// run started beside it.
    /// </remarks>
    public static readonly IReadOnlyList<string> Finished = [Done, Failed, Interrupted];

    /// <summary>Whether this state means the run is over.</summary>
    public static bool IsFinished(string state) => Finished.Contains(state, StringComparer.Ordinal);
}
