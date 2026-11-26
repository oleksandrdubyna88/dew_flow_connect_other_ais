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
public static class FrozenDefinitions
{
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
        var frozen = changes.Count(change => !change.IsLinkOrSubmodule && IsFrozen(change.Path, table));
        if (links + frozen == 0)
        {
            return string.Empty;
        }

        return $"the head changed {frozen.ToString(CultureInfo.InvariantCulture)} frozen build definition path(s) and {links.ToString(CultureInfo.InvariantCulture)} symlink or submodule path(s) against the base; what the checks build and test is decided by the base commit, "
            + "so a job cannot add or change a project, a package, a lock file, a runner configuration or a link, and nothing was run. This is an interim rule (the owner decides): land such a change in a commit of its own, or take it in by hand.";
    }
}
