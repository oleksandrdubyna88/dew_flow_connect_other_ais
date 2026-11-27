namespace CoaiMcp.Runners.Files;

/// <summary>
/// The ONE road for a host read of an entry in a folder that a coder's unit can write (pass 8, M19 and S55): the sign-in copy as the unit left it, the guard's records, a codex rollout, <c>report.txt</c>, and the folder walk before a fresh codex launch. A command of the model in the unit can replace any of them
/// by a FIFO (a plain open waits for ever), a link (a plain open reads what it points at) or a file of any size. So a read here never waits (<c>O_NONBLOCK</c>), never follows a link (<c>O_NOFOLLOW</c>), reads a REGULAR file only (the type is asked of the open descriptor), stays under a size limit,
/// and is bounded in time. What it cannot read is a sentence, never a wait and never an exception.
/// </summary>
internal static class UnitWrittenFile
{
    /// <summary>How long one read, or one walk, of this kind may take: far more than a regular file under the limit needs, and a fixed number — never scaled with a size.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(20);

    /// <summary>The size a sign-in copy may have: it holds one token record.</summary>
    public const int MostCopyBytes = 256 * 1024;

    /// <summary>The size the guard's records may have: about 150 bytes a tool call.</summary>
    public const int MostRecordsBytes = 16 * 1024 * 1024;

    /// <summary>The size a codex rollout may have (read as a stream, never whole).</summary>
    public const long MostRolloutBytes = 512L * 1024 * 1024;

    /// <summary>How many entries, and how deep, a walk of a folder may go.</summary>
    public const int MostEntries = 20_000;

    public const int MostDepth = 12;

    /// <summary>The bytes of <paramref name="path"/>: <see cref="FileReadResult.Missing"/> when nothing is there, <see cref="FileReadResult.Unreadable"/> (with the reason) for a link, a FIFO, a folder, a file over <paramref name="maxBytes"/>, or a read that did not finish in <see cref="Bound"/>.</summary>
    public static FileReadResult Read(string path, int maxBytes) =>
        Bounded(() => RegularFiles.ReadNoFollow(path, maxBytes), new FileReadResult.Unreadable($"it did not finish in {Bound.TotalSeconds:0} seconds"));

    /// <summary>The sentence that says the entry at <paramref name="path"/> is not a plain file of at most <paramref name="maxBytes"/> bytes that can be read, or empty when it is, or is not there. Opens the entry the same way as <see cref="Read"/> and reads nothing of it.</summary>
    public static string Problem(string path, long maxBytes) =>
        Bounded(() =>
        {
            var (stream, failure) = RegularFiles.OpenNoFollow(path);
            if (stream is null)
            {
                return failure is FileReadResult.Unreadable unreadable ? $"it is not a regular file that can be read ({unreadable.Reason})" : string.Empty;
            }

            using (stream)
            {
                return stream.Length > maxBytes ? $"it is larger than {maxBytes} bytes" : string.Empty;
            }
        }, $"it did not finish in {Bound.TotalSeconds:0} seconds");

    /// <summary>The same sentence for the entries a vendor names, with the name of each: the first that is not right, or empty.</summary>
    public static string FirstProblem(IEnumerable<(string Path, long MaxBytes)> entries) =>
        entries.Select(entry => (entry.Path, Said: Problem(entry.Path, entry.MaxBytes))).FirstOrDefault(entry => entry.Said.Length > 0) is { Said.Length: > 0 } bad
            ? $"'{Path.GetFileName(bad.Path)}' in the coder's folders, which a unit can write, is not what coai made: {bad.Said}"
            : string.Empty;

    /// <summary>
    /// The files below <paramref name="root"/>, by a walk that never follows a link and never opens anything: a folder is entered only when it is a real folder; a link, or anything but a folder and a file, is the <c>Problem</c>; a walk past <see cref="MostEntries"/> or <see cref="MostDepth"/>, or one that does not finish in <see cref="Bound"/>, is too.
    /// <c>Odd</c> says the problem is an entry that is not what a CLI writes (a link, a node of another kind, a walk too wide), not just a folder that could not be listed.
    /// </summary>
    public static (IReadOnlyList<string> Files, string Problem, bool Odd) Walk(string root) =>
        Bounded(() => WalkNow(root), ((IReadOnlyList<string>)[], $"the walk did not finish in {Bound.TotalSeconds:0} seconds", true));

    private static (IReadOnlyList<string> Files, string Problem, bool Odd) WalkNow(string root)
    {
        var files = new List<string>();
        var pending = new Stack<(string Folder, int Depth)>();
        pending.Push((root, 0));
        try
        {
            while (pending.Count > 0)
            {
                var (folder, depth) = pending.Pop();
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    if (files.Count + pending.Count >= MostEntries)
                    {
                        return (files, $"there are more than {MostEntries} entries below the folder", true);
                    }

                    switch (RealPath.Lstat(entry).Kind)
                    {
                        case EntryKind.Directory when depth + 1 <= MostDepth:
                            pending.Push((entry, depth + 1));
                            break;
                        case EntryKind.Directory:
                            return (files, $"a folder lies deeper than {MostDepth} levels", true);
                        case EntryKind.File:
                            files.Add(entry);
                            break;
                        case EntryKind.Missing:
                            break;
                        default:
                            return (files, $"'{Path.GetFileName(entry)}' is a link, not a folder or a file, so it was not followed", true);
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ([], $"the folder cannot be listed ({e.GetType().Name})", false);
        }

        return (files, string.Empty, false);
    }

    /// <summary>Runs <paramref name="work"/> and waits for it at most <see cref="Bound"/>; <paramref name="onTimeout"/> when it did not finish. The task that did not finish is left to end by itself (a read that cannot wait does not hold a thread long).</summary>
    public static T Bounded<T>(Func<T> work, T onTimeout, TimeSpan? bound = null)
    {
        var task = Task.Run(work);

        return task.Wait(bound ?? Bound) ? task.GetAwaiter().GetResult() : onTimeout;
    }
}
