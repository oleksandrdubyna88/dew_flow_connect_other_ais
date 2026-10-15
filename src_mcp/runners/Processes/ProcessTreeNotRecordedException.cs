namespace CoaiMcp.Runners.Processes;

/// <summary>
/// The tracker could not write a descendant of a child the launcher was ending — raised only AFTER the whole tree was
/// killed and its exit confirmed (epic 6's code round on story 6.1a of todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <remarks>
/// A tracker that writes a slot's reader marker can fail on the disk or a permission. Ending the tree first means no
/// descendant outlives the recorded root because a write failed; surfacing it afterwards means the caller still learns
/// that the identity on disk is PARTIAL — and must keep the slot fail-closed rather than trust a marker that names the
/// root alone. <see cref="Survivors"/> are the members still alive at the confirmation bound, if any.
/// </remarks>
public sealed class ProcessTreeNotRecordedException(IReadOnlyList<ProcessIdentity> survivors, Exception inner)
    : Exception("a descendant of the child could not be recorded; the tree was ended before this was raised", inner)
{
    /// <summary>The tree members still alive after the confirmation bound — empty when every one is confirmed gone.</summary>
    public IReadOnlyList<ProcessIdentity> Survivors { get; } = survivors;
}
