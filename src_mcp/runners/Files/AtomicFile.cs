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
/// <para><b>Six older copies are still open-coded</b> — <c>ConsultSchemaFile</c>,
/// <c>RemoteRuntime</c>, <c>ReviewerExecutor</c>, <c>ArtifactStore</c>, <c>Escalations</c>,
/// <c>SessionStore</c>. They are named here rather than rewritten inside a story about worktrees:
/// each has its own error handling around the dance and moving them is a change that deserves its
/// own diff and its own round.</para>
/// </remarks>
public static class AtomicFile
{
    /// <summary>Writes the text so that a reader sees the whole of it or none of it.</summary>
    /// <param name="path">The destination. Its directory must already exist.</param>
    /// <param name="text">What to write.</param>
    public static void Write(string path, string text)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }
}
