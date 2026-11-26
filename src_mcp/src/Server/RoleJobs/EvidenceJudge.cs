using System.Text.Json;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>An evidence item with what coai found when it compared it with the file: <see cref="Verified"/> is true only when the line holds the text.</summary>
internal sealed record CheckedEvidence(EvidenceItem Item, bool Verified, string Why);

/// <summary>
/// One answer's evidence checks, in the order the items are met (epoch 3 E3 s1 t1, s2): an item that is an ERROR of the answer (a blank text, a path that may not be opened) goes to <see cref="Errors"/>, an item that was compared
/// and is not true goes to <see cref="Unverified"/>, and every item gives back what the envelope shows. The files are asked about only through <see cref="RoleEvidence"/>, so the one road holds.
/// </summary>
internal sealed class EvidenceRun(IEvidenceSource source)
{
    /// <summary>Problems that are the answer's fault: it must be sent again.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Items that were compared with the file and are not true: they go back once, and stay in the envelope with <c>verified: false</c> if the turns run out.</summary>
    public List<string> Unverified { get; } = [];

    public CheckedEvidence Item(EvidenceItem item, string at) => Taken(RoleEvidence.Check(item, source, at), item, at);

    /// <summary>A file a scout named: <c>Verified</c> of the result is whether it exists.</summary>
    public (string Path, bool Exists) File(string path, string at) => (path, Taken(RoleEvidence.CheckFile(path, source, at), new EvidenceItem(path, 0, string.Empty), at, listed: false).Verified);

    private CheckedEvidence Taken(EvidenceVerdict verdict, EvidenceItem item, string at, bool listed = true)
    {
        if (verdict is EvidenceVerdict.Checked checkedItem)
        {
            if (!checkedItem.Verified && listed)
            {
                Unverified.Add($"{at}: not verified — {checkedItem.Why}. Copy the text from the line that shows your claim (and give that line's number), or take the item out");
            }

            return new CheckedEvidence(item, checkedItem.Verified, checkedItem.Why);
        }

        Errors.Add(((EvidenceVerdict.Error)verdict).Sentence);

        return new CheckedEvidence(item, false, "the item is an error of the answer");
    }
}

/// <summary>
/// The reading every read-only role (the scout, the judge) shares of one answer (epoch 3 E3 s2): ONE JSON object whose shape is right, whose evidence coai checked itself, and whose result coai builds. A shape error and an unverified item
/// are something the model can fix, so each is a <see cref="RoleAnswerVerdict.Repair"/> that carries every problem back to the same session; on the LAST repair turn an unverified item is no longer sent back — it stays in the
/// envelope with <c>verified: false</c>, never shown as verified and never dropped — while a shape error still ends the attempt as <c>output-invalid</c> (the failover's own rule for a repair that has no turn left).
/// </summary>
internal abstract class EvidenceJudge(int repairTurns)
{
    /// <summary>The number of repair turns the attempt has used so far — the turn the answer being judged came from. The role sets it from its failover.</summary>
    public Func<int> Turn { get; set; } = () => 0;

    public RoleAnswerVerdict Judge(string answer) =>
        BaJudge.Parsed(answer) is { } root
            ? Read(root)
            : Wrong(["the answer is not ONE JSON object: answer with the JSON object alone, with no text before or after it"]);

    protected abstract RoleAnswerVerdict Read(JsonElement root);

    /// <summary>The verdict of an answer whose shape is right and whose evidence was checked: <paramref name="envelope"/> is built only when the answer is to be accepted.</summary>
    protected RoleAnswerVerdict Settled(EvidenceRun run, Func<int, string> envelope)
    {
        if (run.Errors.Count > 0)
        {
            return Wrong([.. run.Errors, .. run.Unverified]);
        }

        return run.Unverified.Count > 0 && Turn() < repairTurns ? Wrong(run.Unverified) : new RoleAnswerVerdict.Valid(envelope(Turn()));
    }

    protected static RoleAnswerVerdict Wrong(IReadOnlyList<string> errors) =>
        new RoleAnswerVerdict.Repair(BaPrompt.Repair(errors), $"the answer has {errors.Count} problem{(errors.Count == 1 ? string.Empty : "s")}; the first: {errors[0]}");
}
