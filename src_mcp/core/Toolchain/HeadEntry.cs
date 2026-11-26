namespace CoaiMcp.Core.Toolchain;

/// <summary>
/// Reads one entry of <c>git ls-tree -l -z</c> (E4 s5 verify pass 2, S9): a file name taken from a test's output is a "file of the head" only when the head's listing holds it as a REGULAR FILE — a blob with mode 100644 or 100755. A folder, a link (120000), a submodule (160000), a path that is not
/// there and a listing of another path are not.
/// </summary>
public static class HeadEntry
{
    /// <summary>Whether <paramref name="raw"/> (the raw answer, entries <c>mode type oid size\tpath</c> ended by NUL) holds <paramref name="path"/> as a regular file.</summary>
    public static bool IsRegularFile(string raw, string path)
    {
        foreach (var entry in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0 || !string.Equals(entry[(tab + 1)..], path, StringComparison.Ordinal))
            {
                continue;
            }

            var fields = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            return fields.Length >= 2 && fields[1] == "blob" && fields[0] is "100644" or "100755";
        }

        return false;
    }
}
