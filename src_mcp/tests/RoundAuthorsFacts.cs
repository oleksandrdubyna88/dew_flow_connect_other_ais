using CoaiMcp.Core.Authors;
using CoaiMcp.Store;
using static CoaiMcp.Tests.RoundAuthorsSeed;

namespace CoaiMcp.Tests;

/// <summary>
/// In-memory <see cref="RoundFacts"/> and selections for the pure units of the round-authors report (E6.S2-S5): rounds a reader can see, built
/// without a database — the same builders in every class, so the population, the cells and the merge are proved against one world.
/// </summary>
internal static class RoundAuthorsFacts
{
    public const string Caller = "c1";

    public static readonly string[] CodeAndTest = ["code", "test"];

    public static readonly ReviewerFact Codex = new("codex", "gpt-6-luna", "high", true, "done");

    public static StoredAuthorRow MainRow(string association = Association.SameSession, string model = "claude-opus-5-5", string window = "round") =>
        new(0, "main", string.Empty, "opus", model, "high", string.Empty, association, window, 0, 2, 1);

    public static StoredAuthorRow Coder(string agent, string model, string requested = "sonnet", string effort = "high", string association = Association.WroteHere, string window = "round", int launches = 1, int writes = 1) =>
        new(1, "subagent", agent, requested, model, effort, "general-purpose", association, window, launches, 2, writes);

    public static AuthorsFacts Observed(params StoredAuthorRow[] rows) =>
        new(AuthorsStateKind.Observed, [], Caller, rows, new AmbiguousFacts(true, 0, 0, 0, 0, 0, 0, 0), 0);

    public static AuthorsFacts Stated(AuthorsStateKind kind, string reason, int uncovered = 0, params StoredAuthorRow[] rows) =>
        new(kind, reason.Length == 0 ? [] : [reason], Caller, rows, new AmbiguousFacts(true, 0, 0, 0, 0, 0, 0, 0), uncovered);

    public static readonly AuthorsFacts Typical = Observed(MainRow(), Coder("a1", "claude-sonnet-5-5"));

    public static SizeFacts Sized(Core.Context.RoundSize size) => new(true, size);

    public static SizeFacts Code(int lines) => Sized(Whole(Cell("shown", "code", lines)));

    /// <summary>One round. <paramref name="session"/> <c>s</c> is the default session; any other gives its rounds ids of its own. The default round has 100 code lines, one codex reviewer and one finding.</summary>
    public static RoundFacts Round(
        int number,
        double hours = double.NaN,
        string stage = "CodeReview",
        string session = "s",
        bool inWindow = true,
        string status = "done",
        string verdict = "proceed",
        AuthorsFacts? authors = null,
        SizeFacts? size = null,
        ReviewerFact[]? reviewers = null,
        string branch = "main",
        FindingFacts? findings = null,
        string repo = "D:/repo") =>
        new(
            Slot(session) * 1000 + number + (stage == "PlanReview" ? 100 : 0), session, repo, branch, stage, number, status, verdict,
            Stamp(T0.AddHours(double.IsNaN(hours) ? number : hours)), inWindow, authors ?? Typical, size ?? Code(100), reviewers ?? [Codex],
            findings ?? new FindingFacts(1, 1, 0, 0, 0, 1, false));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Slots = new();

    private static int _next;

    /// <summary>A number per session name, so every round of every session of a test has an id of its own and ids never collide across parallel classes.</summary>
    private static int Slot(string session) => Slots.GetOrAdd(session, _ => Interlocked.Increment(ref _next));

    public static RoundAuthorsSelection Sel(string stage = "CodeReview", string cohort = "all", string[]? kinds = null, string split = "", bool beforeOpen = false, bool withoutElided = false, bool withUncovered = false) =>
        new(stage, string.Empty, string.Empty, split, cohort, kinds ?? CodeAndTest, beforeOpen, withoutElided, withUncovered);

    /// <summary>
    /// The tests' own spelling of "a character a terminal would act on or a reader cannot see": a control, a format character (zero-width, bidi
    /// override, BOM, soft hyphen), a line or paragraph separator, or an unassigned code point. Spelled here, apart from <c>Printable</c>, so a test
    /// of that method is not graded by the method itself.
    /// </summary>
    public static bool IsTerminalUnsafe(char at) =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(at) is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format
            or System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator or System.Globalization.UnicodeCategory.OtherNotAssigned;
}
