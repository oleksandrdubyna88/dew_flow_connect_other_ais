namespace CoaiMcp.Core.Work;

/// <summary>One regular file of a work-plan folder: its path from the feature folder (written with <c>/</c>), its git blob id and its size in bytes.</summary>
public sealed record WorkEntry(string Path, string Blob, long Size);

/// <summary>
/// A feature folder that also says WHICH files it holds and what each one is called in git: what the work pack of a plan round needs beside the texts (epoch 3 E3 s3). The files are the regular ones — a link and a
/// submodule are not files here, as <see cref="IWorkFolder"/> already says — in ordinal path order, so the same folder lists the same bytes.
/// </summary>
public interface IListedWorkFolder : IWorkFolder
{
    /// <summary>Every regular file, by path from the feature folder, in ordinal order.</summary>
    IReadOnlyList<WorkEntry> Files { get; }
}
