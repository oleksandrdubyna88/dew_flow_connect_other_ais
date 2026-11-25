using System.Text.Json;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The judge role's work (epoch 3 E3 s2 t2; spec A): the same road as the scout (<see cref="ReadOnlyRoleWork"/>), on the <c>important</c> list — a model the caller names that is not on it is refused before any folder
/// (<see cref="JudgeRoute"/>). It reads ONE JSON answer: a verdict, a summary, findings with the gate's four severity words and evidence coai checks itself, and a decision when the brief asked to choose.
/// The three rules of the judge (an <c>approve</c> with a <c>blocking</c> finding, a <c>revise</c> or <c>reject</c> with no finding, a finding id used twice) are errors that go back as a repair turn.
/// </summary>
/// <remarks><b>The judge writes no file.</b> A design decision is <c>decision</c>; a review is <c>findings</c>. The tree is compared around every attempt, and a change ends the job as <c>tree-changed</c>.</remarks>
internal sealed class JudgeWork : ReadOnlyRoleWork
{
    protected override string Name => "judge";

    protected override string FirstPrompt(IRoleRun run) => JudgePrompt.Build(run.Request, new RolePrompts(run.DataDir), RepairTurns);

    protected override EvidenceJudge JudgeOf(IEvidenceSource files, string commit) => new JudgeJudge(files, RepairTurns, commit);
}

/// <summary>The judge's reading of one answer: refused, or ONE JSON object whose shape and rules are right and whose evidence coai checked — the envelope when it is to be accepted.</summary>
internal sealed class JudgeJudge(IEvidenceSource files, int repairTurns, string commit) : EvidenceJudge(repairTurns)
{
    protected override RoleAnswerVerdict Read(JsonElement root) => JudgeOutputReader.Read(root) switch
    {
        JudgeRead.Refused refused => new RoleAnswerVerdict.Refused(refused.Reason),
        JudgeRead.Wrong wrong => Wrong(wrong.Problems),
        JudgeRead.Answer answer => Checked(answer.Value),
        _ => Wrong(["the answer could not be read"]),
    };

    private RoleAnswerVerdict Checked(JudgeAnswer answer)
    {
        var run = new EvidenceRun(files);
        IReadOnlyList<IReadOnlyList<CheckedEvidence>> evidence =
            [.. answer.Findings.Select((finding, f) => (IReadOnlyList<CheckedEvidence>)[.. finding.Evidence.Select((item, e) => run.Item(item, $"findings[{f}].evidence[{e}]"))])];

        return Settled(run, turns => ReadOnlyEnvelope.Judge(answer, evidence, turns, commit));
    }
}
