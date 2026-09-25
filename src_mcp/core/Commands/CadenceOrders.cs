using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using CoaiMcp.Core.Cadence;

namespace CoaiMcp.Core.Commands;

/// <summary>
/// What the gate knows about a plan's cadence at the moment of this call — gathered by the server, read here.
/// </summary>
/// <remarks>
/// <see cref="Off"/> by default, so a context built without it says exactly what every release before the
/// cadence said; <c>TheOrdersAreWhatTheyWereTests</c> holds that byte for byte.
/// </remarks>
public sealed record CadenceFacts
{
    /// <summary>No cadence: no orders, whatever else is set.</summary>
    public static CadenceFacts Off { get; } = new();

    public CadenceMode Mode { get; init; } = CadenceMode.Off;

    /// <summary>One consultation per this many epics.</summary>
    public int Every { get; init; } = CadenceRule.DefaultEvery;

    /// <summary>From this many epics the caller is asked what carries the most risk.</summary>
    public int RiskThreshold { get; init; } = CadenceRule.DefaultRiskThreshold;

    /// <summary>The most risky items one plan may name.</summary>
    public int RiskMax { get; init; } = CadenceRule.DefaultRiskMax;

    /// <summary>The checkout the consultant should read — what the order's call carries as <c>repoPath</c>.</summary>
    public string RepoPath { get; init; } = string.Empty;

    /// <summary>The plan the caller declared, repo-relative; empty when it declared none.</summary>
    public string Plan { get; init; } = string.Empty;

    /// <summary>The epic the caller declared; 0 when it declared none.</summary>
    public int Epic { get; init; }

    /// <summary>The plan's last epic number as the caller declared it — the count when the plan names no epics.</summary>
    public int LastEpic { get; init; }

    /// <summary>The plan's own headings, read at the revision under review; empty when it has none.</summary>
    public PlanOutline Outline { get; init; } = PlanOutline.Empty;

    /// <summary>The groups a consultation closed with an outcome already covers.</summary>
    public IReadOnlyList<EpicGroup> Satisfied { get; init; } = [];

    /// <summary>Whether the risk question has been answered for this plan.</summary>
    public bool RiskAnswered { get; init; }

    /// <summary>The risky epics and stories the caller named.</summary>
    public IReadOnlyList<RiskItem> RiskItems { get; init; } = [];

    /// <summary>The keys (<see cref="RiskItem.Key"/>) of the items a closed consultation already covers.</summary>
    public IReadOnlyList<string> SatisfiedRisk { get; init; } = [];

    /// <summary>How many epics the plan holds: its headings when it has them, else what the caller declared.</summary>
    public int EpicCount => Outline.HasEpics ? Outline.Epics.Count : LastEpic;
}

/// <summary>
/// The orders that make a caller consult on a cadence (<c>todo/PLAN_consult_on_a_cadence.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Why orders at all.</b> Measured on 2026-09-25: ~1 260 gate rounds against 18 consultations, none
/// after 2026-09-22, zero across fourteen epics of one product. Every trigger shipped before was reactive,
/// the rule was pasted nowhere, and prose in a mounted convention produced the same zero. The gate's reply
/// is the one channel that reaches every caller.</para>
/// <para><b>Why the whole call.</b> Claude Code defers MCP tool schemas: until something loads one, the model
/// knows <c>mcp__coai__consult</c> by name only. So every order that asks for a consultation carries the line
/// that loads it and the call itself, JSON-escaped, with only the caller's own doubt left to write.</para>
/// <para>Each order opens with a marker kept in code, before the text a person may reword (the #467 rule),
/// so no override can make a working switch unrecognisable. None of them says "critical" — the canonical
/// invented severity, which <c>ReviewParser</c> rejects by name (D2).</para>
/// </remarks>
public static class CadenceOrders
{
    /// <summary>The words the forecast opens with.</summary>
    public const string ForecastMarker = "CONSULT ON A CADENCE.";

    /// <summary>The words the order for a group's consultation opens with.</summary>
    public const string GroupMarker = "CONSULT BEFORE YOU BUILD this group of epics.";

    /// <summary>The words the risk question opens with.</summary>
    public const string RiskQuestionMarker = "NAME THE RISKY EPICS AND STORIES.";

    /// <summary>The words the order for a risky item's consultation opens with.</summary>
    public const string RiskItemMarker = "CONSULT BEFORE YOU BUILD this risky piece.";

    private const string LoadLine =
        "If your client defers tool schemas, load them first (Claude Code: ToolSearch \"select:mcp__coai__consult,mcp__coai__close_consult\").";

    /// <summary>
    /// The cadence orders for this call, in the order they are carried out; empty when the cadence is off.
    /// </summary>
    /// <param name="forecast">Whether this is the round that says what the plan will owe — the server's first
    /// plan round of a plan with epics, decided by <see cref="GateCommands"/> because it needs the split.</param>
    /// <param name="facts">What the server knows about the plan.</param>
    /// <param name="texts">The words, shipped or a person's.</param>
    /// <param name="forecastOutline">The epics the plan under review names, for the forecast's count.</param>
    public static IReadOnlyList<string> For(bool forecast, CadenceFacts facts, CommandTexts texts, PlanOutline forecastOutline)
    {
        if (facts.Mode == CadenceMode.Off)
        {
            return [];
        }
        var orders = new List<string>();
        if (forecast)
        {
            orders.Add(Forecast(facts, texts, forecastOutline));
        }
        orders.AddRange(Due(facts, texts));

        return orders;
    }

    private static IEnumerable<string> Due(CadenceFacts facts, CommandTexts texts)
    {
        if (facts.Epic > 0 && GroupDue(facts) is { } group)
        {
            yield return GroupOrder(facts, group, texts);
        }
        if (facts.Plan.Length > 0 && !facts.RiskAnswered && CadenceRule.AsksForRisk(facts.EpicCount, facts.RiskThreshold))
        {
            yield return RiskQuestion(facts, texts);
        }
        foreach (var item in RiskDue(facts))
        {
            yield return RiskOrder(facts, item, texts);
        }
    }

    /// <summary>The declared epic's group, when no closed consultation covers it yet.</summary>
    public static EpicGroup? GroupDue(CadenceFacts facts)
    {
        var group = CadenceRule.GroupOf(facts.Epic, facts.Every, facts.Outline.FirstNumber);
        var cut = facts.Outline.HasEpics ? group with { Last = Math.Min(group.Last, facts.Outline.LastNumber) } : Cut(group, facts.LastEpic);

        return facts.Satisfied.Any(done => done.Holds(facts.Epic)) ? null : cut;
    }

    /// <summary>The risky items of the declared epic that no closed consultation covers yet.</summary>
    public static IEnumerable<RiskItem> RiskDue(CadenceFacts facts) =>
        facts.RiskItems.Where(item => item.Epic == facts.Epic && facts.Epic > 0 && !facts.SatisfiedRisk.Contains(item.Key));

    private static EpicGroup Cut(EpicGroup group, int last) =>
        last > 0 ? group with { Last = Math.Min(group.Last, last) } : group;

    private static string Forecast(CadenceFacts facts, CommandTexts texts, PlanOutline outline)
    {
        var groups = CadenceRule.GroupsOwed(outline.Epics.Select(e => e.Number), facts.Every);
        var owed = groups.Count > 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"this plan names {outline.Epics.Count} epics, so it owes {groups.Count} consultations: epics {string.Join(", ", groups.Select(g => g.Range))}")
            : "the number follows from how many epics you split it into";

        return $"{ForecastMarker} " + Fill(texts.Text(CommandTexts.ConsultForecast), facts)
            .Replace("{owed}", owed, StringComparison.Ordinal);
    }

    private static string GroupOrder(CadenceFacts facts, EpicGroup group, CommandTexts texts)
    {
        var titles = facts.Outline.TitlesOf(group.First, group.Last);
        var named = titles.Length > 0 ? $"{titles}" : $"epics {group.Range}";
        var problem = $"Plan {facts.Plan}, epics {group.Range} ({named}): is this group right, where is it weak, "
            + "what did it forget? <your own doubts, in one or two sentences>";

        var text = Fill(texts.Text(CommandTexts.ConsultGroup), facts)
            .Replace("{range}", group.Range, StringComparison.Ordinal)
            .Replace("{titles}", named, StringComparison.Ordinal);

        return $"{GroupMarker} " + WithCall(text, ConsultCall(facts, "cadence", group.Range, problem));
    }

    private static string RiskQuestion(CadenceFacts facts, CommandTexts texts) =>
        $"{RiskQuestionMarker} " + Fill(texts.Text(CommandTexts.ConsultRiskQuestion), facts)
            .Replace("{count}", facts.EpicCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static string RiskOrder(CadenceFacts facts, RiskItem item, CommandTexts texts)
    {
        var problem = $"Plan {facts.Plan}, {Describe(item)}, named as the risky piece because: {item.Reason}. "
            + "Is it designed right, and what would make it go wrong? <what you are least sure of>";

        var text = Fill(texts.Text(CommandTexts.ConsultRiskItem), facts)
            .Replace("{item}", Describe(item), StringComparison.Ordinal)
            .Replace("{reason}", item.Reason, StringComparison.Ordinal);

        return $"{RiskItemMarker} " + WithCall(text, ConsultCall(facts, "risk", item.Key, problem));
    }

    private static string Describe(RiskItem item) =>
        item.Story.Length > 0 ? $"story {item.Story} of epic {item.Epic}" : $"epic {item.Epic}";

    /// <summary>The placeholders every cadence text may use.</summary>
    private static string Fill(string text, CadenceFacts facts) => text
        .Replace("{load}", LoadLine, StringComparison.Ordinal)
        .Replace("{every}", facts.Every.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{threshold}", facts.RiskThreshold.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{max}", facts.RiskMax.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{most}", CadenceRule.MostEpics.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{plan}", facts.Plan.Length > 0 ? facts.Plan : "<the plan file>", StringComparison.Ordinal)
        .Replace("{enforced}", facts.Mode == CadenceMode.Require
            ? " — until then this gate refuses the code round of this epic."
            : ".", StringComparison.Ordinal);

    /// <summary>
    /// The call where the text puts <c>{call}</c> — or after the text, when a person's override left it
    /// out: the one thing a caller with a deferred schema cannot write for itself is never dropped
    /// (epic 1's code round, codex).
    /// </summary>
    private static string WithCall(string text, string call) =>
        text.Contains("{call}", StringComparison.Ordinal)
            ? text.Replace("{call}", call, StringComparison.Ordinal)
            : $"{text} Call {call}";

    /// <summary>
    /// The consult call, literally: JSON the caller can pass as it stands, non-ASCII kept readable.
    /// </summary>
    private static string ConsultCall(CadenceFacts facts, string kind, string epics, string problem) =>
        "mcp__coai__consult({"
        + $"\"repoPath\": {Json(facts.RepoPath)}, \"kind\": {Json(kind)}, \"plan\": {Json(facts.Plan)}, "
        + $"\"epics\": {Json(epics)}, \"problem\": {Json(problem)}"
        + "})";

    private static string Json(string value) =>
        "\"" + JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";
}
