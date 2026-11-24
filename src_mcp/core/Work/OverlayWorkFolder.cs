using System.Text;

namespace CoaiMcp.Core.Work;

/// <summary>
/// A feature folder plus files that are not on disk yet: what the folder WOULD be if the BA's output were written (spec C: "coai validates an overlay (disk + output)"). Reads go to the overlay first and to the inner folder second; a
/// folder exists in the overlay when a file lies below it. It never writes, and the inner folder is never changed — so the plan check of a BA's output touches nothing until the output is accepted.
/// </summary>
/// <remarks>
/// Paths are those of <see cref="IWorkFolder"/>: relative to the feature folder, written with <c>/</c>. A file of the overlay replaces a file of the inner folder of the same path; a file the inner folder holds and the overlay
/// does not is read as it is.
/// </remarks>
public sealed class OverlayWorkFolder(IWorkFolder inner, IReadOnlyDictionary<string, string> files) : IWorkFolder
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public IReadOnlyList<string> Folders(string relative)
    {
        var prefix = relative.Length == 0 ? string.Empty : relative.TrimEnd('/') + "/";
        var added = files.Keys
            .Where(path => path.StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => path[prefix.Length..])
            .Where(rest => rest.Contains('/', StringComparison.Ordinal))
            .Select(rest => rest[..rest.IndexOf('/', StringComparison.Ordinal)]);

        return [.. inner.Folders(relative).Concat(added).Distinct(StringComparer.Ordinal)];
    }

    public WorkFile Read(string relative, int maxBytes)
    {
        if (!files.TryGetValue(relative, out var text))
        {
            return inner.Read(relative, maxBytes);
        }

        var bytes = Utf8.GetByteCount(text);

        return bytes > maxBytes ? new WorkFile.TooLarge(bytes) : new WorkFile.Text(text);
    }
}
