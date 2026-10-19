using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What E1's twenty recorded sessions say, cell by cell, for ONE round of ONE gate session. EVERY cell is a hand-read of the fixture
/// (2026-10-16, re-read 2026-10-19 after E1's re-cut of c12, c17 and c19 reached this branch through E3): the numbers are counts of
/// messages, launches and agents and the words attribution uses — never a duration, a date or a token magnitude (instants are a rank
/// clock, E4 §2). A cell is data, so a case that changes fails by its name.
/// </summary>
/// <param name="Case">The fixture folder (<c>c01</c>).</param>
/// <param name="Gate">The index of the gate session in <c>rounds.json</c>.</param>
/// <param name="Number">The round.</param>
/// <param name="Reader">What E3's reader said of the session — the positive half, asserted before any attribution.</param>
/// <param name="CheckoutWrites">How many of the case's writes the reader resolved to a checkout on THIS host.</param>
/// <param name="State">The round's authors state, and <paramref name="Reasons"/> its reasons joined with a comma and a space.</param>
/// <param name="Rows">Each row as <c>window|who|model|effort|association|L|T|W</c>.</param>
/// <param name="Ambiguous">Agents, launches, turns — −1 each when unknown.</param>
/// <param name="Labels">One <c>window:association/rung</c> per placed message, in order.</param>
internal sealed record RecordedCell(
    string Case,
    int Gate,
    int Number,
    string Reader,
    int CheckoutWrites,
    string State,
    string Reasons,
    string[] Rows,
    (int Agents, int Launches, int Turns) Ambiguous,
    int ElsewhereTurns,
    int ClaimedTurns,
    int Unobserved,
    string[] Labels,
    int NestedCheckoutWrites = 0)
{
    private static readonly string NoEvidence = "round:ambiguous/no evidence";

    public string Key => $"{Case}#{Gate}#{Number}";

    public static IReadOnlyList<RecordedCell> All { get; } =
    [
        Observed("c01", 0, 1, (2, 1, 3), [NoEvidence, NoEvidence, NoEvidence]),
        Observed("c02", 0, 1, (1, 0, 1), [NoEvidence]),
        Observed("c03", 0, 1, (1, 0, 1), [NoEvidence]),
        Observed("c04", 0, 1, (2, 1, 2), [NoEvidence, NoEvidence]),
        Observed("c05", 0, 1, (2, 1, 3), [NoEvidence, NoEvidence, NoEvidence]) with
        {
            Reader = "partial: agent call without transcript, meta without transcript",
            State = "partial",
            Reasons = "agent call without transcript, meta without transcript",
            Unobserved = 2,
        },
        Observed("c06", 0, 1, (1, 0, 2), [NoEvidence, NoEvidence]),
        Observed("c07", 0, 1, (3, 2, 3), ["before-open:ambiguous/no evidence", NoEvidence, NoEvidence]),
        Observed("c08", 0, 1, (1, 0, 1), [NoEvidence]),
        Observed("c09", 0, 1, (1, 0, 1), [NoEvidence]),
        Observed("c09", 1, 1, (0, 0, 0), ["round:claimed by peer/no evidence"]) with { ClaimedTurns = 1 },
        new("c10", 0, 1, "unobserved: transcript not found", 0, "unobserved", "transcript not found", [], (-1, -1, -1), -1, -1, -1, []),
        Observed("c11", 0, 1, (1, 0, 1), [NoEvidence]),
        Observed("c12", 0, 1, (1, 0, 1), [NoEvidence]),
        Observed("c14", 0, 1, (0, 0, 0), ["round:same-session/session", .. Enumerable.Repeat("round:wrote here/write", 11)]) with
        {
            CheckoutWrites = 1,
            Rows = ["round|main|claude-opus-5-5|high|same-session|L0|T1|W0", "round|subagent|claude-sonnet-5-5|medium|wrote here|L1|T11|W1"],
        },
        Observed("c15", 0, 1, (0, 0, 0), []),
        Observed("c15", 1, 1, (1, 0, 1), [NoEvidence]),
        Observed("c16", 0, 1, (0, 0, 0), []),
        Observed("c16", 0, 3, (1, 0, 1), [NoEvidence]),
        Observed("c17", 0, 1, (1, 0, 3), [.. Enumerable.Repeat("round:ambiguous/two repositories", 3)]) with
        {
            CheckoutWrites = 2,
            Rows = ["round|main|unknown|unknown|wrote here|L0|T0|W1"],
        },
        Observed("c17", 1, 1, (1, 0, 0), [.. Enumerable.Repeat("round:claimed by peer/two repositories", 3)]) with
        {
            CheckoutWrites = 2,
            ClaimedTurns = 3,
            Rows = ["round|main|unknown|unknown|wrote here|L0|T0|W1"],
        },
        Observed("c18", 0, 1, (0, 0, 0), ["before-open:wrote here/write"]) with
        {
            CheckoutWrites = 1,
            Rows = ["before-open|main|claude-opus-5-5|high|wrote here|L0|T1|W1"],
        },
        Observed("c19", 0, 1, (0, 0, 0), ["round:wrote here/write"]) with
        {
            CheckoutWrites = 1,
            NestedCheckoutWrites = 1,
            Rows = ["round|main|claude-opus-5-5|high|wrote here|L0|T1|W1"],
        },
        Observed("c20", 0, 1, (1, 0, 1), [NoEvidence]),
    ];

    public static TheoryData<string> Keys => [.. All.Select(cell => cell.Key)];

    public static RecordedCell Find(string key) => All.Single(cell => cell.Key == key);

    private static RecordedCell Observed(string dir, int gate, int number, (int, int, int) ambiguous, string[] labels) =>
        new(dir, gate, number, "observed", 0, "observed", string.Empty, [], ambiguous, 0, 0, 0, labels);
}
