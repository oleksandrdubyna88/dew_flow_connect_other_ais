using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Commands;
using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Server;

/// <summary>What the cadence gate says about one epic's code round.</summary>
public abstract record CadenceCheck
{
    /// <summary>Nothing is owed: the epic's group and its risky items each have a closed consultation.</summary>
    public sealed record Satisfied : CadenceCheck;

    /// <summary>A consultation is owed and can be had; the sentence says which, and carries the call.</summary>
    public sealed record Blocked(string Sentence) : CadenceCheck;

    /// <summary>A consultation is owed and CANNOT be had; the round proceeds and this reason is recorded.</summary>
    public sealed record StoodDown(string Reason) : CadenceCheck;

    private CadenceCheck() { }
}

/// <summary>
/// Whether an epic's group, and each risky item in it, has had its consultation
/// (<c>todo/PLAN_consult_on_a_cadence.md</c>, story 2.5).
/// </summary>
/// <remarks>
/// <para><b>The evidence is a consultation record, and nothing else.</b> One counts when it is OVER and closed
/// with a VERDICT (<see cref="ConsultationOutcomes.IsVerdict"/>) for the same repository, plan key, kind and
/// epics. Lapsed and empty are not verdicts — the clock closing a consultation says nothing about whether the
/// advice held — so a lapsed one may still be given its outcome with <c>close_consult</c> and count then.
/// Satisfaction is never copied into <see cref="CadenceStore"/>: a second copy is a copy that can disagree.</para>
/// <para><b>A record the store cannot read counts as no consultation</b> (epic 2's plan round, codex): the
/// group is simply owed, the caller is asked to consult, and a consultation is still possible — so this
/// fails closed without a deadlock.</para>
/// <para><b>Never a deadlock (decision 12).</b> When one is owed and <see cref="ConsultationService.Preflight()"/>
/// says none can be had, the check stands down with the tool's own sentence; the caller of this records it
/// and raises the notice. The preflight is asked only when something is owed.</para>
/// </remarks>
public sealed class CadenceGate(ConsultationStore consultations, Func<ConsultPreflight> preflight)
{
    /// <summary>The groups of a plan a closed consultation covers.</summary>
    public IReadOnlyList<EpicGroup> SatisfiedGroups(string repoId, string plan) => Groups(Evidence(repoId, plan));

    /// <summary>The risk-item keys of a plan a closed consultation covers.</summary>
    public IReadOnlyList<string> SatisfiedRisk(string repoId, string plan) => RiskKeys(Evidence(repoId, plan));

    /// <summary>The facts with what the consultations on record already cover — one read of the store.</summary>
    public CadenceFacts WithEvidence(string repoId, CadenceFacts facts)
    {
        // ONE read of the store for both questions (epic 2's code round, codex and gemini).
        var evidence = Evidence(repoId, facts.Plan);

        return facts with { Satisfied = Groups(evidence), SatisfiedRisk = RiskKeys(evidence) };
    }

    /// <summary>What is owed before the declared epic's code round, if anything, and whether it can be had.</summary>
    public CadenceCheck Check(string repoId, CadenceFacts facts, CommandTexts texts)
    {
        var known = WithEvidence(repoId, facts);
        var owed = Owed(known, texts);
        if (owed.Count == 0)
        {
            return new CadenceCheck.Satisfied();
        }

        var can = preflight();

        return can.Available
            ? new CadenceCheck.Blocked(CadenceRefusals.Owed(known, owed))
            : new CadenceCheck.StoodDown(can.Reason);
    }

    /// <summary>The orders for everything owed before this epic's code round: its group, then its risky items.</summary>
    private static IReadOnlyList<string> Owed(CadenceFacts facts, CommandTexts texts)
    {
        var orders = new List<string>();
        if (CadenceOrders.GroupDue(facts) is { } group)
        {
            orders.Add(CadenceOrders.GroupOrder(facts, group, texts));
        }
        orders.AddRange(CadenceOrders.RiskDue(facts).Select(item => CadenceOrders.RiskOrder(facts, item, texts)));

        return orders;
    }

    /// <summary>This plan's ordered consultations closed with a verdict — the only evidence there is.</summary>
    private IReadOnlyList<ConsultationRecord> Evidence(string repoId, string plan)
    {
        var key = EpicRef.PlanKey(plan);

        return [.. consultations.All()
            .Where(record => ConsultationStore.IsCadenceEvidence(record)
                && repoId.Length > 0
                && record.RepoId == repoId
                && EpicRef.PlanKey(record.Plan) == key)];
    }

    private static IReadOnlyList<EpicGroup> Groups(IReadOnlyList<ConsultationRecord> evidence) =>
        [.. evidence.Where(record => record.Kind == ConsultKinds.Cadence).Select(record => GroupOf(record.Epics)).OfType<EpicGroup>()];

    private static IReadOnlyList<string> RiskKeys(IReadOnlyList<ConsultationRecord> evidence) =>
        [.. evidence.Where(record => record.Kind == ConsultKinds.Risk).Select(record => record.Epics)];

    /// <summary>A recorded range read back into its group; a range this build cannot read covers nothing.</summary>
    private static EpicGroup? GroupOf(string range)
    {
        var (aim, refusal) = ConsultAim.Parse(ConsultKinds.Cadence, "plan", range);

        return refusal.Length > 0 ? null : Parsed(aim.Epics);
    }

    private static EpicGroup Parsed(string range)
    {
        var dash = range.IndexOf('-', StringComparison.Ordinal);
        var first = int.Parse(dash < 0 ? range : range[..dash], System.Globalization.CultureInfo.InvariantCulture);
        var last = dash < 0 ? first : int.Parse(range[(dash + 1)..], System.Globalization.CultureInfo.InvariantCulture);

        return new EpicGroup(first, last);
    }
}

/// <summary>The cadence gate's refusals — the problem, then the cure, in the <c>RoundRefusals</c> style.</summary>
public static class CadenceRefusals
{
    /// <summary>What is owed before this epic's code round, with every call it takes.</summary>
    /// <remarks>
    /// The calls are the ORDERS' own text (<see cref="CadenceOrders.GroupOrder"/>,
    /// <see cref="CadenceOrders.RiskOrder"/>) — one copy of each call, so a person who rewords an order has
    /// reworded the refusal with it.
    /// </remarks>
    public static string Owed(CadenceFacts facts, IReadOnlyList<string> orders) =>
        $"epic {facts.Epic} of {facts.Plan} cannot go through its code round yet: the operator's cadence owes a "
        + $"consultation first. {string.Join(" ", orders)} Nothing was reviewed.";

    /// <summary>A cadence record that cannot be read: refused rather than read as nothing recorded (D7).</summary>
    public static string Unreadable(string why) =>
        $"{why}. Nothing was reviewed — fix or remove that file (it holds only which epics passed their code gate "
        + "and the risk answer; the consultations themselves are kept elsewhere), then call again.";
}
