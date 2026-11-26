using System.Text.RegularExpressions;
using System.Globalization;

namespace CoaiMcp.Core.Toolchain;

/// <summary>One record of <c>git diff-tree -r -z --no-renames --raw</c>: what a commit changed against another, with the modes and object ids on both sides.</summary>
/// <param name="OldMode">The mode in the base ("000000" for an added path).</param>
/// <param name="NewMode">The mode in the head ("000000" for a deleted path).</param>
/// <param name="OldId">The blob (or commit, for a submodule) id in the base.</param>
/// <param name="NewId">The id in the head.</param>
/// <param name="Status">The letter git gave: A, D, M or T.</param>
/// <param name="Path">The path, raw: never spelled into a sentence.</param>
public sealed record RawTreeChange(string OldMode, string NewMode, string OldId, string NewId, char Status, string Path)
{
    /// <summary>Whether either side is a symbolic link (120000) or a submodule (160000).</summary>
    public bool IsLinkOrSubmodule => OldMode is "120000" or "160000" || NewMode is "120000" or "160000";
}

/// <summary>How many test files a head modified, added and deleted.</summary>
public sealed record TestFileCounts(int Modified, int Added, int Deleted);

/// <summary>What a head changed that is not a plain source file: the count of definition and configuration files, the count of symlinks and submodules, and every such path (raw).</summary>
public sealed record ChangedFiles(int Definitions, int Links, IReadOnlyList<string> Paths)
{
    public int Total => Definitions + Links;
}

/// <summary>
/// <b>The classifier of the report "what the head changed in the definitions of the checks"</b> (owner decision 2026-11-26, which replaced the interim gate "frozen build definitions" of E4 s3 passes 2 and 3: nothing the head changes is REFUSED any more). The lists of
/// <c>shared/toolchains.json</c> (<see cref="ToolchainDefaults.Frozen"/>, the allowed source extensions and the configuration-name pattern) only decide which changed paths the envelope and the job's record call a definition or configuration file; a symlink or a submodule is counted apart. The comparison
/// is of the WHOLE tree of the two commits — paths and modes from <c>git diff-tree</c> in the main repository through the hardened reads — so a new property, a second JSON key, a lock file's address or a file outside the unit's folder is reported whatever a reader happened to extract.
/// </summary>
/// <remarks>This file names no file: the list is data, and a scan test holds the code to it.</remarks>
public static class FrozenDefinitions
{
    /// <summary>
    /// <b>The classifier's allow list (pass 3, M15).</b> Whether this path is a plain source file — one a coder edits as a matter of course, and the report does not call a definition or configuration file: its extension (the last one, without case) is in <see cref="ToolchainDefaults.AllowedSourceExtensions"/>, its base name does not match <see cref="ToolchainDefaults.ConfigNamePattern"/>, and it is not a name of the frozen lists.
    /// A name with no extension, a dot file, a trailing dot or space, a config-shaped name — each is NOT plain source. A regular expression that does not answer in time refuses too.
    /// </summary>
    public static bool IsAllowedChange(string path, ToolchainDefaults table)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        if (dot <= 0 || !table.AllowedSourceExtensions.Contains(name[dot..], StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return !table.ConfigName.IsMatch(name) && !IsFrozen(path, table);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// How many test files the head modified, added and deleted (S21 of pass 3): a file whose name or whose folder reads as a test (<c>tests/</c>, <c>__tests__</c>, <c>spec/</c>, <c>*.test.*</c>, <c>*.spec.*</c>, <c>*Tests.*</c>, <c>*Test.*</c>), counted by the listing's status. The gate lets the head edit tests; this makes it visible.
    /// </summary>
    public static TestFileCounts TestFiles(IReadOnlyList<RawTreeChange> changes)
    {
        var tests = changes.Where(change => LooksLikeATest(change.Path)).ToList();

        return new TestFileCounts(tests.Count(change => change.Status is 'M' or 'T'), tests.Count(change => change.Status == 'A'), tests.Count(change => change.Status == 'D'));
    }

    private static bool LooksLikeATest(string path)
    {
        var segments = path.Split('/');
        var name = segments[^1];
        var stem = name.Contains('.', StringComparison.Ordinal) ? name[..name.IndexOf('.', StringComparison.Ordinal)] : name;

        return segments[..^1].Any(segment => segment.ToLowerInvariant() is "test" or "tests" or "__tests__" or "spec" or "specs")
            || name.Contains(".test.", StringComparison.OrdinalIgnoreCase) || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || stem.EndsWith("Tests", StringComparison.OrdinalIgnoreCase) || stem.EndsWith("Test", StringComparison.OrdinalIgnoreCase) || stem.EndsWith("Spec", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The most characters of a raw listing read; a longer one is cut and refused (a head that changed that many paths is not decided by a partial list).</summary>
    public const int MaxChars = 8 * 1024 * 1024;

    /// <summary>Reads <c>git diff-tree -r -z --no-renames --raw</c> output (<c>:oldmode newmode oldid newid STATUS NUL path NUL</c>). Anything that does not read exactly gives null: the caller refuses.</summary>
    public static IReadOnlyList<RawTreeChange>? ParseRaw(string output)
    {
        var parts = output.Split('\0');
        var changes = new List<RawTreeChange>();
        var index = 0;
        while (index < parts.Length && parts[index].Length > 0)
        {
            var fields = parts[index].Split(' ');
            if (fields.Length != 5 || fields[0].Length < 2 || fields[0][0] != ':' || fields[4].Length != 1 || !"ADMT".Contains(fields[4][0]) || index + 1 >= parts.Length || parts[index + 1].Length == 0)
            {
                return null;
            }

            changes.Add(new RawTreeChange(fields[0][1..], fields[1], fields[2], fields[3], fields[4][0], parts[index + 1]));
            index += 2;
        }

        return index == parts.Length - 1 || index == parts.Length ? changes : null;
    }

    /// <summary>Whether <paramref name="path"/> is a frozen build definition: its file name, or its last segments for a pattern with a '/', matches a pattern of the table, without case.</summary>
    public static bool IsFrozen(string path, ToolchainDefaults table)
    {
        var segments = path.Split('/');
        foreach (var pattern in table.FrozenPatterns)
        {
            var parts = pattern.Split('/');
            if (parts.Length > segments.Length)
            {
                continue;
            }

            var tail = segments[^parts.Length..];
            if (parts.Zip(tail).All(pair => ManifestSearch.Matches(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The changed paths a report calls a definition or configuration file (not a plain source file of the allowed kinds), and the symlinks and submodules, counted apart.</summary>
    public static ChangedFiles Classify(IReadOnlyList<RawTreeChange> changes, ToolchainDefaults table)
    {
        var links = changes.Where(change => change.IsLinkOrSubmodule).ToList();
        var definitions = changes.Where(change => !change.IsLinkOrSubmodule && !IsAllowedChange(change.Path, table)).ToList();

        return new ChangedFiles(definitions.Count, links.Count, [.. changes.Where(change => change.IsLinkOrSubmodule || !IsAllowedChange(change.Path, table)).Select(change => change.Path)]);
    }
}
