using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Work;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>The worktree as git sees it: HEAD and the porcelain status (tracked changes and untracked files that are not ignored), or the sentence that says git could not answer (<see cref="CoderChecks.StateAsync"/>).</summary>
internal sealed record CoderTreeState(string Head, string Status, string Sentence);

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

    /// <summary><c>full</c>, <c>selected</c> or <c>rerun</c> (E4 s5 t3, t5): which run of the test this record is.</summary>
    public string Scope { get; init; } = "full";

    /// <summary>How many files a selected run was given.</summary>
    public int ScopeFiles { get; init; }

    /// <summary>The step failed and a clean re-run excused its failures: it counts as passed and the result's <c>flaky</c> says which.</summary>
    public bool Excused { get; init; }
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

    /// <summary>The tests the one re-run excused in this round, each with its proof (E4 s5 t5).</summary>
    public IReadOnlyList<FlakyTest> Flaky { get; init; } = [];

    /// <summary>What the formatter turn before this round did (E4 s5 t1; SPEC D): never a verdict on the round.</summary>
    public FormatResult Format { get; init; } = FormatResult.NotConfigured;

    /// <summary>How many test files the head modified, added and deleted (S21 of pass 3): the gate lets a head edit tests, so the envelope says how many it touched.</summary>
    public TestFileCounts? HeadTests { get; init; }

    /// <summary>What the head did to the definitions of the checks: the flag and the one line (owner decision 2026-11-26).</summary>
    public DefinitionReport Definitions { get; init; } = DefinitionReport.None;

    /// <summary>The definition and configuration files, symlinks and submodules the head changed, for the job's record.</summary>
    public ChangedFiles? ChangedFiles { get; init; }

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

    public async Task<CoderChecksRound> RunAsync(int turn, string head, CancellationToken ct, FormatResult? format = null, IReadOnlyList<CoderCheckStep>? formatSteps = null)
    {
        var round = await RunRoundAsync(turn, head, ct, format);

        return formatSteps is { Count: > 0 } ? round with { Steps = [.. formatSteps, .. round.Steps] } : round;
    }

    private async Task<CoderChecksRound> RunRoundAsync(int turn, string head, CancellationToken ct, FormatResult? format)
    {
        run.Phase("checks", turn);
        var clean = await ProveEqualToTheCommitAsync(ct);
        if (clean.Length > 0)
        {
            return Unverified(turn, head, clean) with { Format = format ?? FormatResult.NotConfigured };
        }

        var before = await StateAsync(git, tree, ct);
        if (before.Sentence.Length > 0 || before.Head != head)
        {
            return Unverified(turn, head, before.Sentence.Length > 0 ? before.Sentence : $"the worktree is at {before.Head}, not at the commit {head} the checks were to run on; nothing was run");
        }

        var logs = Path.Combine(run.Folder, "checks", $"round-{turn}");
        var result = await new ChecksAtBase(git, new CheckRunner(layer.Launcher, layer.ChecksBoundary()))
            .RunCoderJobAsync(new DiskWorkFolder(tree.Root), baseSha, layer.Job, logs, Jobs, ct, step => RecordStep(logs, step), Facts());
        NoteFlaky(result);
        RecordRefusal(result, logs);
        RecordDefinitions(result, logs);
        ct.ThrowIfCancellationRequested();
        var after = await StateAsync(git, tree, ct);
        if (Moved(before, after) is { Length: > 0 } moved)
        {
            return Unverified(turn, head, moved, Steps(result));
        }

        return new CoderChecksRound(turn, head, result.State.Wire(), Steps(result), result.Note, result.State == CheckState.Failed ? DigestOf(result) : []) { Flaky = result.Flaky, Format = format ?? FormatResult.NotConfigured, Toolchain = result.Toolchain, Selection = result.Selection, HeadTests = result.HeadTests, Definitions = DefinitionReport.Of(result.Toolchain?.Changes, result.ChangedFiles, result.ToolchainChange, result.HeadTests), ChangedFiles = result.ChangedFiles };
    }

    /// <summary>
    /// The record of a check unit, beside its log, the way the coder's own launches are recorded (<see cref="CoderUnitRecord"/>): <c>unit-s&lt;n&gt;.json</c> for step n of the round — the unit's properties, its network and the rows of its preflight — written the moment the step
    /// has ended, before the next one starts, so a cancel or a hang in a later step keeps the records of the steps that ran. The number is the one the runner gave the step (<see cref="StepRecord.Number"/>), not one read back from a file name.
    /// A record that cannot be written is not a fault of the checks: it is one warning line in the runner's log, naming the part and no content.
    /// </summary>
    private void RecordStep(string logs, StepRecord step) => RecordStep(run, layer, logs, step);

    /// <summary>The same record for a step of the formatter turn (S5 of the s5 verify): its restore and its format step are units too.</summary>
    internal static void RecordStep(IRoleRun run, CoderOsLayer layer, string logs, StepRecord step)
    {
        if (step.Boundary.Boundary != BoundaryRecord.UnitBoundary || step.Number <= 0)
        {
            return;
        }

        var part = $"s{step.Number}";
        Record(run, layer, logs, part, CoderUnitRecord.CheckUnitName(layer.Job, part), step.Boundary, step.Started, $"{step.Project} {step.Step.Wire()}");
    }

    /// <summary>
    /// What the head changed in the definitions of the checks goes into the job's record (<c>changed-definitions.txt</c> beside the round's logs): the definition and configuration files, symlinks and submodules (spelled by <see cref="PathSpelling"/>, at most 200), the steps the base had and the head lost, and the changed
    /// definition members. The sentence the model reads names none of it (owner decision 2026-11-26: reported, never refused).
    /// </summary>
    private void RecordDefinitions(CheckResult result, string logs)
    {
        var changes = result.Toolchain?.Changes ?? DefinitionChanges.None;
        if (result.ChangedFiles is not { Total: > 0 } && !changes.Any)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(logs);
            var lines = new List<string>();
            lines.AddRange(DefinitionReport.Of(changes, result.ChangedFiles, result.ToolchainChange, result.HeadTests).Summary.Split('\n'));
            lines.AddRange((result.ChangedFiles?.Paths ?? []).Take(200).Select(path => "file: " + PathSpelling.Spell(path)));
            lines.AddRange(changes.LostSteps.Select(line => "lost: " + line));
            lines.AddRange(changes.ChangedDefinitions.Select(line => "changed: " + line));
            File.WriteAllLines(Path.Combine(logs, "changed-definitions.txt"), lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            run.Log.Warning("role job {JobId}: the record of the changed definitions could not be written", run.Request.JobId);
        }
    }

    private const string FullFailedFile = "full-failed";

    private const string ExcusedFile = "flaky-excused.txt";

    private const string NoticedFile = "flaky-noticed";

    private string JobChecks => Path.Combine(run.Folder, "checks");

    /// <summary>What the round knows about the job (E4 s5 t3, t5): the setting, and the two facts kept in files of the job folder — a full test run failed once, and which tests were excused — so they hold across models and rounds.</summary>
    private RoundFacts Facts() => new()
    {
        FullFailedBefore = File.Exists(Path.Combine(JobChecks, FullFailedFile)),
        OnFullFailed = () => Keep(FullFailedFile, "a full test run failed once\n", append: false),
        FlakyReruns = FlakyReruns.Read(run.Settings).Count,
        ExcusedNames = ReadExcused(),
        OnExcused = tests => Keep(ExcusedFile, string.Concat(tests.Select(test => test.Name + "\n")), append: true),
    };

    private List<string> ReadExcused()
    {
        try
        {
            return File.Exists(Path.Combine(JobChecks, ExcusedFile)) ? [.. File.ReadAllLines(Path.Combine(JobChecks, ExcusedFile)).Where(line => line.Length > 0)] : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    /// <summary>A fact of the job, written with a temporary file and a rename.</summary>
    private void Keep(string name, string text, bool append)
    {
        try
        {
            Directory.CreateDirectory(JobChecks);
            var path = Path.Combine(JobChecks, name);
            var content = append && File.Exists(path) ? File.ReadAllText(path) + text : text;
            var temp = path + ".tmp";
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            run.Log.Warning("role job {JobId}: the job's fact {Fact} could not be written", run.Request.JobId, name);
        }
    }

    /// <summary>ONE coder-test-flaky notice for the job, when a re-run excused a test.</summary>
    private void NoteFlaky(CheckResult result)
    {
        if (result.Flaky.Count == 0 || File.Exists(Path.Combine(JobChecks, NoticedFile)))
        {
            return;
        }

        Keep(NoticedFile, "noticed\n", append: false);
        run.Notice(RoleJobNotices.TestFlaky(run.Job, result.Flaky.Count, run.Clock.GetUtcNow().UtcDateTime));
    }

    /// <summary><c>unit-preflight.json</c> when the preflight refused the checks and no unit of a step ran.</summary>
    private void RecordRefusal(CheckResult result, string logs)
    {
        if (result.State == CheckState.BoundaryRefused && result.Boundary.Boundary == BoundaryRecord.UnitBoundary)
        {
            Record(logs, "preflight", CoderUnitRecord.CheckUnitName(layer.Job, "pre"), result.Boundary, started: false, "the preflight of the checks");
        }
    }

    private void Record(string logs, string part, string unit, BoundaryRecord record, bool started, string what) => Record(run, layer, logs, part, unit, record, started, what);

    private static void Record(IRoleRun run, CoderOsLayer layer, string logs, string part, string unit, BoundaryRecord record, bool started, string what)
    {
        if (!CoderUnitRecord.Write(logs, part, unit, record, layer.Mounts, started, what))
        {
            run.Log.Warning("role job {JobId}: the record of the check unit {Part} could not be written", run.Request.JobId, part);
        }
    }

    private CoderChecksRound Unverified(int turn, string head, string sentence, IReadOnlyList<CoderCheckStep>? steps = null) =>
        new(turn, head, CoderChecksRound.TreeChanged, steps ?? [], sentence, []);

    private IReadOnlyList<CoderCheckStep> Steps(CheckResult result) => StepsOf(run, result.Steps);

    /// <summary>The envelope's rows for the records of a run of steps.</summary>
    internal static IReadOnlyList<CoderCheckStep> StepsOf(IRoleRun run, IEnumerable<StepRecord> records) =>
    [
        .. records.Select(step => new CoderCheckStep(
            step.Project, step.Step.Wire(), step.Argv, step.ExitCode, (long)step.Duration.TotalMilliseconds,
            Path.GetRelativePath(run.Folder, step.LogPath).Replace(Path.DirectorySeparatorChar, '/')) { Network = step.Boundary.Network, WorkingFolder = step.WorkingFolder, Scope = step.Scope, ScopeFiles = step.ScopeFiles, Excused = step.Excused }),
    ];

    /// <summary>The digest of the first step that did not pass, read from its log — the machine output of the step, redacted and bounded.</summary>
    private IReadOnlyList<DigestItem> DigestOf(CheckResult result)
    {
        if (result.FailedStep is not { } step)
        {
            return [];
        }

        var runner = result.Toolchain?.Projects.FirstOrDefault(project => project.Id == step.Project || project.Detected?.Id == step.Project)?.Detected?.Fact("runner") ?? string.Empty;

        return FailureDigest.Of(step.Step, step.Adapter, ReadLog(step.LogPath), tree.Root, runner, step.LogCut).Items;
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

    /// <summary>The worktree as git sees it — HEAD and the porcelain status — or the sentence that says git could not answer; the same reading before and after anything that must leave the tree as it found it.</summary>
    internal static async Task<CoderTreeState> StateAsync(GitHistory git, CoderTree tree, CancellationToken ct)
    {
        var head = await CoderCommits.HeadAsync(git, tree, ct);
        if (head.Failure is { } failure)
        {
            return new CoderTreeState(string.Empty, string.Empty, $"coai could not read the worktree's HEAD, so the checks are not trusted: {failure.Reason}");
        }

        var status = await git.InCoderTreeAsync(tree, ["--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all"], ct);

        return status.Ran && status.Ok
            ? new CoderTreeState(head.Head, status.Out, string.Empty)
            : new CoderTreeState(head.Head, string.Empty, status.Refusal.Length > 0 ? status.Refusal : "git could not read the worktree's status, so the checks are not trusted");
    }

    /// <summary>The sentence for a worktree or HEAD that moved through the run, or empty. Names the paths.</summary>
    internal static string Moved(CoderTreeState before, CoderTreeState after)
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
