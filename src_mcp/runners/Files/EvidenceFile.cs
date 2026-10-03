namespace CoaiMcp.Runners.Files;

/// <summary>
/// A vendor's raw words, kept on disk for the person who has to find out what happened — bounded, and
/// never half-written.
/// </summary>
/// <remarks>
/// <para>Extracted from <c>ReviewerExecutor.KeepIn</c> / <c>Bounded</c> / <c>Discard</c> when the
/// consultation needed exactly the same thing (PLAN_the_consultant_works_on_every_vendor.md, E2.2). Its
/// own copy was neither: <c>ConsultationService.KeepEvidence</c> wrote with <c>File.WriteAllText</c>
/// straight onto the final name and kept whatever arrived, so a transcript could be a megabyte, and a
/// process killed between the truncate and the write left a file that exists and holds nothing — which in
/// an evidence directory reads exactly like a vendor that answered with nothing.</para>
/// <para><b>Written to a temp and moved over the name</b> (<see cref="AtomicFile"/>), for that reason.</para>
/// </remarks>
public static class EvidenceFile
{
    /// <summary>
    /// The cap on ONE kept text.
    /// </summary>
    /// <remarks>
    /// The reviewer answers this was written for are 40 to 90 output tokens — a few hundred bytes — so the
    /// cap never fires on the case it was written for. It exists for the case it was NOT: a vendor that
    /// returns a megabyte of prose, or a consultant's whole event stream, and evidence is for reading rather
    /// than for archiving whatever arrives.
    /// </remarks>
    public const int Cap = 64 * 1024;

    /// <summary>
    /// <paramref name="text"/>, bounded, at <paramref name="directory"/>/<paramref name="fileName"/> — or empty
    /// when it could not be kept, after handing the reason to <paramref name="failed"/>.
    /// </summary>
    /// <remarks>
    /// <para>Keeping evidence may never be what fails the work it describes, so the whole expected filesystem
    /// set is caught: a directory that is really a file, an invalid character reaching <c>Path.Combine</c>, a
    /// policy refusing the write. (Four reviewers on the reviewer executor's code round.) Never silently: a
    /// caller that could not keep its evidence must say so, or the empty directory later reads as "nothing
    /// was ever wrong here". The caller words the note — each knows what the text it was keeping WAS.</para>
    /// <para>The write is <see cref="AtomicFile"/>'s: a GUID-named temp beside the file, moved over it, and
    /// removed when the move fails. It was a fixed <c>.writing</c> sibling here, a hand-written third copy of
    /// that dance (epic 2's review).</para>
    /// </remarks>
    public static string Keep(string directory, string fileName, string text, Action<Exception>? failed)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, fileName);
            AtomicFile.Write(file, Bounded(text));
            return file;
        }
        catch (Exception e) when (e is IOException
                                      or UnauthorizedAccessException
                                      or System.Security.SecurityException
                                      or ArgumentException
                                      or NotSupportedException)
        {
            failed?.Invoke(e);
            return string.Empty;
        }
    }

    /// <summary>The text, or its first <see cref="Cap"/> characters and a line saying it was cut.</summary>
    public static string Bounded(string text) =>
        text.Length <= Cap
            ? text
            : text[..Cap] + $"{Environment.NewLine}{Environment.NewLine}[truncated at {Cap} characters]";
}
