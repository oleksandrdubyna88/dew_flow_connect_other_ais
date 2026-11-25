using System.Globalization;
using CoaiMcp.Core;
using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Work;
using CoaiMcp.Roles;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>The work-method limits a plan folder is validated with, and where they came from: <c>device</c> when a person set them, <c>default</c> when the shipped numbers stand — the two words <c>validate_work</c> answers.</summary>
internal sealed record DeviceLimits(WorkLimits Limits, string Source)
{
    /// <summary>The shipped numbers, reading no setting and no environment.</summary>
    internal static Func<DeviceLimits> Shipped { get; } = () => From(_ => null);

    /// <summary>The limits of <paramref name="layered"/> — the environment over the settings file of a data directory — exactly as <c>--validate-work</c> computes them.</summary>
    internal static DeviceLimits From(Func<string, string?> layered) =>
        new(RolesAnswer.Compute(layered, string.Empty, string.Empty, claudeHook: false).WorkMethod.ToLimits(), layered(RolesKeys.WorkMethod) is { Length: > 0 } ? "device" : "default");

    /// <summary>The environment over the settings file of <paramref name="dataDir"/>, read and never written.</summary>
    internal static Func<string, string?> Layered(string dataDir, Func<string, string?> environment)
    {
        var file = SettingsFile.Read(Path.Combine(dataDir, SettingsFile.Name));

        return name => environment(name) is { Length: > 0 } fromEnvironment ? fromEnvironment : file.GetValueOrDefault(name);
    }
}

/// <summary>What a call named as a feature folder, with the rest of the call.</summary>
/// <param name="Folder">The repository-relative folder, as sent (trimmed).</param>
/// <param name="WorkItem">The scope as sent: the folder of an epoch, an epic, a story or a task inside the feature folder, or empty for the whole feature.</param>
internal sealed record FolderCall(string Folder, string WorkItem, string Expected, string RepoPath, string Branch);

/// <summary>
/// <c>review_plan(featureFolder)</c> (epoch 3 E3 s3; <c>SPEC.md</c> section B): the plan a round reviews is a PACK the server makes of a committed work-plan folder — read from git at the branch tip, validated first with the device's limits, packed in
/// topological order, and recorded with an approval id over everything the reviewers read.
/// </summary>
/// <remarks>
/// <para><b>The order</b> is the cheap refusals first: the folder's shape (no git), the tip (once, the commit of the round), the folder at that commit (one listing; over 2 000 entries refused before any blob), the validator (at most
/// <see cref="MaxErrors"/> errors listed, the total, the limits' source), the pack (a file over 64 KiB or a pack over 256 KiB refused, never cut), and last the working-tree guard. Every refusal is answered before any round exists, so a
/// refusal launches no reviewer, writes no round and moves no stage.</para>
/// <para>The whole preparation runs under the deadline <see cref="PlanInputs"/> gives it; the work here only passes the token on.</para>
/// </remarks>
internal static class FeatureFolderPlan
{
    /// <summary>The most validator errors a refusal lists; the total is always said.</summary>
    internal const int MaxErrors = 10;

    internal static async Task<PlanInputOutcome> ReadAsync(
        FolderCall call, Func<string, string, Task<string>> resolve, IProcessLauncher launcher, Func<DeviceLimits> limits, CancellationToken ct)
    {
        var (folder, shape) = Shape(call.Folder);
        if (shape.Length > 0)
        {
            return Refuse(shape);
        }

        var (tip, refusal) = await PlanInputs.TipAsync(call.Expected, call.RepoPath, call.Branch, resolve, ct);
        if (refusal.Length > 0)
        {
            return Refuse(refusal);
        }

        var git = new GitHistory(launcher);
        var opened = await GitWorkFolder.OpenAsync(git, call.RepoPath, tip, folder, ct);

        return opened is FolderOpen.Opened open ? await ValidatedAsync(call, folder, tip, open.Folder, git, limits(), ct) : Refuse(((FolderOpen.Refused)opened).Sentence);
    }

    private static async Task<PlanInputOutcome> ValidatedAsync(FolderCall call, string folder, string tip, GitWorkFolder tree, GitHistory git, DeviceLimits limits, CancellationToken ct)
    {
        var reader = new StrictYamlReader();
        var report = new WorkPlanValidator(reader).Validate(tree, limits.Limits);
        if (!report.FeatureFound)
        {
            return Refuse($"'{folder}' has no feature.yaml at commit {GitIds.Short(tip)}, so it is not a work plan — pass the folder that holds feature.yaml, such as v2");
        }

        if (!report.Valid)
        {
            return Refuse(Invalid(folder, tip, report, limits));
        }

        var missed = await tree.PreloadAsync(WorkPack.NamedFiles(tree, reader), ct);
        if (missed.Length > 0)
        {
            return Refuse(missed);
        }

        var scope = call.WorkItem.Length == 0 ? folder : WorkItems.Check(call.WorkItem).Item;

        return WorkPack.Build(tree, reader, folder, scope) switch
        {
            PackOutcome.Packed packed => await GuardedAsync(call, tip, folder, packed.Pack, limits, git, ct),
            PackOutcome.Refused refused => Refuse(refused.Sentence),
            _ => throw new InvalidOperationException("a pack outcome is Packed or Refused"),
        };
    }

    private static async Task<PlanInputOutcome> GuardedAsync(FolderCall call, string tip, string folder, Pack pack, DeviceLimits limits, GitHistory git, CancellationToken ct)
    {
        var (refusal, note) = await WorkingTreeGuard.CheckAsync(git, call.RepoPath, tip, folder, pack, ct);
        if (refusal.Length > 0)
        {
            return Refuse(refusal);
        }

        var source = new PlanSource(PlanOrigin.Folder, tip, pack.Scope, pack.ApprovalId, pack.Bytes, DocumentRules.ArtifactIdOf(pack.Text));

        return new PlanInputOutcome.Ready(pack.Text, tip, source, WorkPack.SourceLine(pack, tip, limits.Source), note.Length > 0 ? "; " + note : string.Empty, source.TextId);
    }

    /// <summary>The folder as a path inside the repository — one spelling — or the sentence that says it is not one.</summary>
    private static (string Folder, string Refusal) Shape(string raw)
    {
        var path = RepoPaths.Canonical(raw).TrimEnd('/');
        var why = path.Any(char.IsControl) ? "holds a control character" : path.Contains(':') ? "is not a repository path" : RepoPaths.WhyNotRelative(path);

        return why.Length > 0
            ? (string.Empty, $"featureFolder '{raw.Trim().Replace("\0", "\\0", StringComparison.Ordinal)}' {why} — pass the folder that holds feature.yaml as a path from the repository root, such as v2")
            : (path, string.Empty);
    }

    /// <summary>The refusal for a plan that does not validate: the first errors (code, node, message), how many there are, and the limits it was checked against.</summary>
    private static string Invalid(string folder, string tip, WorkReport report, DeviceLimits limits)
    {
        var number = (int n) => n.ToString(CultureInfo.InvariantCulture);
        var total = report.Errors.Count;
        var listed = report.Errors.Take(MaxErrors).Select((error, index) => $"{index + 1}. {error.Code} at {(error.Node.Length > 0 ? error.Node : error.Path)} — {error.Message}");
        var count = report.Truncated ? $"at least {number(total)} errors" : $"{number(total)} error{(total == 1 ? string.Empty : "s")}";
        var shown = total > MaxErrors ? $"the first {number(MaxErrors)}" : "all";
        var method = limits.Limits;

        return $"'{folder}' at commit {GitIds.Short(tip)} is not a valid work plan: {count} ({shown} listed below); limits source {limits.Source} "
            + $"(epics per epoch {number(method.MaxEpicsPerEpoch)}, MVP epics {number(method.MvpMin)} to {number(method.MvpMax)}, stories per epic {number(method.MaxStoriesPerEpic)}, tasks per story {number(method.MaxTasksPerStory)}). "
            + $"No reviewer was launched and no round was started. Fix the plan and commit it (validate_work shows the same list):\n{string.Join('\n', listed)}";
    }

    private static PlanInputOutcome Refuse(string sentence) => new PlanInputOutcome.Refused(sentence);
}

/// <summary>
/// The working-tree guard of a plan folder round: when HEAD IS the commit the round reads, an edited, staged or untracked plan file would be reviewed as the commit while its author believed the edit was reviewed, so it is a refusal that names the files.
/// When HEAD is another commit the working tree says nothing about this one, so nothing is compared and the note says so.
/// </summary>
internal static class WorkingTreeGuard
{
    private const int Listed = 10;

    /// <summary>The refusal (or empty) and the note for the reviewer line (or empty).</summary>
    internal static async Task<(string Refusal, string Note)> CheckAsync(GitHistory git, string repoPath, string tip, string folder, Pack pack, CancellationToken ct)
    {
        var placed = await git.RootAndHeadAsync(repoPath, ct);
        var gate = CommittedPlanReader.CaptureGate(placed, "finding the repository root and HEAD");
        if (gate.Length > 0 || placed.Lines.Count != 2)
        {
            return (gate.Length > 0 ? gate : $"git rev-parse answered {placed.Lines.Count} lines for the root and HEAD, not 2 — report it with the git version", string.Empty);
        }

        if (!placed.Lines[1].Equals(tip, StringComparison.OrdinalIgnoreCase))
        {
            return (string.Empty, CommittedPlanReader.NotCompared(placed.Lines[1], tip));
        }

        var status = await git.FolderStatusAsync(placed.Lines[0], [folder], ct);
        var statusGate = CommittedPlanReader.CaptureGate(status, $"asking whether the plan files under '{folder}' are edited");

        return statusGate.Length > 0 ? (statusGate, string.Empty) : (Sentence(Differing(status.Out, pack), tip, pack.Scope), string.Empty);
    }

    /// <summary>The plan files that differ: a file of the pack that is edited, staged or deleted, and a new <c>.yaml</c> file under the scope (a node the pack could not hold).</summary>
    private static List<string> Differing(string porcelain, Pack pack)
    {
        var packed = pack.Sections.Select(section => section.Path).ToHashSet(StringComparer.Ordinal);
        var below = pack.Scope + "/";

        return [.. CoaiMcp.Runners.Consultation.PorcelainReader.Read(porcelain)
            .Where(entry => packed.Contains(entry.Path) || (entry.Code == "??" && entry.Path.EndsWith(".yaml", StringComparison.Ordinal) && entry.Path.StartsWith(below, StringComparison.Ordinal)))
            .Select(entry => entry.Path).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static string Sentence(List<string> files, string tip, string scope)
    {
        if (files.Count == 0)
        {
            return string.Empty;
        }

        var shown = string.Join(", ", files.Take(Listed).Select(file => $"'{file}'"));
        var more = files.Count > Listed ? $" and {(files.Count - Listed).ToString(CultureInfo.InvariantCulture)} more" : string.Empty;

        return $"{files.Count.ToString(CultureInfo.InvariantCulture)} plan file(s) of '{scope}' differ in the working tree from commit {GitIds.Short(tip)}: {shown}{more} — the reviewers read the commit, not your edits; commit them (or put them back) and call again";
    }
}
