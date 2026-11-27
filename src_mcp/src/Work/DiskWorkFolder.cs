using System.Text;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Files;

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

        // The tree is one a coder's unit and every check unit can write, so an entry may have been made a FIFO (a plain open waits for ever): it is read by the one road for such entries — no wait, a regular file only, at most maxBytes, a time bound (pass 9, M20).
        switch (UnitWrittenFile.Read(info.FullName, maxBytes))
        {
            case FileReadResult.Content content:
                try
                {
                    return new WorkFile.Text(Strict.GetString(content.Bytes));
                }
                catch (DecoderFallbackException)
                {
                    return new WorkFile.NotUtf8();
                }

            case FileReadResult.Missing:
                return new WorkFile.Missing();
            case FileReadResult.Unreadable unreadable when unreadable.Reason.StartsWith("larger than", StringComparison.Ordinal):
                return new WorkFile.TooLarge((long)maxBytes + 1);
            case FileReadResult.Unreadable unreadable when unreadable.Reason.Contains("not a regular file", StringComparison.Ordinal) || unreadable.Reason.Contains("symbolic link", StringComparison.Ordinal) || unreadable.Reason.StartsWith("it did not finish", StringComparison.Ordinal):
                return new WorkFile.NotRegular(unreadable.Reason);
            case FileReadResult.Unreadable unreadable:
                // A read the disk refuses (a permission, a fault) is still an exception, as before: the mode turns it into exit 74.
                throw unreadable.Reason.StartsWith("no read permission", StringComparison.Ordinal) || unreadable.Reason.StartsWith(nameof(UnauthorizedAccessException), StringComparison.Ordinal)
                    ? new UnauthorizedAccessException(unreadable.Reason)
                    : new IOException(unreadable.Reason);
            default:
                return new WorkFile.NotRegular("it could not be read");
        }
    }

    private string Resolve(string relative) => relative.Length == 0 ? root : Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
