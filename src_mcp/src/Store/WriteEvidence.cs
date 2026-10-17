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
        path = StringProperty(input, PropertyOf(toolName));

        return path.Length > 0;
    }

    private static string PropertyOf(string toolName) =>
        Table.FirstOrDefault(row => string.Equals(row.Tool, toolName, StringComparison.Ordinal)).Property ?? string.Empty;

    private static string StringProperty(JsonElement input, string name) =>
        name.Length > 0 && input.ValueKind == JsonValueKind.Object && input.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
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
/// <para>The answer is per DIRECTORY and cached by <c>RepositoryIdentity.Normalised</c>, so <c>D:\X</c> and <c>d:/x</c> share an
/// entry and a session that writes thousands of files asks the filesystem once per folder. Only <c>Directory.Exists</c> and
/// <see cref="GitMarker.HasGit"/> are used: no git process, no file read.</para>
/// <para><b>Nearest root wins.</b> A submodule or a nested checkout resolves to the INNER root, and a linked worktree to the
/// worktree, not to the common dir — two worktrees are two checkouts. Whether an inner root under the gate's <c>repo_path</c> counts as
/// "here" is E4's rule, not this class's.</para>
/// <para><b>Never walk up from a missing directory</b>: a removed worktree would walk up into whatever contains it and answer
/// wrongly. <b>Never resolve against this process's cwd</b>, which is not the agent's: a relative path is unresolved.</para>
/// </remarks>
public sealed class CheckoutRoots
{
    private readonly Dictionary<string, WriteTarget> _byDirectory = [];

    public WriteTarget Resolve(string path)
    {
        var directory = Path.IsPathFullyQualified(path) ? Path.GetDirectoryName(path) : null;
        if (string.IsNullOrEmpty(directory))
        {
            return new WriteTarget(WriteTargetKind.Unresolved, string.Empty);
        }

        var key = RepositoryIdentity.Normalised(directory);
        if (!_byDirectory.TryGetValue(key, out var target))
        {
            target = ResolveDirectory(directory, key);
            _byDirectory[key] = target;
        }

        return target;
    }

    private static WriteTarget ResolveDirectory(string directory, string key) =>
        Directory.Exists(directory) ? NearestRoot(directory) : new WriteTarget(WriteTargetKind.Unresolved, key);

    private static WriteTarget NearestRoot(string directory)
    {
        for (var current = directory; current is not null; current = Path.GetDirectoryName(current))
        {
            if (GitMarker.HasGit(current))
            {
                return new WriteTarget(WriteTargetKind.Checkout, RepositoryIdentity.Normalised(current));
            }
        }

        return new WriteTarget(WriteTargetKind.OutsideAnyCheckout, string.Empty);
    }
}
