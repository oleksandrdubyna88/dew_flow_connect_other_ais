using System.Text.Json;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The scout role's work (epoch 3 E3 s2 t1; spec A): it asks the routed models — the <c>scout</c> list of the roles table, or the <c>important</c> list for a task that is important — a question (<c>brief</c>, and the file it is about when
/// <c>taskFile</c> is given) and reads ONE JSON answer: facts, each with evidence <c>{file, line, text}</c> that coai checks itself, the files a reader needs, and what the scout did not find. The common road is <see cref="ReadOnlyRoleWork"/>.
/// </summary>
/// <remarks>
/// <para><b>Ends:</b> <c>succeeded</c> with the envelope in <c>result.json</c> (<see cref="ReadOnlyEnvelope.Scout"/>); <c>failed: output-invalid</c> when the shape is still wrong after the second repair turn (no result, every turn's answer
/// kept under <c>attempts/&lt;n&gt;/</c>); <c>failed: models-exhausted</c> when every model declared a refusal or stopped; <c>failed: tree-changed</c> when the repository changed during an attempt; the failover's own ends.</para>
/// </remarks>
internal sealed class ScoutWork : ReadOnlyRoleWork
{
    protected override string Name => "scout";

    protected override string FirstPrompt(IRoleRun run) => ScoutPrompt.Build(run.Request, new RolePrompts(run.DataDir), RepairTurns);

    protected override EvidenceJudge JudgeOf(IEvidenceSource files, string commit) => new ScoutJudge(files, RepairTurns, commit);
}

/// <summary>The scout's reading of one answer: refused, or ONE JSON object whose shape is right and whose evidence coai checked — the envelope when it is to be accepted.</summary>
internal sealed class ScoutJudge(IEvidenceSource files, int repairTurns, string commit) : EvidenceJudge(repairTurns)
{
    protected override RoleAnswerVerdict Read(JsonElement root) => ScoutOutputReader.Read(root) switch
    {
        ScoutRead.Refused refused => new RoleAnswerVerdict.Refused(refused.Reason),
        ScoutRead.Wrong wrong => Wrong(wrong.Problems),
        ScoutRead.Answer answer => Checked(answer.Value),
        _ => Wrong(["the answer could not be read"]),
    };

    private RoleAnswerVerdict Checked(ScoutAnswer answer)
    {
        var run = new EvidenceRun(files);
        IReadOnlyList<IReadOnlyList<CheckedEvidence>> evidence =
            [.. answer.Facts.Select((fact, f) => (IReadOnlyList<CheckedEvidence>)[.. fact.Evidence.Select((item, e) => run.Item(item, $"facts[{f}].evidence[{e}]"))])];
        var found = answer.Files.Select((path, i) => run.File(path, $"files[{i}]")).ToList();

        return Settled(run, turns => ReadOnlyEnvelope.Scout(answer, evidence, found, turns, commit));
    }
}
