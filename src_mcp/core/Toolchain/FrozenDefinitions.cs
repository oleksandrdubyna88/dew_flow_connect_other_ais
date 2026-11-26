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

/// <summary>
/// <b>Frozen build definitions — the interim gate (E4 s3, pass 2 of the security verify; INTERIM, the owner decides).</b> A coder's head is refused, before any step and for detected and stated commands alike, when relative to the base it added, deleted, modified or changed the mode of a path whose file name is on the
/// frozen list of <c>shared/toolchains.json</c> (<see cref="ToolchainDefaults.Frozen"/>), or of any symlink or submodule anywhere. The compare is of the WHOLE tree of the two commits — paths, modes and ids from <c>git diff-tree</c> in the main repository through the hardened reads —, not of the facts a
/// reader happened to extract, so a new property, a second JSON key, a new import, a lock file's address or a file outside the unit's folder cannot get past it by being something a list of names did not know (M7-M12 of the verify).
/// </summary>
/// <remarks>
/// <para>The sentence says counts and never a path, a name or a value of the head: the head is untrusted text. What this blocks, by design: a coder cannot add a package or a test project, or change a build file; such a head is refused and a person can take it in by hand. A script or helper file that a frozen
/// definition invokes (<c>scripts/test.sh</c>) is not frozen, and the base's own tests can be edited by the head: the owner's question.</para>
/// <para>This file names no frozen file: the list is data (<c>shared/toolchains.json</c>), and a scan test holds the code to it.</para>
/// </remarks>
/// <summary>How many test files a head modified, added and deleted.</summary>
public sealed record TestFileCounts(int Modified, int Added, int Deleted);

public static class FrozenDefinitions
{
    /// <summary>
    /// <b>The allow list (pass 3, M15).</b> Whether a head may change this path: its extension (the last one, without case) is in <see cref="ToolchainDefaults.AllowedSourceExtensions"/>, its base name does not match <see cref="ToolchainDefaults.ConfigNamePattern"/>, and it is not a name of the frozen lists.
    /// A name with no extension, a dot file, a trailing dot or space, a config-shaped name — each is refused. A regular expression that does not answer in time refuses too.
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

    /// <summary>The paths of a head that are not allowed changes, in the listing's order: what the job's record names so that a person can tell what to move to a commit of its own.</summary>
    public static IReadOnlyList<string> RefusedPaths(IReadOnlyList<RawTreeChange> changes, ToolchainDefaults table) =>
        [.. changes.Where(change => change.IsLinkOrSubmodule || !IsAllowedChange(change.Path, table)).Select(change => change.Path)];

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

    /// <summary>The sentence that refuses a head, or empty when nothing frozen and no link or submodule changed. <paramref name="changes"/> null (unreadable) or <paramref name="cut"/> (too long) is refused too: a partial list decides nothing.</summary>
    public static string Refusal(IReadOnlyList<RawTreeChange>? changes, bool cut, ToolchainDefaults table)
    {
        if (changes is null || cut)
        {
            return "the changes of the head against the base could not be read completely, so whether it changed a frozen build definition is not known and nothing was run (interim rule: the owner decides)";
        }

        var links = changes.Count(change => change.IsLinkOrSubmodule);
        var frozen = changes.Count(change => !change.IsLinkOrSubmodule && !IsAllowedChange(change.Path, table));
        if (links + frozen == 0)
        {
            return string.Empty;
        }

        return $"the head changed {frozen.ToString(CultureInfo.InvariantCulture)} frozen build definition path(s) and {links.ToString(CultureInfo.InvariantCulture)} symlink or submodule path(s) against the base (a path counts as a build definition unless it is a source file of the allowed kinds with a name that is not a configuration's); what the checks build and test is decided by the base commit, "
            + "so a job cannot add or change a project, a package, a lock file, a runner configuration or a link, and nothing was run. This is an interim rule (the owner decides): land such a change in a commit of its own, or take it in by hand.";
    }
}
