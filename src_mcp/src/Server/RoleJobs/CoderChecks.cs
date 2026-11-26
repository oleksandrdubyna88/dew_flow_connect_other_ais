using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Work;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>One step of one round of checks as the envelope's <c>checks</c> lists it (<c>server.md</c> § 10): what ran, how it ended, how long it took and where its log is.</summary>
/// <param name="Project">The project of the toolchain file the step belongs to (<c>app</c>, <c>src_mcp</c>); there can be several projects, each with a build and a test.</param>
/// <param name="Step">"restore", "build", "typecheck" or "test".</param>
/// <param name="Argv">The argument list as it was started: <c>{jobs}</c> replaced, no shell added by coai.</param>
/// <param name="LogRef">The step's log, relative to the job folder and written with <c>/</c>.</param>
internal sealed record CoderCheckStep(string Project, string Step, IReadOnlyList<string> Argv, int ExitCode, long DurationMs, string LogRef)
{
    /// <summary>The network state the step ran with (<c>host</c> or <c>none</c>), from the record of its unit (epoch 3 E4 s3 t4); empty for a step that never reached a unit.</summary>
    public string Network { get; init; } = string.Empty;

    /// <summary>The folder the step started in, relative to the repository root; <c>.</c> is the root.</summary>
    public string WorkingFolder { get; init; } = ".";
}

/// <summary>
/// One round of checks on one commit of a coder job — after turn 0 and after each fix turn. <see cref="State"/> is the wire word of the checks (<c>green</c>, <c>failed</c>, <c>not_configured</c>, <c>toolchain_refused</c>, <c>boundary_refused</c>) or
/// <c>tree_changed</c>: the worktree or HEAD moved while the checks ran, so what they said is not trusted (plan gate, epoch 3 E2).
/// </summary>
/// <param name="Turn">0 for the checks of turn 0's commit, k for those of fix turn k's.</param>
/// <param name="HeadSha">The commit the checks ran on — the head coai made, and verified unchanged through the run.</param>
/// <param name="Note">One sentence: why nothing ran, which step failed, or that everything passed; the tree change, when there was one.</param>
/// <param name="Digest">What a failed step goes back to the coder as (<see cref="FailureDigest"/>); empty for every other state.</param>
internal sealed record CoderChecksRound(int Turn, string HeadSha, string State, IReadOnlyList<CoderCheckStep> Steps, string Note, IReadOnlyList<DigestItem> Digest)
{
    /// <summary>Where the checks of the round came from (the file, the defaults of a folder, or the manifests of the base and the head), what they were and what differed between the base and the head; null when the round never reached the resolving (epoch 3 E4 s3 t4).</summary>
    public ResolvedToolchain? Toolchain { get; init; }

    /// <summary>What level one of the selection decided: the projects that ran and each one left out with its reason (E4 s3 t6); null when nothing was selected.</summary>
    public AffectedSelection? Selection { get; init; }

    /// <summary>How many test files the head modified, added and deleted (S21 of pass 3): the gate lets a head edit tests, so the envelope says how many it touched.</summary>
    public TestFileCounts? HeadTests { get; init; }

    public const string TreeChanged = "tree_changed";

    /// <summary>Only a run that ran every step, saw every one pass, and left the worktree and HEAD where they were.</summary>
    public bool Green => State == "green";

    /// <summary>A step ran and did not pass, and nothing else is wrong: the one state a fix turn can answer.</summary>
    public bool Red => State == "failed";

    /// <summary>
    /// The word the job stops with when this round is the last, or empty for green. <c>not-green</c> is a red round no fix turn is left for; the others are never sent to the coder: a missing or wrong toolchain file, a boundary that did not hold and a tree
    /// the checks changed are not faults of the coder's code.
    /// </summary>
    public string Stop => State switch
    {
        "green" => string.Empty,
        "not_configured" => "not-configured",
        "toolchain_refused" => "toolchain-refused",
        "boundary_refused" => RoleStopWords.CoderBoundary,
        TreeChanged => "checks-changed-tree",
        _ => "not-green",
    };
}

/// <summary>
/// The checks of a coder job (epoch 3 E2 s4 t1; <c>v2/architecture/server.md</c> §§ 10, 12): one round runs the base toolchain on the commit coai just made, INSIDE the OS boundary, by the one road (<see cref="ChecksAtBase.RunCoderJobAsync"/> — the
/// <c>.git</c> pointer compared before and after, the toolchain read at the base commit from the main repository), and checks that the worktree and HEAD are the same after as before.
/// </summary>
/// <remarks>
/// <para><b>Order per turn:</b> format, commit, checks of THAT commit. The toolchain schema names a build and a test per project and no formatter (<see cref="CheckStep"/>), so the format slot is kept and recorded as <c>not_configured</c> (<see cref="FormatNote"/>);
/// when a formatter step exists it runs before the commit and its own commit is <c>style: format (coai)</c>.</para>
/// <para><b>Unchanged through the checks.</b> Before the run the worktree's state (<c>git status</c>, untracked files included, ignored ones not) and HEAD are read; after it they are read again. A check that rewrote a tracked file, left a file git would add, or
/// moved HEAD is never green, whatever the steps exited with: the next commit would carry what the checks made, and the coder is not the one who wrote it. Both reads are coai's hardened git, which compares the <c>.git</c> pointer first.</para>
/// <para><b>Evidence, never a verdict on the coder:</b> the logs are under <c>checks/round-&lt;turn&gt;/</c> in the job folder; a step's log is the digest's source (<see cref="FailureDigest"/>).</para>
/// </remarks>
internal sealed class CoderChecks(IRoleRun run, GitHistory git, CoderOsLayer layer, CoderTree tree, string baseSha)
{
    /// <summary>What <c>{jobs}</c> becomes in an argv: the checks of a coder job share the machine with its siblings (<c>server.md</c> § 9).</summary>
    public const int Jobs = 2;

    /// <summary>Said in the envelope where the order of a turn has its format slot.</summary>
    public const string FormatNote = "the toolchain file has no formatter step (it names a build and a test per project), so no format was run";

    public async Task<CoderChecksRound> RunAsync(int turn, string head, CancellationToken ct)
    {
        run.Phase("checks", turn);
        var clean = await ProveEqualToTheCommitAsync(ct);
        if (clean.Length > 0)
        {
            return Unverified(turn, head, clean);
        }

        var before = await StateAsync(ct);
        if (before.Sentence.Length > 0 || before.Head != head)
        {
            return Unverified(turn, head, before.Sentence.Length > 0 ? before.Sentence : $"the worktree is at {before.Head}, not at the commit {head} the checks were to run on; nothing was run");
        }

        var logs = Path.Combine(run.Folder, "checks", $"round-{turn}");
        var result = await new ChecksAtBase(git, new CheckRunner(layer.Launcher, layer.ChecksBoundary()))
            .RunCoderJobAsync(new DiskWorkFolder(tree.Root), baseSha, layer.Job, logs, Jobs, ct, step => RecordStep(logs, step));
        RecordRefusal(result, logs);
        RecordRefusedPaths(result, logs);
        ct.ThrowIfCancellationRequested();
        var after = await StateAsync(ct);
        if (Moved(before, after) is { Length: > 0 } moved)
        {
            return Unverified(turn, head, moved, Steps(result));
        }

        return new CoderChecksRound(turn, head, result.State.Wire(), Steps(result), result.Note, result.State == CheckState.Failed ? DigestOf(result) : []) { Toolchain = result.Toolchain, Selection = result.Selection, HeadTests = result.HeadTests };
    }

    /// <summary>
    /// The record of a check unit, beside its log, the way the coder's own launches are recorded (<see cref="CoderUnitRecord"/>): <c>unit-s&lt;n&gt;.json</c> for step n of the round — the unit's properties, its network and the rows of its preflight — written the moment the step
    /// has ended, before the next one starts, so a cancel or a hang in a later step keeps the records of the steps that ran. The number is the one the runner gave the step (<see cref="StepRecord.Number"/>), not one read back from a file name.
    /// A record that cannot be written is not a fault of the checks: it is one warning line in the runner's log, naming the part and no content.
    /// </summary>
    private void RecordStep(string logs, StepRecord step)
    {
        if (step.Boundary.Boundary != BoundaryRecord.UnitBoundary || step.Number <= 0)
        {
            return;
        }

        var part = $"s{step.Number}";
        Record(logs, part, CoderUnitRecord.CheckUnitName(layer.Job, part), step.Boundary, step.Started, $"{step.Project} {step.Step.Wire()}");
    }

    /// <summary>S24 of pass 3: the paths the gate refused go into the job's record (<c>refused-paths.txt</c> beside the round's logs, spelled by <see cref="PathSpelling"/>), not into the sentence the model reads.</summary>
    private void RecordRefusedPaths(CheckResult result, string logs)
    {
        if (result.RefusedPaths.Count == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(logs);
            File.WriteAllLines(Path.Combine(logs, "refused-paths.txt"), result.RefusedPaths);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            run.Log.Warning("role job {JobId}: the refused paths of the checks could not be written", run.Request.JobId);
        }
    }

    /// <summary><c>unit-preflight.json</c> when the preflight refused the checks and no unit of a step ran.</summary>
    private void RecordRefusal(CheckResult result, string logs)
    {
        if (result.State == CheckState.BoundaryRefused && result.Boundary.Boundary == BoundaryRecord.UnitBoundary)
        {
            Record(logs, "preflight", CoderUnitRecord.CheckUnitName(layer.Job, "pre"), result.Boundary, started: false, "the preflight of the checks");
        }
    }

    private void Record(string logs, string part, string unit, BoundaryRecord record, bool started, string what)
    {
        if (!CoderUnitRecord.Write(logs, part, unit, record, layer.Mounts, started, what))
        {
            run.Log.Warning("role job {JobId}: the record of the check unit {Part} could not be written", run.Request.JobId, part);
        }
    }

    private CoderChecksRound Unverified(int turn, string head, string sentence, IReadOnlyList<CoderCheckStep>? steps = null) =>
        new(turn, head, CoderChecksRound.TreeChanged, steps ?? [], sentence, []);

    private IReadOnlyList<CoderCheckStep> Steps(CheckResult result) =>
    [
        .. result.Steps.Select(step => new CoderCheckStep(
            step.Project, step.Step.Wire(), step.Argv, step.ExitCode, (long)step.Duration.TotalMilliseconds,
            Path.GetRelativePath(run.Folder, step.LogPath).Replace(Path.DirectorySeparatorChar, '/')) { Network = step.Boundary.Network, WorkingFolder = step.WorkingFolder }),
    ];

    /// <summary>The digest of the first step that did not pass, read from its log — the machine output of the step, redacted and bounded.</summary>
    private IReadOnlyList<DigestItem> DigestOf(CheckResult result)
    {
        if (result.FailedStep is not { } step)
        {
            return [];
        }

        return FailureDigest.Of(step.Step, step.Adapter, ReadLog(step.LogPath), tree.Root).Items;
    }

    private static string ReadLog(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// M6 of the security verify: the checks read the COMMIT (detection, the definitions, the gate) and run in the WORKTREE, and a file git ignores is in neither the commit nor the status — so before every round the worktree is proven equal to the commit, ignored files included: <c>git clean -ffdx</c>
    /// through the hardened door, then <c>status --porcelain --ignored</c> must be empty. Anything else refuses the round with a fixed sentence (no path of the tree is named). Empty when the tree is clean.
    /// </summary>
    private async Task<string> ProveEqualToTheCommitAsync(CancellationToken ct)
    {
        var cleaned = await git.InCoderTreeAsync(tree, ["clean", "-ffdxq"], ct);
        if (!cleaned.Ran || !cleaned.Ok)
        {
            return cleaned.Refusal.Length > 0 ? cleaned.Refusal : "git could not clean the worktree before the checks, so the checks are not trusted and nothing was run";
        }

        var status = await git.InCoderTreeAsync(tree, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignored"], ct);
        if (!status.Ran || !status.Ok)
        {
            return status.Refusal.Length > 0 ? status.Refusal : "git could not read the worktree's status before the checks, so the checks are not trusted and nothing was run";
        }

        if (status.Out.Length != 0)
        {
            return "the worktree is not equal to the commit the checks were to run on (after git clean -ffdx, git status --ignored still shows changes), so the checks are not trusted and nothing was run";
        }

        // S22 of pass 3: neither the clean nor the status sees a folder named .git inside a tracked folder (git takes it for a nested repository and leaves it alone).
        return HasANestedGit(tree.Root)
            ? "the worktree holds a .git entry below its root, which git's clean and status do not look into, so the checks are not trusted and nothing was run"
            : string.Empty;
    }

    private static bool HasANestedGit(string root)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true, MatchType = MatchType.Simple };
        var own = Path.Combine(root, ".git");

        return Directory.EnumerateFileSystemEntries(root, ".git", options).Any(entry => !string.Equals(entry, own, StringComparison.Ordinal));
    }

    /// <summary>The worktree as git sees it: HEAD and the porcelain status (tracked changes and untracked files that are not ignored), or the sentence that says git could not answer.</summary>
    private sealed record TreeState(string Head, string Status, string Sentence);

    private async Task<TreeState> StateAsync(CancellationToken ct)
    {
        var head = await CoderCommits.HeadAsync(git, tree, ct);
        if (head.Failure is { } failure)
        {
            return new TreeState(string.Empty, string.Empty, $"coai could not read the worktree's HEAD, so the checks are not trusted: {failure.Reason}");
        }

        var status = await git.InCoderTreeAsync(tree, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all"], ct);

        return status.Ran && status.Ok
            ? new TreeState(head.Head, status.Out, string.Empty)
            : new TreeState(head.Head, string.Empty, status.Refusal.Length > 0 ? status.Refusal : "git could not read the worktree's status, so the checks are not trusted");
    }

    /// <summary>The sentence for a worktree or HEAD that moved through the run, or empty. Names the paths.</summary>
    private static string Moved(TreeState before, TreeState after)
    {
        if (after.Sentence.Length > 0)
        {
            return after.Sentence;
        }

        if (before.Head != after.Head)
        {
            return $"HEAD moved from {before.Head} to {after.Head} while the checks ran, so what they said is not trusted";
        }

        if (string.Equals(before.Status, after.Status, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var paths = after.Status.Split('\0', StringSplitOptions.RemoveEmptyEntries).Except(before.Status.Split('\0', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal).Select(entry => entry.Length > 3 ? entry[3..] : entry).Take(8).ToList();

        return $"the checks changed the worktree ({(paths.Count > 0 ? string.Join(", ", paths) : "the status differs")}), so what they said is not trusted and nothing of it is committed";
    }
}
