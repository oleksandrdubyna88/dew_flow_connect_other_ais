using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Collecting;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The half of the plan binding of a coder job that belongs to a FOLDER plan round (epoch 3 E3 s3 t4). What a folder round approved is the pack the reviewers read, so the binding rebuilds the pack at the job's base and compares its approval id with the
/// approved one: a task must be inside the scope, and nothing the reviewers read — a scope file, <c>feature.yaml</c>, the plan file, an ancestor's file, a <c>spec</c> file, the id or the title of an outside dependency — may differ. A file the pack
/// does not hold does not take part, so a new <c>RESULTS_*.md</c> or a node nobody in the scope depends on is no change.
/// </summary>
/// <remarks>
/// <para><b>The feature folder is found, not stored.</b> The source records the scope folder and the approval id; the folder that holds <c>feature.yaml</c> is the nearest one at or above the scope at the commit of the round. The id computed there at
/// the round's commit must be the approved one, or the binding is refused as not reproducible — so a source that was altered, or a layout that moved, is never read as approved.</para>
/// <para>Every git read goes through <see cref="GitHistory"/> and its launcher, so a git that does not answer in time is a refusal that says so, never a pass.</para>
/// </remarks>
internal static class FolderPlanBinding
{
    private const string NewRound = "run a new plan round (review_plan with featureFolder) on the changed plan, then start the coder job";

    /// <summary>Whether a task file is inside the scope folder of a folder source.</summary>
    internal static bool Holds(PlanSource plan, string taskFile) => taskFile.StartsWith(plan.Path + "/", StringComparison.Ordinal);

    internal static string Outside(PlanSource plan, string taskFile) =>
        $"task {taskFile} is outside the approved scope {plan.Path}: the plan round approved that folder only — {NewRound.Replace("on the changed plan", "whose workItem names a node that holds the task", StringComparison.Ordinal)}";

    /// <summary>The refusal for what the base holds, or empty: the task is a file of the base, and the approval id computed at the base is the approved one.</summary>
    internal static async Task<string> AtBaseAsync(GitHistory git, string repoPath, RoleJobRecord job, PlanSource plan, CancellationToken ct)
    {
        var (feature, found) = await FeatureFolderAsync(git, repoPath, plan, ct);
        if (found.Length > 0)
        {
            return found;
        }

        // The id at the commit of the ROUND first (the re-review of 2026-11-26, F1): when this build does not reproduce the approved id there, the pack format changed since the approval — or the record is not one a round wrote — and no job is bound to it. The sentence names no path.
        var approved = await PackAsync(git, repoPath, plan.Commit, feature, plan.Path, ct);
        if (approved.Pack is null)
        {
            return $"the approved plan folder {feature} cannot be packed at commit {GitIds.Short(plan.Commit)}: {approved.Refusal}";
        }

        if (approved.Pack.ApprovalId != plan.Blob)
        {
            return FormatChanged(plan);
        }

        var atBase = await PackAsync(git, repoPath, job.BaseSha, feature, plan.Path, ct);
        if (atBase.Pack is null)
        {
            return $"the plan folder {feature} cannot be packed at the job's base commit {GitIds.Short(job.BaseSha)}: {atBase.Refusal}";
        }

        var changed = atBase.Pack.ApprovalId == plan.Blob ? string.Empty : await ChangedAsync(git, repoPath, plan, feature, atBase.Pack, job.BaseSha, ct);
        if (changed.Length > 0)
        {
            return changed;
        }

        var task = await git.HasPathAsync(repoPath, job.BaseSha, job.TaskFile, ct);

        return task.Ran && task.Ok ? string.Empty : $"task {job.TaskFile} is not a file of the job's base commit {GitIds.Short(job.BaseSha)}, so it does not belong to the approved plan revision";
    }

    /// <summary>What the plan round read, rebuilt at the commit of the round — the text the code rounds are judged against — or the refusal.</summary>
    internal static async Task<(string Text, string Refusal)> ApprovedTextAsync(GitHistory git, string repoPath, PlanSource plan, CancellationToken ct)
    {
        var (feature, found) = await FeatureFolderAsync(git, repoPath, plan, ct);
        if (found.Length > 0)
        {
            return (string.Empty, found);
        }

        var approved = await PackAsync(git, repoPath, plan.Commit, feature, plan.Path, ct);

        return approved.Pack is null ? (string.Empty, $"the approved plan folder {feature} cannot be packed at commit {GitIds.Short(plan.Commit)}: {approved.Refusal}")
            : Reproduced(approved.Pack, plan, feature) is { Length: > 0 } unproven ? (string.Empty, unproven)
            : (approved.Pack.Text, string.Empty);
    }

    /// <summary>The approval id was found changed at the base: rebuild what was approved, prove it is what was approved, and name the first difference.</summary>
    private static async Task<string> ChangedAsync(GitHistory git, string repoPath, PlanSource plan, string feature, Pack atBase, string baseSha, CancellationToken ct)
    {
        var approved = await PackAsync(git, repoPath, plan.Commit, feature, plan.Path, ct);
        if (approved.Pack is null)
        {
            return $"the approved plan folder {feature} cannot be packed at commit {GitIds.Short(plan.Commit)}, so what changed cannot be named: {approved.Refusal}";
        }

        if (Reproduced(approved.Pack, plan, feature) is { Length: > 0 } unproven)
        {
            return unproven;
        }

        var first = ApprovalDiff.First(approved.Pack.Approval, atBase.Approval);

        return $"what the reviewers read of {plan.Path} changed after the plan round approved it at commit {GitIds.Short(plan.Commit)}: {(first.Length > 0 ? first : "the approval ids differ")} (job base {GitIds.Short(baseSha)}); the approval covers every file in the pack and every outside dependency — {NewRound}";
    }

    /// <summary>The sentence for an approval this build does not reproduce at the commit of its round: the pack format changed since it was made. No path is named.</summary>
    private static string FormatChanged(PlanSource plan) =>
        $"the plan round that approved this scope was made under another pack format (this build packs under format {WorkPack.FormatVersion}), so what it approved cannot be told from what is packed now and no coder job is bound to it — {NewRound}";

    /// <summary>The refusal when the pack rebuilt at the round's commit is not the approved one (the id, the text), or empty.</summary>
    private static string Reproduced(Pack approved, PlanSource plan, string feature) =>
        approved.ApprovalId != plan.Blob
            ? FormatChanged(plan)
            : DocumentRules.ArtifactIdOf(approved.Text) != plan.TextId
                ? $"the pack of {plan.Path} at commit {GitIds.Short(plan.Commit)} is not the text the reviewers read (text id {DocumentRules.ArtifactIdOf(approved.Text)}, the round recorded {plan.TextId})"
                : string.Empty;

    /// <summary>The nearest folder at or above the scope that holds <c>feature.yaml</c> at the round's commit.</summary>
    private static async Task<(string Feature, string Refusal)> FeatureFolderAsync(GitHistory git, string repoPath, PlanSource plan, CancellationToken ct)
    {
        var folder = plan.Path;
        while (true)
        {
            var has = await git.HasPathAsync(repoPath, plan.Commit, $"{folder}/feature.yaml", ct);
            if (!has.Ran)
            {
                return (string.Empty, "git did not answer in time while the approved plan folder was looked for; try again in a moment");
            }

            if (has.Ok)
            {
                return (folder, string.Empty);
            }

            var cut = folder.LastIndexOf('/');
            if (cut < 0)
            {
                return (string.Empty, $"no feature.yaml at or above the approved scope {plan.Path} at commit {GitIds.Short(plan.Commit)}, so the plan folder cannot be found");
            }

            folder = folder[..cut];
        }
    }

    private sealed record Packing(Pack? Pack, string Refusal);

    private static async Task<Packing> PackAsync(GitHistory git, string repoPath, string sha, string feature, string scope, CancellationToken ct)
    {
        var opened = await GitWorkFolder.OpenAsync(git, repoPath, sha, feature, ct);
        if (opened is not FolderOpen.Opened open)
        {
            return new Packing(null, ((FolderOpen.Refused)opened).Sentence);
        }

        var reader = new StrictYamlReader();
        var missed = await open.Folder.PreloadAsync(WorkPack.NamedFiles(open.Folder, reader), ct);
        if (missed.Length > 0)
        {
            return new Packing(null, missed);
        }

        return WorkPack.Build(open.Folder, reader, feature, scope) switch
        {
            PackOutcome.Packed packed => new Packing(packed.Pack, string.Empty),
            PackOutcome.Refused refused => new Packing(null, refused.Sentence),
            _ => throw new InvalidOperationException("a pack outcome is Packed or Refused"),
        };
    }
}
