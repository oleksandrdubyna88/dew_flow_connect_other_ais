namespace CoaiMcp.Runners.Files;

/// <summary>
/// Writing a small file so that no reader ever sees half of it.
/// </summary>
/// <remarks>
/// <para>Write to a temp beside the destination, then move over it. The move is the only step a
/// reader can observe, and it is atomic on every filesystem this runs on, so a crash or a concurrent
/// read finds either the old file or the new one and never a truncated one.</para>
/// <para><b>The temp name carries a GUID</b>, which is what makes it safe under concurrency: two
/// processes writing the same destination at the same time would otherwise share one temp path and
/// interleave into it. <c>ConsultSchemaFile</c> found that first and this is its shape, extracted on
/// story 3.2a's code round when a reviewer counted the seventh open-coded copy.</para>
/// <para><b>A temp that could not be moved is removed</b>, best effort, before the failure flies on: a
/// directory of evidence or health files that collected orphaned temporaries on every failed write would
/// be a growth surface with no owner (epic 2's review of PLAN_the_consultant_works_on_every_vendor.md,
/// which moved <c>EvidenceFile</c> and <c>ConsultHealthStore</c> onto this instead of a third and fourth
/// hand-written copy).</para>
/// <para><b>Five older copies are still open-coded</b> — <c>ConsultSchemaFile</c>,
/// <c>RemoteRuntime</c>, <c>ArtifactStore</c>, <c>Escalations</c>, <c>SessionStore</c>. (<c>ReviewerExecutor</c>
/// left the list when its evidence moved onto <c>EvidenceFile</c>.) They are named here rather than rewritten
/// inside a story about something else: each has its own error handling around the dance and moving them is
/// a change that deserves its own diff and its own round.</para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>Writes the text so that a reader sees the whole of it or none of it.</summary>
    /// <param name="path">The destination. Its directory must already exist.</param>
    /// <param name="text">What to write.</param>
    /// <param name="sharingRetry">
    /// How long to keep retrying the MOVE when the destination is held open by a reader — on Windows a move
    /// over a file another handle has open without delete sharing is refused. Zero, the default, tries once.
    /// </param>
    public static void Write(string path, string text, TimeSpan sharingRetry = default)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, text);
            MoveOver(temp, path, DateTime.UtcNow + sharingRetry);
        }
        catch
        {
            Discard(temp);
            throw;
        }
    }

    private static void MoveOver(string temp, string path, DateTime retryUntil)
    {
        while (true)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < retryUntil)
            {
                Thread.Sleep(Random.Shared.Next(10, 40));
            }
        }
    }

    private static void Discard(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The cleanup of a failure that is already flying; the caller's own handling reports it.
        }
    }
}
