namespace CoaiMcp.Runners.Context;

/// <summary>One changed file, as <c>git diff --numstat -z</c> described it.</summary>
/// <param name="Path">Where the file is on the branch — the name a reviewer will look for.</param>
/// <param name="RenamedFrom">Where it was on the base, or empty when it did not move.</param>
public sealed record NumstatChange(string Path, string RenamedFrom, bool IsBinary)
{
    /// <summary>
    /// What to hand git to get this file's diff — BOTH names when it moved, each one neutralised.
    /// </summary>
    /// <remarks>
    /// <para>One name is not enough for a rename: asking for the new path alone shows an addition of
    /// a whole file, and the old path alone a deletion of one. Given the pair, git recognises the
    /// move and prints the similarity, the rename header and the edit underneath — which is the only
    /// form in which a refactor's change is readable at all.</para>
    /// <para><b><c>:(literal)</c> on every one of them, and that prefix is the third shape of
    /// finding 4</b> (CodeRabbit, on the pull request that fixed the other two). After <c>--</c> git
    /// reads a leading <c>:(…)</c> as MAGIC, not as a name — and <c>:(exclude)ordinary.txt</c> is a
    /// perfectly legal POSIX filename. Sent as it arrived it asks for "everything except
    /// ordinary.txt", so the reviewer receives somebody else's changed file under this one's name,
    /// with nothing anywhere saying so. Measured on a fixture built through <c>mktree</c>: the
    /// one-file diff came back carrying two files. The prefix says "the rest of this is a path",
    /// which is what the caller meant every time.</para>
    /// <para>It belongs HERE rather than at the call site because the property is already named for
    /// what it produces: a pathspec is not a path, and the one thing a caller must not have to
    /// remember is the difference.</para>
    /// </remarks>
    public IReadOnlyList<string> Pathspecs => RenamedFrom.Length == 0
        ? [Literal(Path)]
        : [Literal(RenamedFrom), Literal(Path)];

    /// <summary>A path, spelled so that git reads all of it as a name.</summary>
    private static string Literal(string path) => $":(literal){path}";
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
        // A WHILE over a cursor rather than a `for`, because a rename consumes THREE fields and an
        // ordinary record one: the step is part of what each record means, and a `for` whose body
        // advances its own counter says the opposite — that the step is fixed and the body is an
        // exception to it.
        var fields = numstat.Split('\0');
        var at = 0;
        while (at < fields.Length)
        {
            if (Counts(fields[at]) is not var (path, binary))
            {
                at += 1;

                continue;
            }

            if (path.Length > 0)
            {
                yield return new NumstatChange(path, string.Empty, binary);
                at += 1;

                continue;
            }

            yield return Renamed(fields, at, binary);
            at += 3;
        }
    }

    /// <summary>
    /// The counts and the path of one record, or nothing when the field is not a record at all.
    /// </summary>
    /// <remarks>
    /// <para><b>At most THREE parts, and that bound is the whole of it.</b> A tab is a legal
    /// character in a filename on Linux, and `-z` does not escape it — it removes the need to escape
    /// the SEPARATOR, which is NUL, and says nothing about the two tabs that divide the counts from
    /// the path. Splitting on every tab therefore turned <c>1⇥0⇥src/od⇥d.cs</c> into a path of
    /// <c>src/od</c>: a name that matches nothing, so the file reached the reviewer named with an
    /// empty diff — which is the very defect this class was written to end, reintroduced one line
    /// below the fix for it.</para>
    /// <para>Six reviewers across two vendors found it in one round, and the first version of this
    /// class's own test ASSERTED the truncated value as though it were correct. That is the shape
    /// testing.md warns about: the suite that finds a defect must not be the thing that enshrines
    /// it. The test asserts the whole name now.</para>
    /// <para>An empty field — the tail after the final NUL, or a blank git chose to emit — splits
    /// into one part and is answered with nothing, so the caller needs no separate check for it.</para>
    /// </remarks>
    private static (string Path, bool IsBinary)? Counts(string field)
    {
        var columns = field.Split('\t', 3);

        return columns.Length < 3 ? null : (columns[2], columns[0] == NoLineCounts);
    }

    /// <summary>The rename whose two names follow the record that announced it.</summary>
    /// <remarks>
    /// <b>A stream that ends mid-record is refused, not trimmed.</b> Returning the records read so
    /// far would hand the reviewer a diff SHORTER than the change and call it the change — the exact
    /// failure this class exists to prevent, arriving by a different door. It cannot happen against a
    /// git that exited zero, which <c>ContextAssembler.Git</c> has already checked, so a throw here
    /// is the report of something impossible rather than a path anybody is expected to take. The
    /// emptiness test is the half that matters: the split leaves a blank tail after the final NUL, so
    /// a record cut off after its old name still has a field to read and that field is "".
    /// </remarks>
    private static NumstatChange Renamed(string[] fields, int at, bool binary) =>
        at + 2 < fields.Length && fields[at + 2].Length > 0
            ? new NumstatChange(fields[at + 2], fields[at + 1], binary)
            : throw new ContextException(
                "diff --numstat -z",
                $"a rename record ended without its new name, after '{fields[at + 1]}' — the diff is "
                + "incomplete and a short diff must never be presented as a whole one");
}
