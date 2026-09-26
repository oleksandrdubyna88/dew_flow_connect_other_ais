using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// The place review trees live, and the few rules everything that touches them shares.
/// </summary>
/// <remarks>
/// <para>Extracted when story 3.2b arrived to LIST and REMOVE what 3.2a creates: the root, the
/// prefix, the identity, the "is this really ours" check and the process budgets are the same facts
/// for both, and a second copy of them is a second place for the prefix to be wrong — which is the
/// one thing that must never be wrong, because the prefix is what keeps the gate's own pruning away
/// from a person's checkout.</para>
/// <para>It owns no state beyond the root path and holds no lifetime: creating a tree and giving one
/// back are different stories with different invariants, and they stay different classes.</para>
/// </remarks>
public sealed class ReviewTreeRoot(IProcessLauncher launcher, string path)
{
    /// <summary>
    /// Distinct from the round prefix, which is what <see cref="WorktreeManager.PruneOursAsync"/>
    /// matches on — with its own root, the second of two independent guards.
    /// </summary>
    public const string Prefix = "coai-review-";

    /// <summary>A probe or an inspection: seconds of work, and a minute is already generous.</summary>
    public static TimeSpan Asking => TimeSpan.FromMinutes(1);

    /// <summary>A checkout and its submodules, matching the extension's own cap on the call.</summary>
    public static TimeSpan Checking => TimeSpan.FromMinutes(10);

    /// <summary>Long enough for a real tree, short enough that a held handle is not forever.</summary>
    public static TimeSpan Erasing => TimeSpan.FromMinutes(2);

    /// <summary>Where the trees are.</summary>
    public string Path => path;

    /// <summary>
    /// Machine-local, and deliberately NOT under the configured data dir: a linked worktree's
    /// <c>.git</c> file holds an absolute path into the parent repository's admin directory, so a
    /// tree is bound to one machine and one OS — and this product's data dir is routinely a network
    /// share, where such a tree would be broken for every other machine that mounted it.
    /// </summary>
    public static string Default => MachineLocal("review-worktrees");

    /// <summary>The variable that points the review trees somewhere else — issue #544.</summary>
    public const string RootVariable = "COAI_REVIEW_ROOT";

    /// <summary>
    /// The review trees' root: <see cref="RootVariable"/> when it is an absolute path, else <see cref="Default"/>.
    /// </summary>
    /// <remarks>
    /// Issue #544. The default is machine-local on purpose (above), and on Windows it comes from the Known
    /// Folder API, which no environment variable redirects — so a test running the real binary over a temp
    /// data directory still read the machine's OWN trees, and failed on any machine that held one. A
    /// RELATIVE value is ignored rather than resolved: it would mean whatever directory the process
    /// happened to start in, and the root must be where every later call finds the same trees. An ignored
    /// value is <paramref name="said"/>, so nobody believes a root they set is in use.
    /// </remarks>
    public static string DefaultIn(Func<string, string?> env, Action<string> said)
    {
        var asked = env(RootVariable);
        if (asked is null)
        {
            return Default;
        }
        var trimmed = asked.Trim();
        if (trimmed.Length > 0 && System.IO.Path.IsPathFullyQualified(trimmed))
        {
            return trimmed;
        }
        said($"{RootVariable} is set but is not an absolute path, so it was ignored; the review trees are in {Default}.");

        return Default;
    }

    /// <summary>
    /// A machine-local directory of this product's, by leaf — the ONE resolution both kinds of tree
    /// use, so the review trees and the round trees can never disagree about where "local" is.
    /// </summary>
    public static string MachineLocal(string leaf) => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "coai-mcp",
        leaf);

    /// <summary>
    /// A stable directory name for one (repository, commit): a short digest of the common dir so the
    /// name is legal on every filesystem, and the short sha so a human reading a path can tell which
    /// commit it holds. The FULL identity lives in the record and is what a reuse is checked against.
    /// </summary>
    public static string NameOf(string repository, string sha) =>
        $"{Prefix}{Digest(Normalised(repository))}-{sha[..12].ToLowerInvariant()}";

    /// <summary>
    /// The identity KEY, not a path to ask the filesystem about: separators one way, no trailing
    /// slash, case folded. Story 2.2 measured the cost of folding case before a filesystem call and
    /// this is the other side of that line — nothing here is ever handed to <c>Directory.Exists</c>.
    /// </summary>
    public static string Normalised(string repository) => Context.RepositoryIdentity.Normalised(repository);

    private static string Digest(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

    /// <summary>
    /// Whether a NAME is one this product could have made: our prefix, and nothing that could make it
    /// leave the root.
    /// </summary>
    /// <remarks>
    /// Checked before any process, and it is what makes "the argument is a name, never a path" true
    /// rather than intended. A round tree's name fails the prefix; a path fails the separator; and
    /// anything at all still has to match a record we actually hold, which the caller checks after.
    /// </remarks>
    public static bool IsOurName(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal)
        && name.Length > Prefix.Length
        && !name.Contains("..", StringComparison.Ordinal)
        && Plain(name);

    /// <summary>
    /// Nothing in it can make it mean a place: no separator, no NUL, nothing a filesystem refuses.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="IsOurName"/> because the two together were a seven-term condition,
    /// over the cap of four — and a guard nobody can read is a guard nobody can check. The separators
    /// are named explicitly rather than left to <c>GetInvalidFileNameChars</c>, which does not include
    /// <c>/</c> on Linux.
    /// </remarks>
    private static bool Plain(string name) =>
        !name.Contains('/', StringComparison.Ordinal)
        && !name.Contains('\\', StringComparison.Ordinal)
        && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Whether a path really is under this root — asked before anything is deleted.</summary>
    public bool Holds(string candidate)
    {
        var below = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;

        return System.IO.Path.GetFullPath(candidate).StartsWith(below, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Where the tree of this name is.</summary>
    public string TreeAt(string name) => System.IO.Path.Combine(path, name);

    /// <summary>
    /// One git command, with a budget, answered as data.
    /// </summary>
    /// <remarks>
    /// <para><b>The working directory must exist.</b> Starting a process in a directory that has been
    /// deleted is tolerated on Windows and throws <c>Win32Exception</c> on Linux — measured on CI,
    /// where it failed the teardown of a whole test class while every test body passed. Every caller
    /// here is asking about something that may have been removed by hand, so the check belongs in the
    /// one place they all go through rather than in each of them.</para>
    /// <para>A timeout or a cancellation is not an answer: the caller must record "we learned
    /// nothing" rather than a fact about the tree.</para>
    /// </remarks>
    public async Task<GitAnswer> GitAsync(string workingDirectory, string[] args, TimeSpan budget, CancellationToken ct)
    {
        if (!Directory.Exists(workingDirectory))
        {
            // `Broken` is "nothing ran", which is NOT "git said no": every caller here must record
            // that it learned nothing rather than a fact about the tree, because a missing working
            // directory tells you about the directory and not about what was in it.
            return GitAnswer.Broken;
        }

        var result = await launcher.RunAsync(
            new ProcessRequest("git", args, workingDirectory) { Timeout = budget }, ct);

        return result.TimedOut || result.Cancelled
            ? GitAnswer.Broken
            : new GitAnswer(true, result.ExitCode == 0, result.StdOut);
    }

    /// <summary>
    /// Deletes a directory under this root, under a budget, off the calling thread.
    /// </summary>
    /// <remarks>
    /// Refused outside the root, because a recursive delete is the one call here that could destroy
    /// something else. Asking whether it is already gone is not a redundant check:
    /// <c>Directory.Delete</c> on an absent directory throws, and the catch would read that as
    /// "could not clear it". The catch answers the QUESTION rather than the exception, for the same
    /// reason — a directory another process removed first is a success.
    /// </remarks>
    public async Task<bool> EnsureGoneAsync(string candidate, CancellationToken ct)
    {
        if (!Holds(candidate))
        {
            return false;
        }

        if (!Directory.Exists(candidate))
        {
            return true;
        }

        try
        {
            await Task.Run(() => Directory.Delete(candidate, recursive: true), ct).WaitAsync(Erasing, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TimeoutException)
        {
            // A TIMEOUT abandons the WAIT and not the delete: `Directory.Delete` takes no token, so
            // it keeps going in the background and may yet succeed. Said out loud because a reviewer
            // read the timeout as a stop and was right to: what this returns is only whether the
            // directory is gone AT THIS MOMENT, and the caller must treat false as "we do not know
            // what this tree is now" rather than as "it is still whole". Nothing downstream acts on
            // a false — the record is kept and the answer is `git_failed`.
            return !Directory.Exists(candidate);
        }

        return !Directory.Exists(candidate);
    }
}
