namespace CoaiMcp.Runners.Files;

/// <summary>What one path entry is, by lstat-level facts: not there, there and not a link, or a link and its text.</summary>
public enum EntryKind
{
    /// <summary>Nothing is at the path (and no link either — a dangling link is <see cref="Link"/>).</summary>
    Missing,

    /// <summary>Something is there and it is not a link.</summary>
    Plain,

    /// <summary>A link — symbolic link, or a Windows junction — whether or not its target exists.</summary>
    Link,
}

/// <summary>One path entry, looked at WITHOUT following it.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="LinkText">The link's own text exactly as stored — raw, never resolved; empty unless <paramref name="Kind"/> is <see cref="EntryKind.Link"/>.</param>
public readonly record struct Entry(EntryKind Kind, string LinkText)
{
    public static Entry Missing { get; } = new(EntryKind.Missing, string.Empty);

    public static Entry Plain { get; } = new(EntryKind.Plain, string.Empty);

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
/// <para><b>Everything else fails.</b> Over the budget, a loop (which only the budget ends), a NUL, an empty link text, a drive-relative
/// <c>C:x</c>, a path over <see cref="MaxPathChars"/> characters, and any IO error are a <see cref="RealPathResult"/> that is not
/// <see cref="RealPathResult.Ok"/>: every caller turns it into refuse / not ours / cannot compare. None of them falls back to the spelling.</para>
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
            return info.LinkTarget is { } text ? Entry.Link(text) : Entry.Plain;
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
    public static RealPathResult ResolveParent(string path, string? baseDirectory = null, EntryReader? read = null)
    {
        var trimmed = path.TrimEnd(Separators);
        var cut = trimmed.LastIndexOfAny(Separators);
        var leaf = trimmed[(cut + 1)..];
        if (leaf.Length == 0 || leaf is "." or "..")
        {
            return Resolve(path, baseDirectory, read);
        }

        var parent = Resolve(cut < 0 ? "." : trimmed[..(cut + 1)], baseDirectory, read);

        return parent.Ok ? RealPathResult.Of(Path.Join(parent.Path, leaf)) : parent;
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

    private static RealPathResult Walk(string path, string? baseDirectory, EntryReader read)
    {
        if (path is null || path.Length == 0 || path.Contains('\0') || path.Length > MaxPathChars)
        {
            return RealPathResult.Fail($"'{Shown(path ?? string.Empty)}' is not a path this gate resolves (empty, a NUL, or over {MaxPathChars} characters)");
        }

        var here = baseDirectory ?? Directory.GetCurrentDirectory();
        var spelled = NeedsBase(path) ? Path.Join(here, path) : path;
        if (Anchor(spelled, here) is not { Root.Length: > 0 } start)
        {
            return RealPathResult.Fail($"'{Shown(path)}' is drive-relative or has no root, which this gate does not resolve");
        }

        var chain = new List<string> { start.Root };
        var pending = new Stack<string>();
        Push(pending, start.Rest);
        var hops = 0;
        while (pending.TryPop(out var segment))
        {
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    // The parent of what is resolved SO FAR, and never above the root.
                    if (chain.Count > 1)
                    {
                        chain.RemoveAt(chain.Count - 1);
                    }

                    continue;
            }

            var entry = Path.Join(chain[^1], segment);
            var seen = read(entry);
            switch (seen.Kind)
            {
                case EntryKind.Missing:
                    return Appended(path, chain[^1], segment, pending);
                case EntryKind.Plain:
                    chain.Add(entry);
                    continue;
            }

            if (++hops > MaxLinkExpansions)
            {
                return RealPathResult.Fail($"'{Shown(path)}' passes through more than {MaxLinkExpansions} links — a loop, or a chain nobody wrote on purpose");
            }

            if (seen.LinkText.Length == 0 || seen.LinkText.Contains('\0') || seen.LinkText.Length > MaxPathChars)
            {
                return RealPathResult.Fail($"'{Shown(path)}' passes through a link whose text is empty, holds a NUL or is over {MaxPathChars} characters");
            }

            if (Anchor(seen.LinkText, chain[^1]) is not { } target)
            {
                return RealPathResult.Fail($"'{Shown(path)}' passes through a drive-relative link ('{Shown(seen.LinkText)}'), which this gate does not resolve");
            }

            if (target.Root.Length > 0)
            {
                chain.Clear();
                chain.Add(target.Root);
            }

            Push(pending, target.Rest);
        }

        return Finished(path, chain[^1]);
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

    private readonly record struct Anchored(string Root, string Rest);

    /// <summary>
    /// Splits a path text into its root and the rest. No root (empty) means "continue from where the text was found". Null is a text this
    /// gate does not resolve: a drive-relative <c>C:x</c>. A Windows <c>\x</c> is rooted WITHOUT a drive, and its drive is the one of the
    /// place the text was found — the link's own volume — never the process's.
    /// </summary>
    private static Anchored? Anchor(string text, string foundIn)
    {
        if (Path.IsPathFullyQualified(text))
        {
            var root = (Path.GetPathRoot(text) ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            return new Anchored(root, text[root.Length..]);
        }

        if (!OperatingSystem.IsWindows())
        {
            return new Anchored(string.Empty, text);
        }

        if (text.Length > 0 && Array.IndexOf(Separators, text[0]) >= 0)
        {
            var volume = (Path.GetPathRoot(foundIn) ?? string.Empty).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            return new Anchored(volume, text.TrimStart(Separators));
        }

        return text.Length > 1 && char.IsAsciiLetter(text[0]) && text[1] == ':' ? null : new Anchored(string.Empty, text);
    }

    /// <summary>Whether a path must be joined to a base: it is neither fully qualified nor (on Windows) rooted or drive-qualified.</summary>
    private static bool NeedsBase(string path) =>
        !Path.IsPathFullyQualified(path)
        && !(OperatingSystem.IsWindows() && (path.Length > 0 && Array.IndexOf(Separators, path[0]) >= 0 || path.Length > 1 && path[1] == ':'));

    private static void Push(Stack<string> pending, string text)
    {
        var names = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = names.Length - 1; i >= 0; i--)
        {
            pending.Push(names[i]);
        }
    }
}
