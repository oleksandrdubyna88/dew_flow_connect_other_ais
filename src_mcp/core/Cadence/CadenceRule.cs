namespace CoaiMcp.Core.Cadence;

/// <summary>What the operator switched on: nothing, orders, or orders the gate enforces.</summary>
/// <remarks>
/// <c>Remind</c> is the shipped default (D4 of <c>research/PLAN_consult_on_a_cadence.md</c>): the extension is
/// on the Marketplace, and a refusal by default would change every user's gate on an update.
/// </remarks>
public enum CadenceMode
{
    /// <summary>No cadence orders and no refusal.</summary>
    Off,

    /// <summary>The orders, and nothing refused.</summary>
    Remind,

    /// <summary>The orders, and the code round of an unconsulted group refused.</summary>
    Require,
}

/// <summary>A run of consecutive epics that owes ONE consultation — three unless the operator changed it.</summary>
/// <param name="First">The group's first epic number.</param>
/// <param name="Last">Its last, cut at the plan's last epic.</param>
public readonly record struct EpicGroup(int First, int Last)
{
    /// <summary>How <c>consult</c>'s <c>epics</c> argument names it: <c>4-6</c>, or <c>7</c> for a group of one.</summary>
    public string Range => First == Last ? $"{First}" : $"{First}-{Last}";

    /// <summary>Whether an epic falls in this group.</summary>
    public bool Holds(int epic) => epic >= First && epic <= Last;
}

/// <summary>
/// The arithmetic of the cadence, as the operator gave it on 2026-09-25.
/// </summary>
/// <remarks>
/// One consultation per group of <c>every</c> epics, before the group is built; the risk question from
/// <c>threshold</c> epics; nothing past fourteen epics in one plan. Groups are counted from the plan's OWN
/// first epic, so a plan that continues another at epic 5 is grouped 5-7, 8-10… and never asks the
/// consultant about an epic that lives in a different file (epic 1's plan round, codex).
/// </remarks>
public static class CadenceRule
{
    /// <summary>The operator's number: one consultation per three epics.</summary>
    public const int DefaultEvery = 3;

    /// <summary>The operator's number: from five epics, the caller is asked what carries the most risk.</summary>
    public const int DefaultRiskThreshold = 5;

    /// <summary>How many risky items one plan may name — D6, so the question keeps its meaning.</summary>
    public const int DefaultRiskMax = 3;

    /// <summary>The most epics one plan may hold; past this it is two plans.</summary>
    public const int MostEpics = 14;

    /// <summary>The group an epic falls in, counted from the plan's first epic.</summary>
    public static EpicGroup GroupOf(int epic, int every, int firstEpic = 1)
    {
        var size = Math.Max(1, every);
        // Floor, not C#'s truncation toward zero: an epic before the plan's first would otherwise get a
        // group that does not hold it (epic 1's code round, gemini).
        var index = (int)Math.Floor((epic - firstEpic) / (double)size);
        var start = firstEpic + (index * size);

        return new EpicGroup(start, start + size - 1);
    }

    /// <summary>The groups a plan owes a consultation for — one per group its own epics fall in, cut at its last.</summary>
    public static IReadOnlyList<EpicGroup> GroupsOwed(IEnumerable<int> epicNumbers, int every)
    {
        var numbers = epicNumbers.Distinct().Order().ToList();
        if (numbers.Count == 0)
        {
            return [];
        }
        var first = numbers[0];
        var last = numbers[^1];

        return [.. numbers
            .Select(epic => GroupOf(epic, every, first))
            .Distinct()
            .Select(group => group with { Last = Math.Min(group.Last, last) })];
    }

    /// <summary>Whether a plan this size is asked which of its epics and stories carry the most risk.</summary>
    public static bool AsksForRisk(int epicCount, int threshold) => epicCount >= Math.Max(1, threshold);

    /// <summary>
    /// The refusal for a plan of more than fourteen epics, or empty. A COUNT of the plan's epics — never its
    /// last number, which a continuing plan starts past one.
    /// </summary>
    public static string RefuseIfTooMany(int epicCount, string plan) =>
        epicCount <= MostEpics
            ? string.Empty
            : $"{plan} holds {epicCount} epics, and one plan is gated a group at a time up to {MostEpics}. Split it "
              + "into two plans — by phase or by area — each with its own epics, and call again with the plan and "
              + "epic you are building. Nothing was reviewed.";
}
