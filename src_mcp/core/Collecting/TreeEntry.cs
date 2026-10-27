using System.Globalization;

namespace CoaiMcp.Core.Collecting;

/// <summary>One <c>ls-tree -l -z</c> record. <paramref name="Size"/> is -1 where git printed <c>-</c> (a submodule, a directory).</summary>
public sealed record TreeEntry(string Mode, string Type, string Oid, long Size, string Path);

/// <summary>Every record parsed, or — when one could not be — the entries before it and the head of the bad record.</summary>
public sealed record TreeListing(IReadOnlyList<TreeEntry> Entries, string Malformed);

/// <summary>
/// Reads <c>git ls-tree -l -z</c> output — <c>mode type oid size\tpath\0</c> — into what git SAID, before any rule about
/// what a plan may be.
/// </summary>
/// <remarks>
/// <para><b>The size is right-aligned in at least seven columns</b> (<c>      11</c>, <c>  263170</c>, one space before
/// <c>10000000</c>), so the text before the first tab is split on spaces with the empty pieces removed — that absorbs
/// the padding whatever its width. The path is everything AFTER the first tab: under <c>-z</c> git does not quote it, and
/// a path may itself hold a tab.</para>
/// <para>Nothing throws. A record that is not this shape stops the parse and is named, with the entries read before it
/// kept: a reader that guessed at a record it did not understand would be deciding what a plan is from a string it
/// could not read.</para>
/// </remarks>
public static class TreeEntries
{
    private const int HeadChars = 60;

    private static readonly TreeEntry None = new(string.Empty, string.Empty, string.Empty, -1, string.Empty);

    public static TreeListing Parse(string output)
    {
        var seen = new List<TreeEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TryEntry(record, out var entry))
            {
                return new TreeListing(seen, Head(record));
            }

            seen.Add(entry);
        }

        return new TreeListing(seen, string.Empty);
    }

    private static bool TryEntry(string record, out TreeEntry entry)
    {
        entry = None;
        var tab = record.IndexOf('\t');
        if (tab < 0)
        {
            return false;
        }

        var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return fields.Length == 4 && TryOf(fields, record[(tab + 1)..], out entry);
    }

    private static bool TryOf(string[] fields, string path, out TreeEntry entry)
    {
        entry = None;
        if (!(IsShape(fields) && TrySize(fields[3], out var size)))
        {
            return false;
        }

        entry = new TreeEntry(fields[0], fields[1], fields[2], size, path);

        return true;
    }

    private static bool IsShape(string[] fields) =>
        IsMode(fields[0]) && fields[1].All(char.IsAsciiLetterLower) && IsOid(fields[2]);

    private static bool IsMode(string mode) => mode.Length == 6 && mode.All(char.IsAsciiDigit);

    /// <summary>Forty hex for SHA-1, sixty-four for SHA-256 — git's shape, lower case as git prints it.</summary>
    private static bool IsOid(string oid) => oid.Length is 40 or 64 && oid.All(char.IsAsciiHexDigitLower);

    private static bool TrySize(string text, out long size)
    {
        if (text == "-")
        {
            size = -1;

            return true;
        }

        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out size);
    }

    private static string Head(string record) => record[..Math.Min(HeadChars, record.Length)];
}
