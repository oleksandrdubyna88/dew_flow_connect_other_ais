using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Collecting;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The checks of a coder job: the toolchain is read from <c>.coai/toolchain.yaml</c> AT THE BASE COMMIT, never from the worktree, and run in the worktree. A coder that edits the file — to skip a test, to
/// build something else — changes nothing for its own job: the change is REPORTED, and it takes effect for the jobs after it, once it has been reviewed and merged (plan gate, epoch 3 E2).
/// </summary>
/// <remarks>
/// <para><b>Fails closed.</b> A base commit that is not in the repository, or a file git could not hand over, is <see cref="CheckState.ToolchainRefused"/> — never "no file", which would read the outage as a
/// repository with nothing to check, and never the worktree's file as a fallback.</para>
/// <para><b>A coder job reads the base from the MAIN repository</b> (<see cref="BoundaryJob.Tree"/>): a linked worktree reads its objects through its <c>.git</c> pointer file, and that file is the one thing in the tree a check or a coder could replace.
/// Reading from the main repository path leaves nothing of the worktree in the way of the one file that decides what runs (epoch 3 E2 s2 t4 d5).</para>
/// <para>The comparison with the worktree reads text, with line endings made equal, so a Windows checkout that turned LF into CRLF is not reported as a change the coder made.</para>
/// </remarks>
public sealed class ChecksAtBase(GitHistory history, CheckRunner runner)
{
    /// <summary>The reads that decide the checks are the hardened ones (<see cref="GitHistory.Hardened"/>): no replace refs, no lazy fetch, no names that move git.</summary>
    private readonly GitHistory git = history.ForChecks();

    /// <summary>Reads a setting by name; the process environment unless a test hands in its own. Only <c>COAI_TOOLCHAIN_DETECT</c> is read.</summary>
    public Func<string, string?> Environment { get; init; } = name => System.Environment.GetEnvironmentVariable(name);

    /// <summary>
    /// The checks of a coder job, the one road for it: the <c>.git</c> pointer of the worktree is compared with what coai wrote down BEFORE anything runs and again AFTER, the base toolchain is read from the MAIN repository
    /// (never through the worktree's pointer), and every step runs inside the OS boundary — this runner must have one, or the job is refused. A pointer that changed after the checks makes the run <see cref="CheckState.Failed"/> whatever the steps said.
    /// </summary>
    public async Task<CheckResult> RunCoderJobAsync(IWorkFolder tree, string baseSha, BoundaryJob job, string logFolder, int jobs, CancellationToken ct = default)
    {
        var before = job.Tree.Verify();
        if (before.Length > 0)
        {
            return new CheckResult(CheckState.BoundaryRefused, [], $"{before} Nothing was run.");
        }

        var result = await RunAsync(tree, baseSha, new CheckRequest(job.Tree.Root, logFolder, jobs) { Job = job }, ct);
        var after = job.Tree.Verify();

        return after.Length == 0 ? result : result with { State = CheckState.Failed, Note = $"{after} The result of the checks is not trusted. {result.Note}" };
    }

    /// <summary>Runs the checks of the job whose worktree is <paramref name="request"/>'s tree, with the toolchain of <paramref name="baseSha"/>. <paramref name="tree"/> reads the worktree's files.</summary>
    public async Task<CheckResult> RunAsync(IWorkFolder tree, string baseSha, CheckRequest request, CancellationToken ct = default)
    {
        var atBase = await ReadAtBaseAsync(request.Job?.Tree.MainCheckout ?? request.Tree, baseSha, ct);
        if (atBase.Trouble.Length > 0)
        {
            return new CheckResult(CheckState.ToolchainRefused, [], atBase.Trouble);
        }

        var change = Compare(atBase.File, tree.Read(ToolchainTables.FilePath, ToolchainTables.MaxBytes));
        var file = ToolchainFile.Read(atBase.File);
        var result = request.Job is { } job && file is not ToolchainRead.Refused
            ? await RunDetectedAsync(file, job, baseSha, request, ct)
            : await runner.RunAsync(file, request, ct);

        return result with { ToolchainChange = change, Note = result.Note + Reported(change, baseSha) };
    }

    /// <summary>
    /// What the checks of a CODER job are (epoch 3 E4 s3 t3; SPEC §A): the file at the base wins; with none, or for a project that names no command, the manifests of the BASE commit and of the HEAD commit — the commit coai made, read from the MAIN repository's objects through the
    /// hardened reads, never from the worktree: an untracked or ignored manifest in the worktree changes nothing — resolve into steps by <see cref="ToolchainResolve"/>. The manifests are read only when the answer needs them: a file that states every command needs neither.
    /// </summary>
    private async Task<CheckResult> RunDetectedAsync(ToolchainRead file, BoundaryJob job, string baseSha, CheckRequest request, CancellationToken ct)
    {
        var read = await ResolveAsync(file, job, baseSha, ct);
        AffectedSelection? selection = null;
        if (read.Resolution is ToolchainResolution.Resolved resolved && read.AtBase is not null && read.AtHead is not null)
        {
            // Level one of the selection (SPEC §E): a project is left out only when the diff is shown not to reach it; impact unknown runs everything.
            var diff = await git.InCoderTreeAsync(job.Tree, ["diff", "--name-status", "-z", "-M", baseSha, read.HeadSha], ct);
            selection = AffectedProjects.Select(resolved.Toolchain, diff.Ran && diff.Ok && !diff.Truncated ? AffectedProjects.ParseNameStatus(diff.Out) : null, read.AtBase, read.AtHead);
        }

        return await runner.RunResolvedAsync(read.Resolution, request, ct, selection);
    }

    private sealed record Read(ToolchainResolution Resolution, ManifestSet? AtBase, ManifestSet? AtHead, string HeadSha);

    private async Task<Read> ResolveAsync(ToolchainRead file, BoundaryJob job, string baseSha, CancellationToken ct)
    {
        var setting = Environment(ToolchainResolve.DetectVariable);
        var detect = ToolchainResolve.DetectOn(setting, []);
        var needs = detect && file is not ToolchainRead.Refused && (file is not ToolchainRead.Configured configured || configured.Toolchain.Projects.Any(project => !project.StatesACommand));
        ManifestRead atBase = new ManifestRead.Refused("the manifests of the base commit were not read");
        ManifestRead atHead = new ManifestRead.Refused("the manifests of the head commit were not read");
        var headSha = string.Empty;
        if (needs)
        {
            var head = await git.InCoderTreeAsync(job.Tree, ["rev-parse", "HEAD"], ct);
            headSha = head.Ran && head.Ok ? head.Out.Trim() : string.Empty;
            if (!GitHistory.IsCommitish(headSha))
            {
                return new Read(new ToolchainResolution.Refused("the head commit of the worktree could not be read, so the checks of a repository with no commands in its file were not decided"), null, null, string.Empty);
            }

            var manifests = new ManifestsAtCommit(git);
            atBase = await manifests.ReadAsync(job.Tree.MainCheckout, baseSha, ToolchainDefaults.Current, ct);
            atHead = await manifests.ReadAsync(job.Tree.MainCheckout, headSha, ToolchainDefaults.Current, ct);
        }

        return new Read(ToolchainResolve.Resolve(file, () => atBase, () => atHead, setting, ToolchainDefaults.Current), atBase is ManifestRead.Found foundBase ? foundBase.Set : null, atHead is ManifestRead.Found foundHead ? foundHead.Set : null, headSha);
    }

    private sealed record BaseFile(WorkFile File, string Trouble);

    private async Task<BaseFile> ReadAtBaseAsync(string repository, string baseSha, CancellationToken ct)
    {
        var commit = await git.HasCommitAsync(repository, baseSha, ct);
        if (!commit.Ran || !commit.Ok)
        {
            return Unreadable($"the base commit '{baseSha}' is not a commit of this repository");
        }

        var has = await git.HasPathAsync(repository, baseSha, ToolchainTables.FilePath, ct);
        if (!has.Ran)
        {
            return Unreadable($"git did not answer whether {baseSha} has {ToolchainTables.FilePath}");
        }

        if (!has.Ok)
        {
            return new BaseFile(new WorkFile.Missing(), string.Empty);
        }

        var shown = await git.FileAtAsync(repository, baseSha, ToolchainTables.FilePath, ct);

        return !shown.Ran || !shown.Ok
            ? Unreadable($"git could not read {ToolchainTables.FilePath} at {baseSha}")
            : new BaseFile(shown.Truncated ? new WorkFile.TooLarge(shown.Out.Length) : new WorkFile.Text(shown.Out), string.Empty);
    }

    private static BaseFile Unreadable(string trouble) => new(new WorkFile.Missing(), $"the toolchain at the base was not read, so nothing was run: {trouble}");

    /// <summary>What the worktree did to the file, comparing the text with line endings made equal.</summary>
    private static ToolchainChange Compare(WorkFile atBase, WorkFile inTree) => (atBase, inTree) switch
    {
        (WorkFile.Missing, WorkFile.Missing) => ToolchainChange.Unchanged,
        (WorkFile.Missing, _) => ToolchainChange.Added,
        (_, WorkFile.Missing) => ToolchainChange.Removed,
        (WorkFile.Text before, WorkFile.Text after) when Same(before.Content, after.Content) => ToolchainChange.Unchanged,
        _ => ToolchainChange.Changed,
    };

    private static bool Same(string before, string after) => before.ReplaceLineEndings("\n") == after.ReplaceLineEndings("\n");

    private static string Reported(ToolchainChange change, string baseSha) => change == ToolchainChange.Unchanged
        ? string.Empty
        : $" The worktree {Verb(change)} {ToolchainTables.FilePath}; this job ran the file at base {baseSha[..Math.Min(8, baseSha.Length)]} and the change did not run.";

    private static string Verb(ToolchainChange change) => change switch
    {
        ToolchainChange.Added => "added",
        ToolchainChange.Removed => "removed",
        _ => "changed",
    };
}
