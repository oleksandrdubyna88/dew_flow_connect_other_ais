namespace CoaiMcp.Runners.Context;

/// <summary>One changed file, as <c>git diff --numstat -z</c> described it.</summary>
/// <param name="Path">Where the file is on the branch — the name a reviewer will look for.</param>
/// <param name="RenamedFrom">Where it was on the base, or empty when it did not move.</param>
public sealed record NumstatChange(string Path, string RenamedFrom, bool IsBinary)
{
    /// <summary>
    /// What to hand git to get this file's diff — BOTH names when it moved.
    /// </summary>
    /// <remarks>
    /// One name is not enough for a rename: asking for the new path alone shows an addition of a
    /// whole file, and the old path alone a deletion of one. Given the pair, git recognises the move
    /// and prints the similarity, the rename header and the edit underneath — which is the only form
    /// in which a refactor's change is readable at all.
    /// </remarks>
    public IReadOnlyList<string> Pathspecs => RenamedFrom.Length == 0 ? [Path] : [RenamedFrom, Path];
}

/// <summary>
/// Reads the machine-readable numstat, which is the only form of it whose third column is a PATH.
/// </summary>
/// <remarks>
/// <para><b>The human-readable form is not parseable, and this is the defect that proved it</b>
/// (product audit of 2026-09-09, finding 4). Its third column takes two shapes that are not file
/// names: a rename arrives as <c>src/{old.cs =&gt; new.cs}</c>, and any path with a byte outside
/// ASCII — or a quote, a backslash, a tab, a newline — arrives C-QUOTED with escapes under the
/// default <c>core.quotePath</c>. Handed back to git as a pathspec, neither matches anything, so the
/// file reached the reviewer NAMED, with an empty diff under it and no error anywhere. Measured on a
/// fixture repository: the collected paths were <c>src/dead.cs</c>,
/// <c>src/{old.cs =&gt; new.cs}</c> and <c>"src/\321\204\320\260\320\271\320\273.cs"</c> — one path
/// and two things that look like one.</para>
/// <para><c>-z</c> removes both problems at once rather than one at a time: fields are separated by
/// NUL, paths are never quoted, and a rename carries its two names as two more fields instead of
/// being joined into a sentence. There is nothing left to unescape, which is the point — an
/// unescaper is a second parser to get wrong.</para>
/// <para>Pure, and separate from <see cref="ContextAssembler"/> for that reason: the shapes below
/// are exhaustively testable without a repository, and the assembler keeps only the part that needs
/// one.</para>
/// </remarks>
public static class NumstatReader
{
    /// <summary>Binary files have no line counts, and git says so with a dash in both columns.</summary>
    private const string NoLineCounts = "-";

    /// <summary>
    /// The records, in the order git listed them.
    /// </summary>
    /// <remarks>
    /// The shape, measured against git rather than read from a manual — every field NUL-terminated:
    /// <code>
    /// 2\t1\tsrc/file.cs        an ordinary change
    /// -\t-\tsrc/logo.png       a binary one
    /// 1\t0\t  src/old.cs  src/new.cs    a RENAME: the path column is EMPTY and two fields follow
    /// </code>
    /// So an empty third column is not a malformed record — it is the announcement of one.
    /// </remarks>
    public static IEnumerable<NumstatChange> Read(string numstat)
    {
        var fields = numstat.Split('\0');
        for (var i = 0; i < fields.Length; i++)
        {
            // The tail after the final NUL, and any blank git chose to emit. Not a record.
            if (fields[i].Length == 0)
            {
                continue;
            }

            var columns = fields[i].Split('\t');
            if (columns.Length < 3)
            {
                continue;
            }

            var binary = columns[0] == NoLineCounts;
            if (columns[2].Length > 0)
            {
                yield return new NumstatChange(columns[2], string.Empty, binary);

                continue;
            }

            // A rename, and its two names are the next two fields. Output that ends here is
            // truncated — dropping the half-record is the only honest thing available, and it cannot
            // happen against a git that completed, which `Git` has already checked.
            //
            // The EMPTINESS of the new name is half the check and the half that was missing: the
            // split leaves a blank tail after the final NUL, so a record cut off after its old name
            // still has a field to read and it is "". Without this the reader emitted a change whose
            // Path was empty, which downstream is a file nobody can name — caught by the test for
            // exactly this, which is why it is here rather than in a later story's bug report.
            if (i + 2 >= fields.Length || fields[i + 2].Length == 0)
            {
                yield break;
            }

            yield return new NumstatChange(fields[i + 2], fields[i + 1], binary);
            i += 2;
        }
    }
}
