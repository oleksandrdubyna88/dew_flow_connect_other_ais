namespace CoaiMcp.Server;

/// <summary>
/// The prompt and answer files a launch leaves behind, removed once they are older than any record.
/// </summary>
/// <remarks>
/// <para>Every turn writes at least one: codex its <c>-o</c> answer, the local and api shims a
/// <c>.prompt</c> and a <c>.json</c>. They hold the working-tree diff or the caller's context —
/// somebody's source code — and nothing was deleting them. Named as a growth surface rather than
/// discovered as a full disk: one turn is kilobytes, a busy week is a few hundred of them, and they go
/// on the same clock as the record they belong to. (gemini, the consultant plan's round.)</para>
/// <para>Deleted when it is OURS and old. A file cannot say which consultation or question it came
/// from, so the clock stands in for that — one older than the retention window belongs to a record
/// that is itself gone, and a file a running turn is still writing is younger than the window by
/// definition. But age is not ownership: <see cref="Runners.Consultation.ConsultantArtefacts.Ours"/> is
/// what keeps a note somebody left in this directory out of it. (codex, code round.)</para>
/// <para>Extracted out of <see cref="ConsultationStore"/> the day the question consultant's store
/// needed the same sweep over its own directory (PLAN_question_consultant.md, S2) rather than copied
/// into it: the rule lives with the adapters that NAME these files, and two sweeps over one naming
/// rule would drift the day one of them was taught a new extension.</para>
/// </remarks>
internal static class ConsultantArtefactSweep
{
    /// <summary>Removes this product's artefacts under <paramref name="directory"/> older than <paramref name="retention"/>; answers how many went.</summary>
    public static int Sweep(string directory, DateTime nowUtc, TimeSpan retention, Action<string>? warn)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(directory))
        {
            removed += Removed(path, nowUtc, retention, warn) ? 1 : 0;
        }

        return removed;
    }

    private static bool Removed(string path, DateTime nowUtc, TimeSpan retention, Action<string>? warn)
    {
        try
        {
            // OURS by name, as well as old. A person or another component putting a file under this
            // directory would otherwise have it deleted on the retention clock, and a sweep that
            // removes what it did not write is a sweep nobody can trust with a directory.
            if (!Runners.Consultation.ConsultantArtefacts.Ours(Path.GetFileName(path)) || nowUtc - File.GetLastWriteTimeUtc(path) <= retention)
            {
                return false;
            }

            File.Delete(path);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn?.Invoke($"an answer file at {path} is past retention and could not be removed: {e.Message}");

            return false;
        }
    }
}
