namespace CoaiMcp.Runners.Files;

/// <summary>What one path entry is, by lstat-level facts: not there, a folder, a file, or a link and its text.</summary>
public enum EntryKind
{
    /// <summary>Nothing is at the path (and no link either — a dangling link is <see cref="Link"/>).</summary>
    Missing,

    /// <summary>A folder, not a link: names may be below it.</summary>
    Directory,

    /// <summary>Anything else that is there and is not a link: nothing can be below it (the disk answers ENOTDIR).</summary>
    File,

    /// <summary>A link — symbolic link, or a Windows junction — whether or not its target exists.</summary>
    Link,
}

/// <summary>One path entry, looked at WITHOUT following it.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="LinkText">The link's own text exactly as stored — raw, never resolved; empty unless <paramref name="Kind"/> is <see cref="EntryKind.Link"/>.</param>
public readonly record struct Entry(EntryKind Kind, string LinkText)
{
    public static Entry Missing { get; } = new(EntryKind.Missing, string.Empty);

    public static Entry Directory { get; } = new(EntryKind.Directory, string.Empty);

    public static Entry File { get; } = new(EntryKind.File, string.Empty);

    public static Entry Link(string text) => new(EntryKind.Link, text);
}

/// <summary>Looks at ONE entry without following it. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when it cannot be looked at.</summary>
public delegate Entry EntryReader(string path);

/// <summary>A resolved path, or the sentence saying why there is none — an expected failure as a value.</summary>
public readonly record struct RealPathResult(bool Ok, string Path, string Why)
{
    public static RealPathResult Of(string path) => new(true, path, string.Empty);

    public static RealPathResult Fail(string why) => new(false, string.Empty, why);
}

/// <summary>
/// The real path of a path — what <c>realpath(3)</c> answers — and the ONE place in this product that resolves a path through
/// links.
/// </summary>
/// <remarks>
/// <para><b>Why one road.</b> A path was resolved root-down by hand at four sites, each taking
/// <c>ResolveLinkTarget(returnFinalTarget: true).FullName</c> for a link. That string is the link's target AS TEXT, folded
/// lexically: the directories INSIDE it were never resolved, and a <c>..</c> in the link text was applied to the string
/// instead of to the real directory. With <c>root/l2</c> a link to an outside folder and <c>root/alias</c> a link to
/// <c>root/l2/secret.txt</c>, the walk said "inside" and the operating system read outside (2026-11-01: the document reader,
/// whose text goes to three vendors; the worktree deletion guard; the worktree path comparison). The fix is not a better walk
/// at each site but no walk at any of them: <c>TheLinkResolverIsOneRoadTests</c> fails for a second one.</para>
/// <para><b>How it walks.</b> From the root down, with a stack of segments still to do. A plain segment is appended. A link is
/// read RAW (<see cref="FileSystemInfo.LinkTarget"/>), costs ONE hop of a single budget of <see cref="MaxLinkExpansions"/> for the
/// whole resolution (plain segments are free — a 60-folder path costs nothing), and its text is pushed in front of the rest: an
/// absolute target restarts at its own root (another drive or share is fine for RESOLVING; whether it is a place the caller may
/// use is the caller's decision on the final path), a relative one continues from the link's directory. <c>.</c> is skipped, and
/// <c>..</c> goes to the parent of the path resolved SO FAR — never above its root — which is the parent the disk would use,
/// not the one the text suggests.</para>
/// <para><b>A segment that is not there ends the walk, and a <c>..</c> after it fails.</b> Nothing below a missing entry
/// exists, so the rest is appended as written (once, not re-resolved). But the kernel answers ENOENT for <c>missing/..</c>, and
/// a lexical pop would answer the folder that holds <c>missing</c> — so a link text <c>nonexistent/../evil</c> would resolve to
/// <c>root/evil</c> with <c>evil</c> never looked at. Fail, never pop. A DANGLING link is not "missing": it is a link, it costs
/// a hop, and its target is walked like any other.</para>
/// <para><b>Nothing is below a file.</b> <c>file/.</c>, <c>file/..</c> and <c>file/name</c> fail: the disk answers ENOTDIR, and a pop would answer the file's neighbour, which nobody named.</para>
/// <para><b>Everything else fails.</b> Over the budget, a loop (which only the budget ends), a NUL, an empty link text, a drive-relative
/// <c>C:x</c>, a path over <see cref="MaxPathChars"/> characters, and any IO error are a <see cref="RealPathResult"/> that is not
/// <see cref="RealPathResult.Ok"/>: every caller turns it into refuse / not ours / cannot compare. None of them falls back to the spelling — with ONE named exception: <c>ConsultationService.RepoIdentity</c> keeps the spelling of a checkout it cannot resolve, on purpose. It is an owner LABEL for filtering a caller's own consultations, not an access decision, and both sides compute it the same way, so two callers agree on it whether or not either could resolve the path.</para>
/// <para><b>Containment is the caller's decision</b> on two resolved paths (<see cref="IsInside"/>), root resolved the same way as the path.</para>
/// <para><b>What it does not see.</b> Hard links (one file, two names — there is no path to resolve) and bind mounts (the kernel
/// shows one tree at two places, and no readlink says so). Both are outside what a path check can decide; SECURITY.md says so.
/// Case is kept as spelled: on a case-insensitive disk a comparison that differs only by case is a FALSE refusal (fail closed),
/// never a false accept. The check is at one moment; a swap between the check and the use is not seen.</para>
/// <para>Separators are this OS's (<see cref="Path.DirectorySeparatorChar"/> and <see cref="Path.AltDirectorySeparatorChar"/>): on
/// Unix a backslash is an ordinary letter of a name.</para>
/// </remarks>
public static class RealPath
{
    /// <summary>Link expansions one resolution may take — the kernel's own bound (<c>MAXSYMLINKS</c>) on Linux.</summary>
    public const int MaxLinkExpansions = 40;

    /// <summary>A path, or a link's text, longer than this is refused: resolving it is work nobody asked for (a 200,000-segment path took 36 s in a quadratic walk).</summary>
    public const int MaxPathChars = 32 * 1024;

    private const int ShownChars = 200;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>The real entry reader: <c>lstat</c>-level — a link is a link whether or not its target exists.</summary>
    public static Entry Lstat(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            FileSystemInfo info = attributes.HasFlag(FileAttributes.Directory) ? new DirectoryInfo(path) : new FileInfo(path);

            // A reparse point with no link target (a cloud placeholder, a dedup stub) is read transparently by the OS and
            // redirects no path: it is a plain entry here.
            return info.LinkTarget is { } text ? Entry.Link(text)
                : attributes.HasFlag(FileAttributes.Directory) ? Entry.Directory : Entry.File;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Entry.Missing;
        }
    }

    /// <summary>
    /// The real path of <paramref name="path"/>, resolved root-down. A relative path is taken against
    /// <paramref name="baseDirectory"/> (the process directory when null) — spelled, never folded: the <c>..</c> in it is the disk's to apply.
    /// </summary>
    /// <param name="read">The seam: how one entry is looked at. A test stands a link in with it on a machine that cannot make one.</param>
    public static RealPathResult Resolve(string path, string? baseDirectory = null, EntryReader? read = null)
    {
        try
        {
            return Walk(path, baseDirectory, read ?? Lstat);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return RealPathResult.Fail($"'{Shown(path)}' could not be resolved: {Shown(e.Message)}");
        }
    }

    /// <summary>
    /// The path as <see cref="Resolve"/> answers it for everything but the LAST name, which is kept as spelled and not followed —
    /// for the question "is the thing at this path a link itself", which a fully resolved path can no longer answer.
    /// </summary>
    /// <remarks>A path with no last name to split off — empty, a bare root (<c>/</c>, <c>C:\</c>, a UNC share), <c>.</c>, <c>..</c> — is resolved whole.</remarks>
    public static RealPathResult ResolveParent(string path, string? baseDirectory = null, EntryReader? read = null) =>
        Split(path) is { } cut
            ? Joined(path, Resolve(cut.Folder, baseDirectory, read), cut.Leaf)
            : Resolve(path, baseDirectory, read);

    /// <summary>
    /// The real path of <paramref name="path"/> when it is strictly UNDER <paramref name="root"/> — both resolved root-down — and the last name
    /// is not a link; otherwise a failure. The question every guard in front of a delete asks, asked once: the caller acts on the returned
    /// path, the one that was judged, never on the spelling it was handed.
    /// </summary>
    /// <remarks>
    /// A path that cannot be resolved is not under anything. A link as the last name is not a tree: it is refused rather than followed or
    /// unlinked. The root itself is not under itself.
    /// </remarks>
    public static RealPathResult ResolveBelow(string root, string path, StringComparison comparison, string? baseDirectory = null, EntryReader? read = null)
    {
        var top = Resolve(root, baseDirectory, read);
        var tree = top.Ok ? ResolveParent(path, baseDirectory, read) : top;

        return tree.Ok ? Placed(top.Path, tree.Path, path, comparison, read ?? Lstat) : tree;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is <paramref name="root"/> or under it — both already resolved. The root plus a SEPARATOR, never a
    /// bare prefix: <c>/repo-secrets</c> starts with <c>/repo</c> and is not in it.
    /// </summary>
    public static bool IsInside(string root, string path, StringComparison comparison)
    {
        var top = Path.TrimEndingDirectorySeparator(root);
        var at = Path.TrimEndingDirectorySeparator(path);

        return string.Equals(top, at, comparison)
            || at.StartsWith(top.EndsWith(Path.DirectorySeparatorChar) ? top : top + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>A path as a sentence may carry it: control characters made visible, and cut when long.</summary>
    public static string Shown(string text)
    {
        var cut = text.Length > ShownChars ? $"{text[..ShownChars]}… ({text.Length} characters)" : text;

        return string.Create(cut.Length, cut, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = char.IsControl(source[i]) ? '?' : source[i];
            }
        });
    }

    // ---------- the folder / last-name split, and the guard's two questions ----------

    private readonly record struct Cut(string Folder, string Leaf);

    /// <summary>The folder part and the last name of a path — or null when there is no last name to keep apart.</summary>
    private static Cut? Split(string path)
    {
        if (Whole(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd(Separators);
        var at = trimmed.LastIndexOfAny(Separators);
        var leaf = trimmed[(at + 1)..];

        return leaf is "" or "." or ".." ? null : new Cut(FolderOf(trimmed, at), leaf);
    }

    /// <summary>Whether the path is a drive and a name with no separator after the colon (<c>C:child</c>) — only on Windows, where both <c>/</c> and <c>\</c> separate.</summary>
    /// <remarks>The OS fact is a parameter so a test decides it on any machine; <see cref="Split"/> passes this OS's. <c>C:child</c> is the current folder of drive C and is no name to split off: kept whole, <see cref="Resolve"/> refuses it.</remarks>
    internal static bool IsBareDriveName(string path, bool windows) =>
        windows && IsDriveQualified(path) && path.AsSpan(2).IndexOfAny('/', '\\') < 0;

    /// <summary>A path with no last name to keep apart: empty, a bare root, or a bare drive-relative name.</summary>
    private static bool Whole(string path) =>
        string.IsNullOrEmpty(path) || IsRoot(path) || IsBareDriveName(path, OperatingSystem.IsWindows());

    private static string FolderOf(string trimmed, int lastSeparator) => lastSeparator < 0 ? "." : trimmed[..(lastSeparator + 1)];

    /// <summary>
    /// Whether the path is only its own root: <c>/</c>, <c>C:\</c>, <c>\\server\share\</c>. <c>Path.GetPathRoot</c> knows every spelling a root has;
    /// string arithmetic on separators does not (<c>C:\</c> trims to <c>C:</c>, which is no folder and no name).
    /// </summary>
    private static bool IsRoot(string path)
    {
        var root = Path.GetPathRoot(path);

        return !string.IsNullOrEmpty(root) && string.Equals(path.TrimEnd(Separators), root.TrimEnd(Separators), StringComparison.Ordinal);
    }

    private static RealPathResult Joined(string path, RealPathResult folder, string leaf) =>
        folder.Ok
            ? Finished(path, Path.Join(folder.Path, leaf))
            : RealPathResult.Fail($"'{Shown(path)}' could not be resolved: the folder it is in cannot be — {folder.Why}");

    private static RealPathResult Placed(string top, string tree, string said, StringComparison comparison, EntryReader read)
    {
        var plain = NotALink(tree, said, read);

        return !plain.Ok ? plain : StrictlyInside(top, tree, said, comparison);
    }

    private static RealPathResult NotALink(string tree, string said, EntryReader read)
    {
        try
        {
            return read(tree).Kind == EntryKind.Link
                ? RealPathResult.Fail($"'{Shown(said)}' is a link itself, and a link is not a tree")
                : RealPathResult.Of(tree);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return RealPathResult.Fail($"'{Shown(said)}' could not be inspected: {Shown(e.Message)}");
        }
    }

    private static RealPathResult StrictlyInside(string top, string tree, string said, StringComparison comparison) =>
        !string.Equals(Path.TrimEndingDirectorySeparator(top), Path.TrimEndingDirectorySeparator(tree), comparison) && IsInside(top, tree, comparison)
            ? RealPathResult.Of(tree)
            : RealPathResult.Fail($"'{Shown(said)}' is not under '{Shown(top)}'");

    // ---------- the walk ----------

    private static RealPathResult Walk(string path, string? baseDirectory, EntryReader read)
    {
        if (Refusal(path) is { } refused)
        {
            return refused;
        }

        var here = baseDirectory ?? Directory.GetCurrentDirectory();

        return Anchor(Spelled(path, here), here) is { Root.Length: > 0 } start
            ? new Walker(path, read, start).Run()
            : RealPathResult.Fail($"'{Shown(path)}' is drive-relative or has no root, which this gate does not resolve");
    }

    private static RealPathResult? Refusal(string? path) =>
        string.IsNullOrEmpty(path) || path.Contains('\0') || path.Length > MaxPathChars ? NotAPath(path) : null;

    private static RealPathResult NotAPath(string? path) =>
        RealPathResult.Fail($"'{Shown(path ?? string.Empty)}' is not a path this gate resolves (empty, a NUL, or over {MaxPathChars} characters)");

    private static string Spelled(string path, string here) => NeedsBase(path) ? Path.Join(here, path) : path;

    /// <summary>
    /// One resolution in progress: the real folders found so far (<c>_chain</c>, the last is where the walk stands), the names still to do
    /// (<c>_pending</c>), the link expansions spent, and whether the walk stands ON a file. Every step answers null to go on or a result to stop.
    /// </summary>
    private sealed class Walker(string path, EntryReader read, Anchored start)
    {
        private readonly List<string> _chain = [start.Root];
        private readonly Stack<string> _pending = Seeded(start.Rest);
        private int _hops;
        private bool _onFile;

        public RealPathResult Run()
        {
            while (_pending.TryPop(out var segment))
            {
                if (Step(segment) is { } done)
                {
                    return done;
                }
            }

            return Finished(path, _chain[^1]);
        }

        private static Stack<string> Seeded(string rest)
        {
            var pending = new Stack<string>();
            Push(pending, rest);

            return pending;
        }

        private RealPathResult? Step(string segment)
        {
            if (_onFile)
            {
                // `file/.`, `file/..` and `file/name` are ENOTDIR on the disk: nothing is below a file — not even `.`, which is a name there —
                // so a skip or a pop to its neighbour would read a file nobody named.
                return RealPathResult.Fail($"'{Shown(path)}' goes below a file ('{Shown(_chain[^1])}') — the disk answers 'not a directory'");
            }

            if (segment == ".")
            {
                return null;
            }

            return segment == ".." ? Up() : Enter(segment);
        }

        /// <summary>The parent of what is resolved SO FAR, and never above the root.</summary>
        private RealPathResult? Up()
        {
            if (_chain.Count > 1)
            {
                _chain.RemoveAt(_chain.Count - 1);
            }

            return null;
        }

        private RealPathResult? Enter(string segment)
        {
            var entry = Path.Join(_chain[^1], segment);
            var seen = read(entry);

            return seen.Kind switch
            {
                EntryKind.Missing => Appended(path, _chain[^1], segment, _pending),
                EntryKind.Link => Follow(seen.LinkText),
                _ => Descend(entry, seen.Kind),
            };
        }

        private RealPathResult? Descend(string entry, EntryKind kind)
        {
            _chain.Add(entry);
            _onFile = kind == EntryKind.File;

            return null;
        }

        /// <summary>One link expansion: counted against the budget, its RAW text pushed in front of the rest.</summary>
        private RealPathResult? Follow(string text)
        {
            if (++_hops > MaxLinkExpansions)
            {
                return RealPathResult.Fail($"'{Shown(path)}' passes through more than {MaxLinkExpansions} links — a loop, or a chain nobody wrote on purpose");
            }

            if (Unusable(text))
            {
                return RealPathResult.Fail($"'{Shown(path)}' passes through a link whose text is empty, holds a NUL or is over {MaxPathChars} characters");
            }

            return Anchor(text, _chain[^1]) is { } target
                ? Pushed(target)
                : RealPathResult.Fail($"'{Shown(path)}' passes through a drive-relative link ('{Shown(text)}'), which this gate does not resolve");
        }

        private static bool Unusable(string text) => text.Length == 0 || text.Length > MaxPathChars || text.Contains('\0');

        /// <summary>An absolute target restarts at its own root; a relative one continues from the link's directory.</summary>
        private RealPathResult? Pushed(Anchored target)
        {
            if (target.Root.Length > 0)
            {
                _chain.Clear();
                _chain.Add(target.Root);
            }

            Push(_pending, target.Rest);

            return null;
        }
    }

    /// <summary>
    /// The walk ended at a name that is not there: nothing below it exists either, so the rest is appended as written — in one
    /// join, never walked again. A <c>..</c> among it fails: the disk would answer ENOENT, and popping would answer a folder nobody looked at.
    /// </summary>
    private static RealPathResult Appended(string said, string at, string missing, Stack<string> rest)
    {
        var names = new List<string>(rest.Count + 1) { missing };
        while (rest.TryPop(out var next))
        {
            if (next == "..")
            {
                return RealPathResult.Fail($"'{Shown(said)}' goes '..' after a name that is not there — the disk answers 'no such file', and resolving it as a folder nobody looked at would be a guess");
            }

            if (next != ".")
            {
                names.Add(next);
            }
        }

        return Finished(said, Path.Join(at, string.Join(Path.DirectorySeparatorChar, names)));
    }

    private static RealPathResult Finished(string said, string resolved) =>
        resolved.Length > MaxPathChars
            ? RealPathResult.Fail($"'{Shown(said)}' resolves to a path over {MaxPathChars} characters")
            : RealPathResult.Of(resolved);

    // ---------- where a path text is rooted ----------

    private readonly record struct Anchored(string Root, string Rest);

    /// <summary>
    /// Splits a path text into its root and the rest. No root (empty) means "continue from where the text was found". Null is a text this
    /// gate does not resolve: a drive-relative <c>C:x</c>. A Windows <c>\x</c> is rooted WITHOUT a drive, and its drive is the one of the
    /// place the text was found — the link's own volume — never the process's.
    /// </summary>
    private static Anchored? Anchor(string text, string foundIn) =>
        Path.IsPathFullyQualified(text) ? Qualified(text)
        : OperatingSystem.IsWindows() ? WindowsAnchor(text, foundIn)
        : new Anchored(string.Empty, text);

    private static Anchored Qualified(string text)
    {
        var root = Normalised(Path.GetPathRoot(text));

        return new Anchored(root, text[root.Length..]);
    }

    private static Anchored? WindowsAnchor(string text, string foundIn) =>
        StartsWithSeparator(text) ? new Anchored(Normalised(Path.GetPathRoot(foundIn)), text.TrimStart(Separators))
        : IsDriveQualified(text) ? null
        : new Anchored(string.Empty, text);

    private static string Normalised(string? root) => (root ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static bool StartsWithSeparator(string text) => text.Length > 0 && Array.IndexOf(Separators, text[0]) >= 0;

    /// <summary>A drive letter and a colon: <c>C:x</c>, which names the current folder of drive C and is not resolved.</summary>
    private static bool IsDriveQualified(string text) => text.Length > 1 && char.IsAsciiLetter(text[0]) && text[1] == ':';

    /// <summary>Whether a path must be joined to a base: it is neither fully qualified nor (on Windows) rooted or drive-qualified.</summary>
    private static bool NeedsBase(string path) =>
        !Path.IsPathFullyQualified(path)
        && !(OperatingSystem.IsWindows() && (StartsWithSeparator(path) || IsDriveQualified(path)));

    private static void Push(Stack<string> pending, string text)
    {
        var names = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = names.Length - 1; i >= 0; i--)
        {
            pending.Push(names[i]);
        }
    }
}
