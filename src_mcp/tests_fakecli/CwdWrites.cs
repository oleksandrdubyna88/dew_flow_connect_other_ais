using System.Text.Json;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The files a fake CODER leaves in its working folder (epoch 3 E2 s3 t3): the <c>writeFiles</c> field of a <c>FAKECLI_MODEL_MAP</c> entry is a JSON object of relative path to text, and each one is written
/// below the current directory before the fake answers — the way a real coder edits the worktree it stands in.
/// </summary>
/// <remarks>A path that is rooted or climbs out of the folder is refused with a line on stderr and exit 65 by the caller: a test double that wrote outside its folder would hide the very boundary a coder test is about.</remarks>
internal static class CwdWrites
{
    private const string Utf16Suffix = "#utf16le";

    /// <summary>Writes every entry; answers the sentence that says why one was not written, or empty when all were.</summary>
    public static string Apply(string json, string folder)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return "writeFiles must be a JSON object of relative path to text";
        }

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            // A name ending in `#utf16le` is written in UTF-16LE with no mark (the file a .gitattributes `working-tree-encoding=UTF-16LE` expects): the bytes on disk are NOT the text git commits.
            var utf16 = entry.Name.EndsWith(Utf16Suffix, StringComparison.Ordinal);
            var name = utf16 ? entry.Name[..^Utf16Suffix.Length] : entry.Name;
            var target = Path.GetFullPath(Path.Combine(folder, name));
            var below = Path.GetRelativePath(folder, target);
            if (Path.IsPathRooted(name) || below == ".." || below.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return $"writeFiles path '{entry.Name}' is not below the working folder";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var text = entry.Value.GetString() ?? string.Empty;
            if (utf16)
            {
                File.WriteAllBytes(target, System.Text.Encoding.Unicode.GetBytes(text));
            }
            else
            {
                File.WriteAllText(target, text);
            }
        }

        return string.Empty;
    }
}
