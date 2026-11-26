using System.Text.RegularExpressions;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Worktrees;

/// <summary>
/// The worktree a coder works in: <c>git worktree add -b coder/&lt;jobId&gt; &lt;root&gt;/coai-coder-&lt;jobId&gt; &lt;baseSha&gt;</c>, removed by coai when the job is over,
/// at most <see cref="Cap"/> live ones per repository (epoch 3 E2 s2 t2; <c>v2/architecture/server.md</c> §§ 9, 16 item 8, 19).
/// </summary>
/// <remarks>
/// <para><b>The root</b> is <see cref="Default"/>: <c>&lt;LocalApplicationData&gt;/coai-mcp/coder</c> — the product's machine-local folder (the same one the review and round
/// trees use, <see cref="ReviewTreeRoot.MachineLocal"/>), under one short leaf. Why there and not elsewhere: (1) <b>outside the main checkout</b> — a start refuses a root that lies inside it,
/// and is the only thing that decides that, not the default; (2) <b>a real disk</b> — <c>/tmp</c> can be a tmpfs, and the s1 recording (<c>RESULTS_s1_toolchain.md</c>) measured a build
/// there fall under the product's own 5 GiB free-space floor, 41 tests red; (3) <b>machine-local, never the data dir</b> — a linked worktree's <c>.git</c> file holds an absolute path
/// into one machine's repository and the data dir may be a network share; (4) <b>a short path</b> — the same recording measured two tests red under a 33-character temp root, so the leaf is
/// <c>coder</c>, the folder is <c>coai-coder-</c> plus a 24-character job id, and an operator whose home is long moves the whole root with <see cref="RootVariable"/>
/// (an absolute path, like <c>COAI_REVIEW_ROOT</c>; the same rule, <see cref="ReviewTreeRoot.DefaultIn(Func{string, string?}, Action{string}, string, string, string)"/>).</para>
/// <para><b>Names.</b> The job id is the only free part of the branch and of the folder. It is one strict shape (<see cref="IsJobId"/>: lower-case letters, digits and <c>-</c>, up to 40,
/// starting with a letter or digit) — wider than a role job id (<c>r&lt;14 digits&gt;-&lt;8 hex&gt;</c>, which this project cannot see) and narrower than anything that could be a path
/// or a ref with a meaning of its own — so no separator, dot, colon, <c>@</c>, <c>~</c> or leading dash can reach git or the file system. It is checked before any process starts.</para>
/// <para><b>The cap refuses and never evicts</b>: a live tree is a coder's work. "Live" is a worktree git lists for this repository whose folder name has the prefix, under this root or an earlier one (a root that was moved leaves trees that still hold
/// their disk), and whose folder is still there (git marks a deleted one <c>prunable</c>). Count and creation are one step under a lock file in the root (<see cref="HeldFile"/>), so two starts that both read nineteen
/// cannot leave twenty-one.</para>
/// <para><b>The main checkout is never written</b>: the commands are <c>rev-parse</c>, <c>cat-file</c>, <c>show-ref</c>, <c>worktree list/add/remove/prune</c> and <c>branch -D</c>, none of which
/// reads or writes its HEAD, index or files; the suite proves it with bytes. Submodules are not populated here.</para>
/// <para>Every process is git, through the one <see cref="IProcessLauncher"/> (argv, a timeout, the tree killed with it).</para>
/// </remarks>
public sealed partial class CoderWorktree(IProcessLauncher launcher, string root)
{
    /// <summary>The first part of every coder folder; the job id follows.</summary>
    public const string FolderPrefix = "coai-coder-";

    /// <summary>The first part of every coder branch; the job id follows.</summary>
    public const string BranchPrefix = "coder/";

    /// <summary>The most live coder worktrees of one repository. The next one is refused.</summary>
    public const int Cap = 20;

    /// <summary>The variable that moves the root; an absolute path, else ignored and said.</summary>
    public const string RootVariable = "COAI_CODER_ROOT";

    private const string LockName = ".coder-start.lock";

    private static readonly TimeSpan ForTheLock = TimeSpan.FromSeconds(30);

    private readonly ReviewTreeRoot _at = new(launcher, root);

    private readonly GitHistory _git = new(launcher);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{0,39}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JobIdShape { get; }

    /// <summary>The machine-local root, on a real disk, under one short leaf. See the class remarks for why.</summary>
    public static string Default => ReviewTreeRoot.MachineLocal("coder");

    /// <summary>The root in use: <see cref="RootVariable"/> when it is an absolute path, else <see cref="Default"/>.</summary>
    public static string DefaultIn(Func<string, string?> env, Action<string> said) =>
        ReviewTreeRoot.DefaultIn(env, said, RootVariable, Default, "coder worktrees");

    /// <summary>True only for the one shape; nothing is trimmed or folded.</summary>
    public static bool IsJobId(string? id) => id is not null && JobIdShape.IsMatch(id);

    /// <summary>The folder name of a job: the prefix and the id, nothing else.</summary>
    public static string FolderOf(string jobId) => FolderPrefix + jobId;

    /// <summary>The branch name of a job: the prefix and the id, nothing else.</summary>
    public static string BranchOf(string jobId) => BranchPrefix + jobId;

    /// <summary>
    /// Makes the worktree of <paramref name="jobId"/> at <paramref name="baseSha"/>, or refuses with a sentence and changes nothing — not the repository's HEAD, index or files,
    /// and not a branch or folder that was already there.
    /// </summary>
    public async Task<CoderWorktreeOutcome> StartAsync(string repoPath, string jobId, string baseSha, CancellationToken ct = default)
    {
        var lexical = Lexical(jobId, baseSha);
        if (lexical.Count > 0)
        {
            return lexical[0];
        }

        var place = await RepositoryAsync(repoPath, ct);

        return place.Count > 0 ? place[0] : await UnderTheLockAsync(repoPath, jobId, baseSha, ct);
    }

    /// <summary>
    /// Removes the worktree of <paramref name="jobId"/> with whatever the coder left in it (<c>worktree remove --force</c>), then the branch. Nothing there is also success.
    /// A folder this repository does not list as a worktree is not ours to delete, and is refused.
    /// </summary>
    public async Task<CoderWorktreeOutcome> RemoveAsync(string repoPath, string jobId, CancellationToken ct = default)
    {
        if (!IsJobId(jobId))
        {
            return Refuse(CoderWorktreeRefusal.BadJobId, BadJobIdSentence);
        }

        var listing = await ListAsync(repoPath, ct);
        if (listing.Refusals.Count > 0)
        {
            return listing.Refusals[0];
        }

        var path = _at.TreeAt(FolderOf(jobId));
        var listed = listing.Worktrees.Where(tree => WorktreePaths.Same(tree.Path, path)).Take(1).ToList();

        return listed.Count > 0 || !IsThere(path)
            ? await EraseAsync(repoPath, jobId, path, registered: listed.Count > 0, ct)
            : Refuse(CoderWorktreeRefusal.NotOurs, $"The folder {path} is not a worktree of this repository, so it was not removed.");
    }

    private const string BadJobIdSentence =
        "The job id is not usable: it must be 1 to 40 characters of a-z, 0-9 and '-', starting with a letter or a digit.";

    private static IReadOnlyList<CoderWorktreeOutcome.Refused> Lexical(string jobId, string baseSha) =>
        !IsJobId(jobId)
            ? [Refuse(CoderWorktreeRefusal.BadJobId, BadJobIdSentence)]
            : !GitHistory.IsCommitish(baseSha)
                ? [Refuse(CoderWorktreeRefusal.BadBaseSha, "The base must be a full 40-character commit id; a branch name or a short id is not accepted.")]
                : [];

    /// <summary>The repository is a repository, and the root is a place a coder cannot write into the main checkout from.</summary>
    private async Task<IReadOnlyList<CoderWorktreeOutcome.Refused>> RepositoryAsync(string repoPath, CancellationToken ct)
    {
        var asked = Directory.Exists(repoPath) ? await _git.RootAndHeadAsync(repoPath, ct) : new GitAnswer(true, false, string.Empty);

        return !asked.Ran
            ? [Refuse(CoderWorktreeRefusal.GitFailed, "git did not answer in time while the repository was looked at.")]
            : asked.Ok && asked.Lines.Count > 0
                ? OutsideTheCheckout(asked.Lines[0])
                : [Refuse(CoderWorktreeRefusal.NotARepository, $"{repoPath} is not a git repository with a commit.")];
    }

    private IReadOnlyList<CoderWorktreeOutcome.Refused> OutsideTheCheckout(string topLevel)
    {
        var rootReal = RealPath.Resolve(root);
        var checkoutReal = RealPath.Resolve(topLevel);

        return !Path.IsPathFullyQualified(root) || !rootReal.Ok || !checkoutReal.Ok
            ? [Refuse(CoderWorktreeRefusal.RootUnusable, $"The coder root '{root}' is not an absolute path that can be resolved.")]
            : Within(checkoutReal.Path, rootReal.Path)
                ? [Refuse(CoderWorktreeRefusal.RootInsideCheckout, $"The coder root {root} is inside the main checkout {topLevel}; set {RootVariable} to a folder outside it.")]
                : [];
    }

    /// <summary>Whether <paramref name="child"/> is <paramref name="parent"/> or below it; both are real paths.</summary>
    private static bool Within(string parent, string child)
    {
        var relative = Path.GetRelativePath(parent, child);

        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private async Task<CoderWorktreeOutcome> UnderTheLockAsync(string repoPath, string jobId, string baseSha, CancellationToken ct)
    {
        var hold = await Task.Run(TakeTheLock, ct);
        if (hold is not Hold.Taken taken)
        {
            return Refuse(CoderWorktreeRefusal.RootUnusable, Unheld(hold));
        }

        using (taken.Stream)
        {
            return await GuardedAddAsync(repoPath, jobId, baseSha, ct);
        }
    }

    private Hold TakeTheLock()
    {
        try
        {
            Directory.CreateDirectory(root);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Hold.Unusable(e);
        }

        return HeldFile.TakeWithin(Path.Combine(root, LockName), ForTheLock);
    }

    private string Unheld(Hold hold) => hold is Hold.Unusable unusable
        ? $"The coder root {root} cannot be used: {unusable.Cause.Message}"
        : $"Another coder worktree is being made in {root} and did not finish within {ForTheLock.TotalSeconds:0} seconds; try again.";

    private async Task<CoderWorktreeOutcome> GuardedAddAsync(string repoPath, string jobId, string baseSha, CancellationToken ct)
    {
        Func<Task<IReadOnlyList<CoderWorktreeOutcome.Refused>>>[] guards =
        [
            () => BaseExistsAsync(repoPath, baseSha, ct),
            () => NamesAreFreeAsync(repoPath, jobId, ct),
            () => RoomAsync(repoPath, ct),
        ];
        foreach (var guard in guards)
        {
            var refused = await guard();
            if (refused.Count > 0)
            {
                return refused[0];
            }
        }

        return await AddAsync(repoPath, jobId, baseSha, ct);
    }

    private async Task<IReadOnlyList<CoderWorktreeOutcome.Refused>> BaseExistsAsync(string repoPath, string baseSha, CancellationToken ct)
    {
        var asked = await _git.HasCommitAsync(repoPath, baseSha, ct);

        return !asked.Ran
            ? [Refuse(CoderWorktreeRefusal.GitFailed, "git did not answer in time while the base commit was looked for.")]
            : asked.Ok
                ? []
                : [Refuse(CoderWorktreeRefusal.UnknownBase, $"The base commit {baseSha} does not exist in this repository, so no coder worktree was made.")];
    }

    /// <summary>Neither the folder nor the branch of this job is there — a refused start must never be able to delete or move what it did not make.</summary>
    private async Task<IReadOnlyList<CoderWorktreeOutcome.Refused>> NamesAreFreeAsync(string repoPath, string jobId, CancellationToken ct)
    {
        var folder = _at.TreeAt(FolderOf(jobId));
        if (IsThere(folder))
        {
            return [Refuse(CoderWorktreeRefusal.AlreadyExists, $"The folder {folder} is already there; it was not touched.")];
        }

        var branch = await BranchAsync(repoPath, jobId, ct);

        return !branch.Ran
            ? [Refuse(CoderWorktreeRefusal.GitFailed, "git did not answer in time while the branch name was looked for.")]
            : branch.Ok
                ? [Refuse(CoderWorktreeRefusal.AlreadyExists, $"The branch {BranchOf(jobId)} is already there; it was not touched.")]
                : [];
    }

    private async Task<IReadOnlyList<CoderWorktreeOutcome.Refused>> RoomAsync(string repoPath, CancellationToken ct)
    {
        var listing = await ListAsync(repoPath, ct);
        if (listing.Refusals.Count > 0)
        {
            return listing.Refusals;
        }

        var live = listing.Worktrees.Count(IsLiveCoderTree);

        return live >= Cap
            ? [Refuse(CoderWorktreeRefusal.Full, $"This repository already has {live} live coder worktrees, the most allowed ({Cap}); remove one before starting another. None was removed to make room.")]
            : [];
    }

    /// <summary>One of ours, by what it is called — under THIS root or an earlier one: a tree left under a root that was moved still holds its disk — whose folder is still there.</summary>
    private static bool IsLiveCoderTree(ListedWorktree tree) =>
        !tree.Prunable && Path.GetFileName(tree.Path).StartsWith(FolderPrefix, StringComparison.Ordinal);

    /// <summary>Whether ANYTHING is at the path — a folder, a file or a link, even a dangling one — looked at without following it. A path that cannot be looked at counts as taken.</summary>
    private static bool IsThere(string path)
    {
        try
        {
            return RealPath.Lstat(path).Kind != EntryKind.Missing;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private async Task<CoderWorktreeOutcome> AddAsync(string repoPath, string jobId, string baseSha, CancellationToken ct)
    {
        var path = _at.TreeAt(FolderOf(jobId));
        var branch = BranchOf(jobId);
        var added = await GitAsync(repoPath, ["worktree", "add", "-b", branch, path, baseSha], ReviewTreeRoot.Checking, ct);
        if (!added.TimedOut && !added.Cancelled && added.ExitCode == 0)
        {
            return new CoderWorktreeOutcome.Started(_at.Real(path) is { Ok: true } real ? real.Path : path, branch, baseSha);
        }

        // Both names were proved free under the lock a moment ago, so whatever exists now is what this call made: removing it takes nothing of anybody's.
        await EraseAsync(repoPath, jobId, path, registered: true, CancellationToken.None);

        return Refuse(CoderWorktreeRefusal.GitFailed, added.TimedOut || added.Cancelled
            ? "git worktree add did not finish in time; what it had made was removed."
            : $"git worktree add: {WorktreeFailure.Sentence(added.StdErr, path)}");
    }

    /// <summary>The folder and the branch of one job taken away: the tree by git, the folder if git left it, the registration if it is stale, then the branch.</summary>
    private async Task<CoderWorktreeOutcome> EraseAsync(string repoPath, string jobId, string path, bool registered, CancellationToken ct)
    {
        await GitAsync(repoPath, ["worktree", "remove", "--force", path], ReviewTreeRoot.Erasing, ct);
        var gone = !IsThere(path) || await _at.EnsureGoneAsync(path, ct);
        if (gone && registered)
        {
            await GitAsync(repoPath, ["worktree", "prune"], ReviewTreeRoot.Asking, ct);
        }

        var dropped = gone
            ? await DropBranchAsync(repoPath, jobId, ct)
            : [Refuse(CoderWorktreeRefusal.GitFailed, $"The folder {path} could not be removed; it is still there.")];

        return dropped.Count > 0 ? dropped[0] : new CoderWorktreeOutcome.Removed(path, BranchOf(jobId));
    }

    private async Task<IReadOnlyList<CoderWorktreeOutcome.Refused>> DropBranchAsync(string repoPath, string jobId, CancellationToken ct)
    {
        var branch = await BranchAsync(repoPath, jobId, ct);
        if (!branch.Ran)
        {
            return [Refuse(CoderWorktreeRefusal.GitFailed, "git did not answer in time while the branch was looked for.")];
        }

        return branch.Ok ? Broke("branch -D", await GitAsync(repoPath, ["branch", "-D", BranchOf(jobId)], ReviewTreeRoot.Asking, ct)) : [];
    }

    /// <summary>Whether <c>refs/heads/coder/&lt;jobId&gt;</c> is there: exit 0 yes, 1 no, anything else (or no answer) is "we do not know".</summary>
    private async Task<GitAnswer> BranchAsync(string repoPath, string jobId, CancellationToken ct)
    {
        var asked = await GitAsync(repoPath, ["show-ref", "--verify", "--quiet", $"refs/heads/{BranchOf(jobId)}"], ReviewTreeRoot.Asking, ct);

        return asked.TimedOut || asked.Cancelled || asked.ExitCode > 1 ? GitAnswer.Broken : new GitAnswer(true, asked.ExitCode == 0, asked.StdOut);
    }

    private async Task<Listing> ListAsync(string repoPath, CancellationToken ct)
    {
        if (!Directory.Exists(repoPath))
        {
            return new Listing([Refuse(CoderWorktreeRefusal.NotARepository, $"{repoPath} is not a git repository with a commit.")], []);
        }

        var asked = await GitAsync(repoPath, ["worktree", "list", "--porcelain", "-z"], ReviewTreeRoot.Asking, ct);
        var broke = Broke("worktree list", asked);

        return new Listing(broke, broke.Count > 0 ? [] : CoderWorktreeList.Parse(asked.StdOut));
    }

    private static IReadOnlyList<CoderWorktreeOutcome.Refused> Broke(string doing, ProcessResult result) =>
        result.TimedOut || result.Cancelled
            ? [Refuse(CoderWorktreeRefusal.GitFailed, $"git {doing} did not finish in time.")]
            : result.ExitCode == 0
                ? []
                : [Refuse(CoderWorktreeRefusal.GitFailed, $"git {doing} failed: {WorktreeManager.FatalLine(result.StdErr)}")];

    private static CoderWorktreeOutcome.Refused Refuse(CoderWorktreeRefusal reason, string sentence) => new(reason, sentence);

    private Task<ProcessResult> GitAsync(string repoPath, string[] args, TimeSpan budget, CancellationToken ct) =>
        launcher.RunAsync(new ProcessRequest("git", TreeGit.Argv(args), repoPath) { Timeout = budget }, ct);

    private sealed record Listing(IReadOnlyList<CoderWorktreeOutcome.Refused> Refusals, IReadOnlyList<ListedWorktree> Worktrees);
}
