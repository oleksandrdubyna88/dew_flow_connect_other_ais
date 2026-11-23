using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Work;
using CoaiMcp.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The BA role's work (epoch 3 E1 s4, spec C): it reads the epic, asks the routed models for a plan — ONE JSON object, no path — checks the plan over an overlay (disk plus the files the plan would make) with the rules of
/// <c>validate_work</c> and the device's limits, sends the errors back to the SAME session of the SAME model for at most two repair turns, and only then writes the story and task files, atomically, with a manifest.
/// </summary>
/// <remarks>
/// <para><b>Read-only until the end.</b> The model has Read, Glob and Grep (the consultant tool set); the tree is compared around every attempt (<see cref="RoleFailover"/>); coai writes nothing until a plan is valid, and then only
/// <c>&lt;epic&gt;/stories/**/story.yaml</c> and <c>task.yaml</c> and <c>ba-manifest.json</c> — never a path the model gave (it gave ids).</para>
/// <para><b>Ends:</b> <c>succeeded</c> with the manifest in <c>result.json</c>; <c>failed: output-invalid</c> when the plan is still wrong after the second repair turn (zero files written, every turn's answer kept under
/// <c>attempts/&lt;n&gt;/</c>); <c>failed: refusal</c> when every model declared a refusal; <c>failed: ba-epic</c> when the epic is not fit to plan (it has a story, it is not an epic file); the failover's own ends.</para>
/// </remarks>
internal sealed class BaWork : IRoleWork
{
    /// <summary>How many repair turns one attempt may use (spec C: at most 2).</summary>
    public const int RepairTurns = 2;

    /// <summary>The most one model may take for the whole of its turns; a test shortens it.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(20);

    /// <summary>The waits of the last model's ladder; a test shortens them.</summary>
    public IReadOnlyList<TimeSpan> Ladder { get; init; } = Runners.Reviewers.RetryLadder.Default;

    /// <summary>The hook of the publisher's write boundaries — for the tests; null in the product.</summary>
    public Action<string>? Boundary { get; init; }

    public async Task<RoleWorkOutcome> RunAsync(IRoleRun run, CancellationToken ct)
    {
        run.Phase("ba-prepare", 0);
        var (site, refusal) = BaEpic.Locate(run.Request.RepoPath, run.Request.TaskFile);
        if (site is null)
        {
            return new RoleWorkOutcome.Failed("ba-epic", refusal);
        }

        var store = new RoleJobStore(run.DataDir, run.Clock, run.Log);
        BaStaging.Sweep(site.EpicFolder, id => id == run.Request.JobId || store.Read(id) is { Terminal: false });
        var roles = RolesAnswer.Compute(run.Settings, string.Empty, string.Empty, claudeHook: false);
        var judge = new BaJudge(site, roles.WorkMethod.ToLimits());
        var failover = new RoleFailover(run, judge.Judge) { MaxRepairTurns = RepairTurns, AttemptTimeout = AttemptTimeout, Ladder = Ladder };
        var outcome = await failover.RunAsync(BaPrompt.Build(BaEpic.Inputs(site), roles.WorkMethod, RepairTurns), ct);

        return outcome is RoleWorkOutcome.Succeeded && judge.Accepted is { } accepted ? Publish(run, site, accepted, failover.RepairTurnsUsed) : outcome;
    }

    private RoleWorkOutcome Publish(IRoleRun run, BaEpicSite site, BaAccepted accepted, int repairTurns)
    {
        run.Phase("ba-publish", 0);
        var published = new BaPublisher(site, run.Request.JobId, run.StillWriter, Boundary, text => File.WriteAllText(Path.Combine(run.Folder, BaPublisher.ManifestName), text)).Publish(accepted.Files);

        return published switch
        {
            BaPublished.Done done => new RoleWorkOutcome.Succeeded(ResultOf(site, accepted, done, repairTurns)),
            BaPublished.Stale => new RoleWorkOutcome.Stopped(),
            BaPublished.Refused refused => new RoleWorkOutcome.Failed("ba-publish", refused.Reason),
            _ => new RoleWorkOutcome.Failed("ba-publish", "the publisher gave no answer"),
        };
    }

    /// <summary>The job's result: the epic, what was written (the manifest), how many repair turns it took, and the BA's own notes.</summary>
    private static string ResultOf(BaEpicSite site, BaAccepted accepted, BaPublished.Done done, int repairTurns)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("status", "ok");
            json.WriteString("epic", site.EpicId);
            json.WriteNumber("stories", accepted.Plan.Stories.Count);
            json.WriteNumber("tasks", accepted.Plan.Stories.Sum(story => story.Tasks.Count));
            json.WriteNumber("repairTurns", repairTurns);
            json.WriteString("manifest", $"{site.EpicRel}/stories/{BaPublisher.ManifestName}");
            json.WriteStartArray("files");
            foreach (var entry in done.Manifest)
            {
                json.WriteStartObject();
                json.WriteString("path", entry.Path);
                json.WriteString("sha256", entry.Sha256);
                json.WriteString("kind", entry.Kind);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteStartArray("notes");
            foreach (var note in accepted.Plan.Notes)
            {
                json.WriteStringValue(note);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>A plan that passed the shape and the rules, and the files it makes.</summary>
internal sealed record BaAccepted(BaPlan Plan, IReadOnlyList<BaFile> Files);

/// <summary>
/// The BA's reading of one answer: refused, or ONE JSON object whose shape and plan are right — and anything else is something the model can fix, so it is a <see cref="RoleAnswerVerdict.Repair"/> that carries every error back
/// to the same session. The last plan it accepted is <see cref="Accepted"/>.
/// </summary>
internal sealed class BaJudge(BaEpicSite site, WorkLimits limits)
{
    public BaAccepted? Accepted { get; private set; }

    public RoleAnswerVerdict Judge(string answer)
    {
        Accepted = null;
        if (Parsed(answer) is not { } root)
        {
            return Wrong(["the answer is not ONE JSON object: answer with the JSON object alone, with no text before or after it"]);
        }

        switch (BaOutputReader.Read(root))
        {
            case BaRead.Refused refused:
                return new RoleAnswerVerdict.Refused(refused.Reason);
            case BaRead.Wrong wrong:
                return Wrong(wrong.Problems);
            case BaRead.Plan plan:
                return Checked(plan.Value, root);
            default:
                return Wrong(["the answer could not be read"]);
        }
    }

    private RoleAnswerVerdict Checked(BaPlan plan, JsonElement root)
    {
        var checkedPlan = BaPlanCheck.Check(site, plan, limits);
        if (checkedPlan.Errors.Count > 0)
        {
            return Wrong(checkedPlan.Errors);
        }

        Accepted = new BaAccepted(plan, checkedPlan.Files);

        return new RoleAnswerVerdict.Valid(root.GetRawText());
    }

    private static RoleAnswerVerdict Wrong(IReadOnlyList<string> errors) =>
        new RoleAnswerVerdict.Repair(BaPrompt.Repair(errors), $"the plan has {errors.Count} error{(errors.Count == 1 ? string.Empty : "s")}; the first: {errors[0]}");

    /// <summary>The answer as one JSON object — a single surrounding code fence is taken off, since a model puts one there however it is told not to; anything else that is not JSON is null.</summary>
    internal static JsonElement? Parsed(string answer)
    {
        try
        {
            using var document = JsonDocument.Parse(Unfenced(answer.Trim()));

            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Unfenced(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal) || text.Length < 6)
        {
            return text;
        }

        var inner = text[3..^3];
        var firstLine = inner.IndexOf('\n', StringComparison.Ordinal);

        return firstLine >= 0 && inner[..firstLine].Trim() is "" or "json" ? inner[(firstLine + 1)..] : text;
    }
}
