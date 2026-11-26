using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// One submodule of the base commit, as a coder job sees it (epoch 3 E2 s6, the recorded change): where it is declared, the commit the base pins, and the folder of coai's own that holds that commit's files — empty when it could not be provided, with the reason.
/// </summary>
/// <param name="Name">The name <c>.gitmodules</c> gives the submodule (not always its path).</param>
/// <param name="Path">The path in the worktree, relative, with <c>/</c>.</param>
/// <param name="Pin">The commit the base commit's tree pins at <paramref name="Path"/> (its gitlink).</param>
/// <param name="Source">The folder holding that commit's files, in coai's mounts folder; empty when not provided.</param>
/// <param name="Note">Why it was not provided, or empty.</param>
public sealed record CoderMount(string Name, string Path, string Pin, string Source, string Note)
{
    /// <summary>Whether the files of the pinned commit are there to be bound.</summary>
    public bool Provided => Source.Length > 0;

    /// <summary>Whether the path has the shape a mount may have (relative, no <c>..</c>, no character a bind cannot spell).</summary>
    public bool Valid => CoderSubmodules.IsMountPath(Path);
}

/// <summary>The submodules a coder job declares at its base, and what coai made of each (<see cref="CoderSubmodules"/>).</summary>
public sealed record CoderMounts(IReadOnlyList<CoderMount> Declared)
{
    /// <summary>The file that declares submodules. A coder never changes it: it is the base's.</summary>
    public const string GitmodulesFile = ".gitmodules";

    /// <summary>No submodule.</summary>
    public static CoderMounts None { get; } = new([]);

    /// <summary>The names of the worktree a coder's diff may not touch: <c>.gitmodules</c> and every mount's path (and anything below it).</summary>
    public IReadOnlyList<string> Protected => [GitmodulesFile, .. Declared.Where(mount => mount.Valid).Select(mount => mount.Path)];

    /// <summary>The read-only binds of the provided mounts: each source onto its path in <paramref name="worktree"/>.</summary>
    public IReadOnlyList<ReadOnlyMount> Binds(string worktree) =>
        [.. Declared.Where(mount => mount.Provided).Select(mount => new ReadOnlyMount(mount.Source, System.IO.Path.Combine(worktree, mount.Path)))];
}
