using System.Globalization;
using System.Text;
using CoaiMcp.Core.Collecting;

namespace CoaiMcp.Core.Toolchain;

/// <summary>The limits of the search for manifests at a commit (SPEC §A3). Over a limit is a refusal that names the number and the limit; nothing is ever cut.</summary>
public static class ManifestLimits
{
    /// <summary>A manifest is searched for at most this many FOLDERS deep (<c>a/b/c/d/package.json</c> is four).</summary>
    public const int MaxFolderDepth = 4;

    /// <summary>The manifest files a commit may hold inside the search.</summary>
    public const int MaxManifestFiles = 500;

    /// <summary>One manifest, in bytes (256 KiB).</summary>
    public const long MaxManifestBytes = 256 * 1024;

    /// <summary>All manifest texts read, in bytes (16 MiB).</summary>
    public const long MaxTotalBytes = 16L * 1024 * 1024;

    /// <summary>The listing of the tree, in bytes (32 MiB).</summary>
    public const long MaxListingBytes = 32L * 1024 * 1024;

    /// <summary>The projects a repository may have, all stacks together (SPEC §A3: 20 projects). Over it is a refusal.</summary>
    public const int MaxProjects = 20;

    /// <summary>The files recorded by their name only (a lock file, a <c>tsconfig.json</c>): their own bound, because their texts are never read.</summary>
    public const int MaxPresenceFiles = 5000;

    /// <summary>The submodules a notice names one by one; the rest are counted.</summary>
    public const int MaxSubmoduleNotes = 20;

    /// <summary>The folders no search enters, by name; a folder whose name starts with a dot is never entered either.</summary>
    public static readonly IReadOnlyList<string> SkippedFolders = ["node_modules", "bin", "obj"];
}

/// <summary>What a file found by the search is for.</summary>
public enum ManifestKind
{
    /// <summary>A manifest of a stack: read, and its text is a fact the detectors use.</summary>
    Manifest,

    /// <summary>A file that is part of a check definition (<c>global.json</c>, <c>*.props</c>): read, because a change of it is a change of the checks.</summary>
    Definition,

    /// <summary>A file whose NAME is the fact (a lock file, <c>tsconfig.json</c>): recorded, never read.</summary>
    Present,
}

/// <summary>One file the search found, with its object id, size and what it is for. <see cref="Text"/> is empty for a <see cref="ManifestKind.Present"/> file.</summary>
public sealed record ManifestFile(string Path, string Stack, ManifestKind Kind, string Oid, long Size, string Text);

/// <summary>The manifests of ONE commit: the files in path order, and the notes (a submodule that was not entered).</summary>
public sealed record ManifestSet(string Commit, IReadOnlyList<ManifestFile> Files, IReadOnlyList<string> Notes)
{
    /// <summary>The files of a kind, in path order.</summary>
    public IEnumerable<ManifestFile> Of(ManifestKind kind) => Files.Where(file => file.Kind == kind);
}

/// <summary>The answer to "what manifests does this commit have": the set, or the sentence that says why it cannot be told. A refusal is never an empty set.</summary>
public abstract record ManifestRead
{
    /// <summary>The set of a commit.</summary>
    public sealed record Found(ManifestSet Set) : ManifestRead;

    /// <summary>Nothing was decided: the sentence names the path or the number and the limit.</summary>
    public sealed record Refused(string Sentence) : ManifestRead;

    private ManifestRead() { }
}

/// <summary>A file the search selected from a listing, not yet read.</summary>
public sealed record ManifestCandidate(string Path, string Stack, ManifestKind Kind, string Oid, long Size);

/// <summary>What the pure selection decided: the files to read and the notes, or a refusal.</summary>
public abstract record ManifestSelection
{
    public sealed record Selected(IReadOnlyList<ManifestCandidate> Candidates, IReadOnlyList<string> Notes) : ManifestSelection;

    public sealed record Refused(string Sentence) : ManifestSelection;

    private ManifestSelection() { }
}

/// <summary>
/// The pure half of "the manifests of a commit": from the entries of <c>git ls-tree -r -l -z</c> of a commit, which files are the search's (the names of <see cref="ToolchainDefaults"/>, at most four folders deep, outside <c>node_modules</c>, <c>bin</c>, <c>obj</c> and the folders whose name starts with a dot),
/// and which refusals of SPEC §A3 apply BEFORE a byte is read: a link, a submodule (named, never entered), a path that is not a plain relative one, a manifest over its size, and every limit.
/// </summary>
public static class ManifestSearch
{
    /// <summary>Selects from a parsed listing. Never throws; a listing that did not parse is a refusal that names the head of the bad record.</summary>
    public static ManifestSelection Select(TreeListing listing, ToolchainDefaults table)
    {
        if (listing.Malformed.Length > 0)
        {
            return new ManifestSelection.Refused($"the listing of the tree has a record coai cannot read (after {listing.Entries.Count.ToString(CultureInfo.InvariantCulture)} readable records), so no manifest was looked for");
        }

        var candidates = new List<ManifestCandidate>();
        var submodules = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in listing.Entries)
        {
            if (entry.Type == "commit")
            {
                submodules.Add(entry.Path);

                continue;
            }

            if (ClassifyNamed(entry.Path, table) is not { } kind)
            {
                if (entry.Type == "blob" && IsGeneratedImport(entry.Path))
                {
                    // MSBuild imports obj/<project>.*.props and *.targets by a wildcard of its own, and the search never enters obj: the NAME of such a file is recorded (never read) so that a head that adds one is a change of the check definition.
                    candidates.Add(new ManifestCandidate(entry.Path, "dotnet", ManifestKind.Present, entry.Oid, entry.Size));
                }

                continue;
            }

            if (kind.Kind != ManifestKind.Present && !kind.Exact)
            {
                return new ManifestSelection.Refused($"the file {PathSpelling.Spell(entry.Path, entry.Oid)} is named like a manifest or a definition file with a different case: a tool on a file system without case would read it and one with case would not, so coai does not guess; rename it, or name the commands in {ToolchainTables.FilePath}");
            }

            if (PathProblem(entry.Path, entry.Oid) is { Length: > 0 } problem)
            {
                return new ManifestSelection.Refused(problem);
            }

            if (!seen.Add(entry.Path))
            {
                return new ManifestSelection.Refused($"the path {PathSpelling.Spell(entry.Path, entry.Oid)} is listed twice, so what it names cannot be told; name the commands in {ToolchainTables.FilePath}");
            }

            if (entry.Mode == "120000")
            {
                return new ManifestSelection.Refused($"{PathSpelling.Spell(entry.Path, entry.Oid)} is a link: a link is never read, because it can point out of the repository; name the commands in {ToolchainTables.FilePath}");
            }

            if (entry.Type != "blob")
            {
                continue;
            }

            if (kind.Kind != ManifestKind.Present && entry.Size > ManifestLimits.MaxManifestBytes)
            {
                return new ManifestSelection.Refused($"{PathSpelling.Spell(entry.Path, entry.Oid)} is {entry.Size.ToString(CultureInfo.InvariantCulture)} bytes, over the limit of {ManifestLimits.MaxManifestBytes.ToString(CultureInfo.InvariantCulture)} for one manifest; name the commands in {ToolchainTables.FilePath}");
            }

            candidates.Add(new ManifestCandidate(entry.Path, kind.Stack, kind.Kind, entry.Oid, entry.Size));
        }

        var read = candidates.Where(candidate => candidate.Kind != ManifestKind.Present).ToList();
        if (read.Count > ManifestLimits.MaxManifestFiles)
        {
            return new ManifestSelection.Refused($"{read.Count.ToString(CultureInfo.InvariantCulture)} manifest files were found, over the limit of {ManifestLimits.MaxManifestFiles.ToString(CultureInfo.InvariantCulture)}; name the commands in {ToolchainTables.FilePath}");
        }

        var present = candidates.Count - read.Count;
        if (present > ManifestLimits.MaxPresenceFiles)
        {
            return new ManifestSelection.Refused($"{present.ToString(CultureInfo.InvariantCulture)} lock and configuration files were found, over the limit of {ManifestLimits.MaxPresenceFiles.ToString(CultureInfo.InvariantCulture)}; name the commands in {ToolchainTables.FilePath}");
        }

        var total = read.Sum(candidate => candidate.Size);
        if (total > ManifestLimits.MaxTotalBytes)
        {
            return new ManifestSelection.Refused($"the manifests hold {total.ToString(CultureInfo.InvariantCulture)} bytes in all, over the limit of {ManifestLimits.MaxTotalBytes.ToString(CultureInfo.InvariantCulture)}; name the commands in {ToolchainTables.FilePath}");
        }

        return new ManifestSelection.Selected([.. candidates.OrderBy(candidate => candidate.Path, StringComparer.Ordinal)], Notes(submodules));
    }

    /// <summary>
    /// The refusal of a path that is not a plain relative one — a control, format, separator or replacement character, an absolute or drive form, a <c>..</c>, a folder name that starts with <c>-</c> (a command would read it as an option) or a path that starts with <c>@</c> (MSBuild reads it as a
    /// response file) — or empty. The path is spelled by <see cref="PathSpelling"/>, never raw.
    /// </summary>
    public static string PathProblem(string path, string oid = "") =>
        HasUnsafeCharacter(path) ? $"the path {PathSpelling.Spell(path, oid)} holds a control, format, separator or replacement character, so it was not read"
        : !RepoPaths.IsRelative(path) ? $"the path {PathSpelling.Spell(path, oid)} is not a path inside the repository (absolute, a drive or a '..'), so it was not read"
        : path.Split('/').Any(segment => segment.StartsWith('-')) || path.StartsWith('@') ? $"the path {PathSpelling.Spell(path, oid)} has a name that starts with '-' or '@', which a command reads as an option or a response file, so it was not read; rename it, or name the commands in {ToolchainTables.FilePath}"
        : string.Empty;

    /// <summary>True when a path holds a character that is not an ordinary one: control, format (U+200B, U+202E, U+FEFF…), line or paragraph separator, a space but the plain one, a private-use, unassigned or lone surrogate character, or U+FFFD (a byte that was not UTF-8).</summary>
    public static bool HasUnsafeCharacter(string path)
    {
        foreach (var rune in path.EnumerateRunes())
        {
            if (rune.Value == 0xFFFD || (Rune.IsWhiteSpace(rune) && rune.Value != ' '))
            {
                return true;
            }

            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned or UnicodeCategory.Surrogate)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>What a path is for, by its name and its place — or null when the search does not look at it. Names are matched without case (see <see cref="ClassifyNamed"/>).</summary>
    public static (string Stack, ManifestKind Kind)? Classify(string path, ToolchainDefaults table) =>
        ClassifyNamed(path, table) is { } named ? (named.Stack, named.Kind) : null;

    /// <summary>
    /// <see cref="Classify"/> with whether the name matched EXACTLY: a name that matches only without case (<c>Package.json</c>, <c>Directory.Build.TARGETS</c>) is the odd spelling that a tool on a file system without case would read and one with case would not (S6 of the s2 security verify).
    /// </summary>
    public static (string Stack, ManifestKind Kind, bool Exact)? ClassifyNamed(string path, ToolchainDefaults table)
    {
        var segments = path.Split('/');
        if (segments.Length - 1 > ManifestLimits.MaxFolderDepth || segments.Take(segments.Length - 1).Any(Skipped))
        {
            return null;
        }

        var name = segments[^1];
        foreach (var comparison in new[] { StringComparison.Ordinal, StringComparison.OrdinalIgnoreCase })
        {
            foreach (var stack in table.Stacks.Values)
            {
                if (stack.Manifests.Any(pattern => Matches(pattern, name, comparison)))
                {
                    return (stack.Id, ManifestKind.Manifest, comparison == StringComparison.Ordinal);
                }

                if (stack.DefinitionFiles.Any(pattern => Matches(pattern, name, comparison)))
                {
                    return (stack.Id, ManifestKind.Definition, comparison == StringComparison.Ordinal);
                }

                if (stack.Presence.Any(pattern => Matches(pattern, name, comparison)))
                {
                    return (stack.Id, ManifestKind.Present, comparison == StringComparison.Ordinal);
                }
            }
        }

        return null;
    }

    /// <summary>A <c>.props</c> or <c>.targets</c> file in an <c>obj</c> folder, at any depth: MSBuild imports <c>obj/&lt;project&gt;.*.props</c> and <c>.targets</c> by itself.</summary>
    public static bool IsGeneratedImport(string path)
    {
        var segments = path.Split('/');

        return segments.Length > 1 && segments.Take(segments.Length - 1).Any(segment => string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase))
            && (path.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".targets", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A name against a pattern with at most one <c>*</c> (<c>*.csproj</c>, <c>.prettierrc*</c>, <c>prettier.config.*</c>); ordinal, case-sensitive, never a path.</summary>
    public static bool Matches(string pattern, string name, StringComparison comparison = StringComparison.Ordinal)
    {
        var star = pattern.IndexOf('*', StringComparison.Ordinal);

        return star < 0
            ? string.Equals(pattern, name, comparison)
            : name.Length >= pattern.Length - 1 && name.StartsWith(pattern[..star], comparison) && name.EndsWith(pattern[(star + 1)..], comparison);
    }

    private static bool Skipped(string folder) => folder.StartsWith('.') || ManifestLimits.SkippedFolders.Contains(folder, StringComparer.Ordinal);

    private static List<string> Notes(List<string> submodules)
    {
        var notes = submodules.OrderBy(path => path, StringComparer.Ordinal).Take(ManifestLimits.MaxSubmoduleNotes).Select(path => $"submodule {PathSpelling.Spell(path)} was not entered").ToList();
        if (submodules.Count > ManifestLimits.MaxSubmoduleNotes)
        {
            notes.Add($"{(submodules.Count - ManifestLimits.MaxSubmoduleNotes).ToString(CultureInfo.InvariantCulture)} more submodules were not entered");
        }

        return notes;
    }
}
