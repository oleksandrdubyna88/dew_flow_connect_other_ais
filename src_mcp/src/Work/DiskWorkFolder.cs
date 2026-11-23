using System.Text;
using CoaiMcp.Core.Work;

namespace CoaiMcp.Work;

/// <summary>
/// A feature folder in the WORKING TREE, read for <c>validate_work</c>. Reads only: the validator never writes. A symbolic link is never followed — a folder that is a link is not a
/// folder here and a file that is a link is not a file — so a link inside the plan cannot lead the read out of it (the walk of <c>--plan-report</c> keeps the same rule). A read that
/// fails (a permission, a disk fault) throws, and the mode turns it into exit 74; text that is not UTF-8 is an answer, <see cref="WorkFile.NotUtf8"/>.
/// </summary>
internal sealed class DiskWorkFolder(string root) : IWorkFolder
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public IReadOnlyList<string> Folders(string relative)
    {
        var path = Resolve(relative);
        if (!Directory.Exists(path))
        {
            return [];
        }

        return [.. new DirectoryInfo(path).EnumerateDirectories().Where(folder => !IsLink(folder)).Select(folder => folder.Name)];
    }

    public WorkFile Read(string relative, int maxBytes)
    {
        var info = new FileInfo(Resolve(relative));
        if (!info.Exists || IsLink(info))
        {
            return new WorkFile.Missing();
        }

        if (info.Length > maxBytes)
        {
            return new WorkFile.TooLarge(info.Length);
        }

        // At most maxBytes + 1 bytes are taken, so a file that grew since the length was read cannot be read whole.
        using var stream = info.OpenRead();
        var buffer = new byte[maxBytes + 1];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        if (read > maxBytes)
        {
            return new WorkFile.TooLarge(read);
        }

        try
        {
            return new WorkFile.Text(Strict.GetString(buffer, 0, read));
        }
        catch (DecoderFallbackException)
        {
            return new WorkFile.NotUtf8();
        }
    }

    private string Resolve(string relative) => relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
