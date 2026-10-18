using System.Text.Json;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Worktrees;

namespace CoaiMcp.Store;

/// <summary>
/// Which tool calls are a write, and the ONE property of each that names its file (E3.S3).
/// </summary>
/// <remarks>
/// <para>Only the four tools E1's harness counts (<c>scripts/lib/caller-transcripts.mjs:17</c>). <c>Bash</c> and <c>PowerShell</c> can
/// write a file and stay <c>Unknown</c> — a command line is text this reader never materialises — and so does every other
/// tool, an <c>mcp__…</c> one carrying a <c>file_path</c> included (parent §5, §13).</para>
/// <para>An <c>Artifact</c>-style file writer is a new row in <see cref="Table"/> (tool name, its one path property), counted only
/// when the path resolves to a checkout (parent §16 D18) — and none is added without an E1 fixture showing it on disk. E1's
/// fixtures hold no such write, so the table ships with the four names alone.</para>
/// </remarks>
public static class WriteTools
{
    private static readonly (string Tool, string Property)[] Table =
    [
        ("Edit", "file_path"),
        ("Write", "file_path"),
        ("MultiEdit", "file_path"),
        ("NotebookEdit", "notebook_path"),
    ];

    /// <summary>The names, ordinal and case-sensitive.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Table.Select(row => row.Tool)];

    /// <summary>
    /// The file a write tool's input names. Reads ONE property, only when it is a string; never enumerates the input and never
    /// touches <c>content</c>, <c>old_string</c>, <c>new_string</c> or <c>edits</c>. Any other tool, a missing property, a non-string
    /// or an empty one is false and leaves <paramref name="path"/> empty.
    /// </summary>
    public static bool TryPath(string toolName, JsonElement input, out string path)
    {
        var property = PropertyOf(toolName);
        path = property.Length > 0 ? JsonText.StringProperty(input, property) : string.Empty;

        return path.Length > 0;
    }

    private static string PropertyOf(string toolName) =>
        Table.FirstOrDefault(row => string.Equals(row.Tool, toolName, StringComparison.Ordinal)).Property ?? string.Empty;
}

/// <summary>Where a write landed. <see cref="Checkout"/> is the only kind that names a repository.</summary>
public enum WriteTargetKind
{
    /// <summary>Inside a checkout; <see cref="WriteTarget.Where"/> is its root as a KEY.</summary>
    Checkout,

    /// <summary>No checkout above the file — a scratchpad, a temp file, a memory file. Not "another repository".</summary>
    OutsideAnyCheckout,

    /// <summary>Could not be placed: a relative path, or a directory that no longer exists.</summary>
    Unresolved,
}

/// <summary>
/// <see cref="Where"/> is <c>RepositoryIdentity.Normalised</c> of the checkout root (<see cref="WriteTargetKind.Checkout"/>) or of the
/// vanished directory (<see cref="WriteTargetKind.Unresolved"/> for an absolute path), else empty. It is a KEY, never handed back
/// to the filesystem.
/// </summary>
public sealed record WriteTarget(WriteTargetKind Kind, string Where);

/// <summary>
/// Which checkout a write landed in, from the path alone. One instance serves ONE read.
/// </summary>
/// <remarks>
/// <para>The answer is per DIRECTORY and cached by the exact full directory (<see cref="CacheKey"/>: case-folded only where the
/// file system folds it), so <c>D:\X</c> and <c>d:/x</c> share an entry on Windows and <c>/work/App</c> and <c>/work/app</c> do not
/// on Linux, and a session that writes thousands of files asks the filesystem once per folder. Only <c>Directory.Exists</c> and
/// <see cref="GitMarker.HasGit"/> are used, through <see cref="CheckoutProbes"/>: no git process, no file read.</para>
/// <para><b>Never asks about a remote path</b> (security fix, 2026-10-18): a UNC or device path, or one on a mapped network drive,
/// is <c>Unresolved</c> with an empty key and never reaches the file system — see <see cref="LocalPaths"/>.</para>
/// <para><b>Nearest root wins.</b> A submodule or a nested checkout resolves to the INNER root, and a linked worktree to the
/// worktree, not to the common dir — two worktrees are two checkouts. Whether an inner root under the gate's <c>repo_path</c> counts as
/// "here" is E4's rule, not this class's.</para>
/// <para><b>Never walk up from a missing directory</b>: a removed worktree would walk up into whatever contains it and answer
/// wrongly. <b>Never resolve against this process's cwd</b>, which is not the agent's: a relative path is unresolved.</para>
/// </remarks>
public sealed class CheckoutRoots
{
    /// <summary>Windows and macOS file systems fold case; a Linux one (WSL, CI) does not, and <c>/work/App</c> is not <c>/work/app</c> there.</summary>
    private static readonly bool CaseSensitiveFileSystem = !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    private readonly Dictionary<string, WriteTarget> _byDirectory = [];
    private readonly CheckoutProbes _probes;

    public CheckoutRoots()
        : this(CheckoutProbes.Real)
    {
    }

    /// <summary>The seam a test uses to see every filesystem touch: <see cref="CheckoutProbes"/> is the ONLY way this class asks the disk.</summary>
    internal CheckoutRoots(CheckoutProbes probes) => _probes = probes;

    /// <summary>
    /// The cache key of a FULL directory: the directory itself, folded to one case only where the file system folds it. Not
    /// <c>RepositoryIdentity.Normalised</c>, which always lower-cases — on a case-sensitive file system two directories that
    /// differ by case shared one entry and the first answer won. <c>Normalised</c> still makes <see cref="WriteTarget.Where"/>.
    /// </summary>
    internal static string CacheKey(string fullDirectory, bool caseSensitive) =>
        caseSensitive ? fullDirectory : fullDirectory.ToLowerInvariant();

    public WriteTarget Resolve(string path)
    {
        // A UNC, device or network-drive path is refused on its TEXT, before any file-system call: asking Windows about
        // \\host\share contacts that host and offers the user's credentials, and a transcript path can be steered (LocalPaths).
        var directory = IsAskable(path) ? DirectoryOf(path) : string.Empty;
        if (directory.Length == 0)
        {
            return new WriteTarget(WriteTargetKind.Unresolved, string.Empty);
        }

        var key = CacheKey(directory, CaseSensitiveFileSystem);
        if (!_byDirectory.TryGetValue(key, out var target))
        {
            target = ResolveDirectory(directory);
            _byDirectory[key] = target;
        }

        return target;
    }

    /// <summary>
    /// The full directory of a fully-qualified path, with <c>..</c> and <c>.</c> resolved BEFORE anything walks up: walking up the
    /// text of <c>repo/../other/a.cs</c> climbs through <c>repo</c> and answers for a file that is in <c>other</c>. A fully-qualified
    /// path never reads the cwd. Empty for a relative path, a root, or a path the platform refuses.
    /// </summary>
    private static string DirectoryOf(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) ? Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty : string.Empty;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return string.Empty;
        }
    }

    /// <summary>A local, rooted path on a drive that is not a network drive — the only kind this class hands to the file system.</summary>
    private bool IsAskable(string path) =>
        LocalPaths.IsLocalRooted(path, OperatingSystem.IsWindows()) && !_probes.IsNetworkDrive(path);

    private WriteTarget ResolveDirectory(string directory) =>
        _probes.DirectoryExists(directory)
            ? NearestRoot(directory)
            : new WriteTarget(WriteTargetKind.Unresolved, RepositoryIdentity.Normalised(directory));

    private WriteTarget NearestRoot(string directory)
    {
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
        {
            if (_probes.HasGit(current))
            {
                return new WriteTarget(WriteTargetKind.Checkout, RepositoryIdentity.Normalised(current));
            }
        }

        return new WriteTarget(WriteTargetKind.OutsideAnyCheckout, string.Empty);
    }
}

/// <summary>
/// Every question <see cref="CheckoutRoots"/> asks the file system — the only door it has to the disk, so a test can see each
/// touch. <see cref="IsNetworkDrive"/> is answered from the local drive table only.
/// </summary>
internal sealed record CheckoutProbes(Func<string, bool> DirectoryExists, Func<string, bool> HasGit, Func<string, bool> IsNetworkDrive)
{
    public static CheckoutProbes Real { get; } = new(Directory.Exists, GitMarker.HasGit, LocalPaths.IsOnNetworkDrive);
}
