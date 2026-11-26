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
    public async Task<CheckResult> RunCoderJobAsync(IWorkFolder tree, string baseSha, BoundaryJob job, string logFolder, int jobs, CancellationToken ct = default, Action<StepRecord>? stepFinished = null, RoundFacts? facts = null)
    {
        var before = job.Tree.Verify();
        if (before.Length > 0)
        {
            return new CheckResult(CheckState.BoundaryRefused, [], $"{before} Nothing was run.");
        }

        var result = await RunAsync(tree, baseSha, new CheckRequest(job.Tree.Root, logFolder, jobs) { Job = job, StepFinished = stepFinished, Facts = facts ?? new RoundFacts() }, ct);
        var after = job.Tree.Verify();

        return after.Length == 0 ? result : result with { State = CheckState.Failed, Note = $"{after} The result of the checks is not trusted. {result.Note}" };
    }

    /// <summary>Runs the checks of the job whose worktree is <paramref name="request"/>'s tree, with the toolchain of <paramref name="baseSha"/>. <paramref name="tree"/> reads the worktree's files.</summary>
    public async Task<CheckResult> RunAsync(IWorkFolder tree, string baseSha, CheckRequest request, CancellationToken ct = default)
    {
        var atBase = await ReadFileAtAsync(request.Job?.Tree.MainCheckout ?? request.Tree, baseSha, ct);
        if (atBase.Trouble.Length > 0)
        {
            return new CheckResult(CheckState.ToolchainRefused, [], atBase.Trouble);
        }

        var baseFile = ToolchainFile.Read(atBase.File);
        if (request.Job is not { } job)
        {
            var change = Compare(atBase.File, tree.Read(ToolchainTables.FilePath, ToolchainTables.MaxBytes));
            var result = await runner.RunAsync(baseFile, request, ct);

            return result with { ToolchainChange = change, Note = result.Note + Reported(change, baseSha, headRuns: false) };
        }

        return await RunCoderJobCoreAsync(job, baseSha, atBase.File, baseFile, request, ct);
    }

    /// <summary>
    /// The toolchain the HEAD's definitions resolve to, for the formatter turn (E4 s5 t1): the same reading as the checks of the round — the head commit and its toolchain file from the main repository through the hardened reads — and nothing is run. Null when the head or its file cannot be read.
    /// </summary>
    public async Task<ToolchainResolution?> ResolveForFormatAsync(BoundaryJob job, string baseSha, CancellationToken ct = default)
    {
        var atBase = await ReadFileAtAsync(job.Tree.MainCheckout, baseSha, ct);
        var head = await git.InCoderTreeAsync(job.Tree, ["--no-replace-objects", "rev-parse", "HEAD"], ct);
        var headSha = head.Ran && head.Ok ? head.Out.Trim() : string.Empty;
        if (atBase.Trouble.Length > 0 || !GitHistory.IsCommitish(headSha))
        {
            return null;
        }

        var headFile = await ReadFileAtAsync(job.Tree.MainCheckout, headSha, ct);
        var file = ToolchainFile.Read(headFile.File);
        if (headFile.Trouble.Length > 0 || file is ToolchainRead.Refused)
        {
            return null;
        }

        return (await ResolveAsync(file, ToolchainFile.Read(atBase.File), job, baseSha, headSha, ct)).Resolution;
    }

    /// <summary>
    /// The checks of a coder job run the HEAD's definitions (owner decision 2026-11-26, which replaced the interim gate "frozen build definitions": nothing the head changes is refused). The head commit, its toolchain file and what it changed against the base are read from the MAIN repository through
    /// the hardened reads; the difference is REPORTED (<see cref="CheckResult.ChangedFiles"/>, <see cref="CheckResult.HeadTests"/>, the resolved toolchain's changes) and never judged here. A head that cannot be read, or whose toolchain file does not parse, is a refusal because nothing can be run.
    /// </summary>
    private async Task<CheckResult> RunCoderJobCoreAsync(BoundaryJob job, string baseSha, WorkFile baseFileText, ToolchainRead baseFile, CheckRequest request, CancellationToken ct)
    {
        var head = await git.InCoderTreeAsync(job.Tree, ["--no-replace-objects", "rev-parse", "HEAD"], ct);
        var headSha = head.Ran && head.Ok ? head.Out.Trim() : string.Empty;
        if (!GitHistory.IsCommitish(headSha))
        {
            return new CheckResult(CheckState.ToolchainRefused, [], "the head commit of the worktree could not be read, so nothing was run");
        }

        var headFile = await ReadFileAtAsync(job.Tree.MainCheckout, headSha, ct);
        if (headFile.Trouble.Length > 0)
        {
            return new CheckResult(CheckState.ToolchainRefused, [], headFile.Trouble);
        }

        var change = Compare(baseFileText, headFile.File);
        var raw = await git.RawChangesAsync(job.Tree.MainCheckout, baseSha, headSha, FrozenDefinitions.MaxChars, ct);
        var changes = raw.Ran && raw.Ok && !raw.Truncated ? FrozenDefinitions.ParseRaw(raw.Out) : null;
        var file = ToolchainFile.Read(headFile.File);
        request = request with { Facts = request.Facts with { ChangedPaths = changes is null ? [] : [.. changes.Where(change => change.Status is 'A' or 'M' or 'T' && !change.IsLinkOrSubmodule).Select(change => change.Path)], InHead = async (path, token) => await git.EntryAtAsync(job.Tree.MainCheckout, headSha, path, token) is { Ran: true, Ok: true } shown && HeadEntry.IsRegularFile(shown.Out, path) } };
        var result = file is ToolchainRead.Refused
            ? await runner.RunAsync(file, request, ct)
            : await RunDetectedAsync(file, baseFile, job, baseSha, headSha, request, ct);

        return result with
        {
            ToolchainChange = change,
            ChangedFiles = changes is null ? null : FrozenDefinitions.Classify(changes, ToolchainDefaults.Current),
            HeadTests = changes is null ? null : FrozenDefinitions.TestFiles(changes),
            Note = result.Note + Reported(change, baseSha, headRuns: true),
        };
    }

    /// <summary>
    /// What the checks of a CODER job are (epoch 3 E4 s3 t3; SPEC §A1, §A4): the toolchain file of the HEAD decides (the base's when the head left it alone); with none, or for a project that names no command, the manifests of the BASE commit and of the HEAD commit — the commit coai made, read from the MAIN repository's objects through the
    /// hardened reads, never from the worktree: an untracked or ignored manifest in the worktree changes nothing — resolve into steps by <see cref="ToolchainResolve"/>, which runs the HEAD's definitions and REPORTS what the head changed, never refuses it (owner decision 2026-11-26). The manifests are read only when the answer needs them: a file that states every command needs neither.
    /// </summary>
    private async Task<CheckResult> RunDetectedAsync(ToolchainRead file, ToolchainRead baseFile, BoundaryJob job, string baseSha, string headSha, CheckRequest request, CancellationToken ct)
    {
        var read = await ResolveAsync(file, baseFile, job, baseSha, headSha, ct);
        AffectedSelection? selection = null;
        if (read.Resolution is ToolchainResolution.Resolved resolved && read.AtBase is not null && read.AtHead is not null)
        {
            // Level one of the selection (SPEC §E), INFORMATION ONLY in this version (M13 of the verify): every detected unit runs, and the envelope says what the selection would have left out. The diff is read from the main repository through the hardened reads.
            var diff = await git.NameStatusAsync(job.Tree.MainCheckout, baseSha, read.HeadSha, 4 * 1024 * 1024, ct);
            selection = AffectedProjects.Select(resolved.Toolchain, diff.Ran && diff.Ok && !diff.Truncated ? AffectedProjects.ParseNameStatus(diff.Out) : null, read.AtBase, read.AtHead);
        }

        return await runner.RunResolvedAsync(read.Resolution, request, ct, selection);
    }

    private sealed record Read(ToolchainResolution Resolution, ManifestSet? AtBase, ManifestSet? AtHead, string HeadSha);

    private async Task<Read> ResolveAsync(ToolchainRead file, ToolchainRead baseFile, BoundaryJob job, string baseSha, string headSha, CancellationToken ct)
    {
        var setting = Environment(ToolchainResolve.DetectVariable);
        var detect = ToolchainResolve.DetectOn(setting, []);
        var needs = detect && file is not ToolchainRead.Refused && (file is not ToolchainRead.Configured configured || configured.Toolchain.Projects.Any(project => !project.StatesACommand));
        ManifestRead atBase = new ManifestRead.Refused("the manifests of the base commit were not read");
        ManifestRead atHead = new ManifestRead.Refused("the manifests of the head commit were not read");
        if (needs)
        {
            var manifests = new ManifestsAtCommit(git);
            atBase = await manifests.ReadAsync(job.Tree.MainCheckout, baseSha, ToolchainDefaults.Current, ct);
            atHead = await manifests.ReadAsync(job.Tree.MainCheckout, headSha, ToolchainDefaults.Current, ct);
        }

        return new Read(ToolchainResolve.Resolve(file, () => atBase, () => atHead, setting, ToolchainDefaults.Current, baseFile), atBase is ManifestRead.Found foundBase ? foundBase.Set : null, atHead is ManifestRead.Found foundHead ? foundHead.Set : null, headSha);
    }

    /// <summary>
    /// The toolchain file AT THE BASE COMMIT of the main repository, as the reader reads it — the one reading for everything that is declared there, the checks and the instruction step (epoch 3 E3 s7 t3). A base commit that is not there or a file git could not hand over is a
    /// refusal with the trouble named, never "no file".
    /// </summary>
    public async Task<ToolchainRead> ToolchainAtBaseAsync(string repository, string baseSha, CancellationToken ct = default)
    {
        var atBase = await ReadFileAtAsync(repository, baseSha, ct);

        return atBase.Trouble.Length > 0 ? new ToolchainRead.Refused([atBase.Trouble]) : ToolchainFile.Read(atBase.File);
    }

    private sealed record BaseFile(WorkFile File, string Trouble);

    private async Task<BaseFile> ReadFileAtAsync(string repository, string baseSha, CancellationToken ct)
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

    private static BaseFile Unreadable(string trouble) => new(new WorkFile.Missing(), $"the toolchain file was not read, so nothing was run: {trouble}");

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

    private static string Reported(ToolchainChange change, string baseSha, bool headRuns) => change == ToolchainChange.Unchanged
        ? string.Empty
        : headRuns
            ? $" The head {Verb(change)} {ToolchainTables.FilePath}; this job ran the head's file (owner decision 2026-11-26)."
            : $" The worktree {Verb(change)} {ToolchainTables.FilePath}; this job ran the file at base {baseSha[..Math.Min(8, baseSha.Length)]} and the change did not run.";

    private static string Verb(ToolchainChange change) => change switch
    {
        ToolchainChange.Added => "added",
        ToolchainChange.Removed => "removed",
        _ => "changed",
    };
}
