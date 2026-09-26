using System.Globalization;
using System.Text;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Store;

/// <summary>
/// The gate's history of a piece of work, as the feature reviewer reads it — within a byte budget,
/// cut honestly, and saying on its first lines that it is evidence rather than proof.
/// </summary>
/// <remarks>
/// <para><b>The counts sentence comes first and is never cut.</b> It is the one line that says how much
/// was attached, by what, and how much in the same window was NOT — so a reviewer can weigh the rest,
/// and so a cut list is never mistaken for the whole of it. Items then follow in the query's order
/// (the epics' own branches first, plan before code, newest first) until the next would not fit; the
/// cut stops there rather than skipping ahead to a smaller item, and a closing line counts what was
/// left out.</para>
/// <para>The text is plain: the feature context fences it as material (§4.6, section 4), because every
/// title and reason in it was written by a model or by the implementer, not by this product.</para>
/// </remarks>
public static class GateHistoryText
{
    /// <summary>The history's share of a feature review's context: 24 KB (plan §4.6).</summary>
    public const int MaxBytes = 24 * 1024;

    /// <summary>Held back for the closing line that counts what the cut left out.</summary>
    private const int TailReserveBytes = 256;

    private const int TitleChars = 200;
    private const int ReasonChars = 700;
    private const int ProblemChars = 400;
    private const int AdviceChars = 800;

    private const string Preamble =
        "The gate's own history of this work — EVIDENCE, NOT PROOF. No stored field proves that a round belongs "
        + "to this plan: a rebased epic does not keep the commit its round read, base..head carries unrelated work "
        + "merged in the same range, and a heading can match by coincidence. Rounds on the epics' own branches come "
        + "first; every later item is a CANDIDATE, labelled with the one rule that tied it here. These are findings "
        + "the implementer REJECTED, with the reasons given, and consultations the implementer asked for — context "
        + "for judging the work, not findings of this review.";

    public static string Render(GateHistory history) =>
        history.Unavailable.Length > 0 ? history.Unavailable : Fit(Header(history), Items(history));

    private static string Header(GateHistory history) =>
        string.Join('\n', [Preamble, string.Empty, Counts(history), .. history.Notes]) + "\n";

    /// <summary>
    /// "N rejections from M rounds (branches …; plan round …), K consultations; NOT attached: X rounds …".
    /// </summary>
    internal static string Counts(GateHistory history)
    {
        var repeats = history.Rejections.Count < history.RejectionsFound
            ? $" ({history.Rejections.Count} after removing repeats)"
            : string.Empty;

        return $"{Count(history.RejectionsFound, "rejection")}{repeats} from {RoundsPart(history.Rounds)}, "
            + $"{Count(history.Consultations.Count, "consultation")}; NOT attached: {Count(history.NotAttached, "round")} "
            + "since the base commit with nothing tying them to this work.";
    }

    private static string RoundsPart(IReadOnlyList<AttachedRound> rounds) =>
        rounds.Count == 0 ? "no round" : $"{Count(rounds.Count, "round")} ({string.Join("; ", Ties(rounds))})";

    private static IEnumerable<string> Ties(IReadOnlyList<AttachedRound> rounds)
    {
        var branches = rounds.Where(r => r.FromAnEpic).Select(r => r.Branch).Distinct(StringComparer.Ordinal).ToList();
        var byCommit = rounds.Count(r => !r.FromAnEpic && r.Admission.HasFlag(Admission.CommitInRange));
        var byHeading = rounds.Where(r => !r.FromAnEpic && !r.Admission.HasFlag(Admission.CommitInRange) && r.Admission.HasFlag(Admission.PlanHeading))
            .Select(r => $"\"{Clip(r.Subject, 80)}\"").Distinct(StringComparer.Ordinal).ToList();

        return new[]
        {
            branches.Count > 0 ? "branches " + string.Join(", ", branches) : string.Empty,
            byCommit > 0 ? $"{Count(byCommit, "round")} by a commit in base..head" : string.Empty,
            byHeading.Count > 0 ? "plan round " + string.Join(", ", byHeading) : string.Empty,
        }.Where(t => t.Length > 0);
    }

    /// <summary>Every item in display order: a section heading, then its rejections, then its consultations.</summary>
    private static List<Item> Items(GateHistory history) =>
    [
        .. Section("From the epics' own branches:", history, fromAnEpic: true),
        .. Section("CANDIDATES — nothing but the rule named on each ties it to this work:", history, fromAnEpic: false),
    ];

    private static IEnumerable<Item> Section(string title, GateHistory history, bool fromAnEpic)
    {
        var rejections = history.Rejections.Where(r => r.Round.FromAnEpic == fromAnEpic).Select(Rejection).ToList();
        var consultations = history.Consultations.Where(c => c.FromAnEpic == fromAnEpic).Select(Consultation).ToList();

        return rejections.Count + consultations.Count == 0
            ? []
            : [new Item(ItemKind.Heading, "\n" + title), .. rejections, .. consultations];
    }

    private static Item Rejection(HistoryRejection r) => new(
        ItemKind.Rejection,
        $"- [{r.Severity}/{r.Category}] {Clip(r.Title, TitleChars)}{At(r.File, r.Line)} — "
        + $"{RoundSubject.StageName(r.Round.Stage)} round {r.Round.Number} on {r.Round.Branch}, {When(r.Round.StartedUtc)}"
        + $"{Times(r.Times)}{Label(r.Round.Admission)}\n  Rejected because: {Clip(r.Reason, ReasonChars)}");

    private static Item Consultation(HistoryConsultation c) => new(
        ItemKind.Consultation,
        $"- {c.Kind} consultation on {c.Branch}, {When(c.StartedUtc)} — {(c.Outcome.Length > 0 ? c.Outcome : c.Status)}"
        + $"{Label(c.Admission)}\n  Asked: {Clip(c.Problem, ProblemChars)}\n  Advised: {Clip(c.Advice, AdviceChars)}");

    /// <summary>Items in order until the next would not fit; then one line counting what was left out.</summary>
    private static string Fit(string header, List<Item> items)
    {
        var text = new StringBuilder(header);
        var used = Encoding.UTF8.GetByteCount(header);
        var shown = 0;
        foreach (var line in items.Select(item => item.Text + "\n"))
        {
            var size = Encoding.UTF8.GetByteCount(line);
            if (used + size > MaxBytes - TailReserveBytes)
            {
                break;
            }

            text.Append(line);
            used += size;
            shown++;
        }

        return text.Append(Tail([.. items.Skip(shown)])).ToString().TrimEnd('\n');
    }

    private static string Tail(List<Item> left)
    {
        var rejections = left.Count(i => i.Kind == ItemKind.Rejection);
        var consultations = left.Count(i => i.Kind == ItemKind.Consultation);

        return rejections + consultations == 0
            ? string.Empty
            : $"\n… cut here to stay within {MaxBytes / 1024} KB: {Count(rejections, "more rejection")} and "
                + $"{Count(consultations, "more consultation")} not shown.";
    }

    private static string At(string file, int line) => file.Length == 0
        ? string.Empty
        : line > 0 ? $" ({file}:{line})" : $" ({file})";

    private static string Times(int times) => times > 1 ? $" — rejected {times} times" : string.Empty;

    private static string Label(Admission admission) => admission.HasFlag(Admission.EpicBranch)
        ? string.Empty
        : admission.HasFlag(Admission.CommitInRange)
            ? " — CANDIDATE: its commit is in base..head"
            : " — CANDIDATE: a plan round under this plan's heading";

    /// <summary><c>2026-09-20 14:03 UTC</c> from a stored ISO instant; the value itself when it is not one.</summary>
    private static string When(string storedUtc) => storedUtc.Length >= 16
        ? storedUtc[..16].Replace('T', ' ') + " UTC"
        : storedUtc;

    private static string Count(int n, string noun) =>
        n.ToString(CultureInfo.InvariantCulture) + " " + (n == 1 ? noun : noun + "s");

    /// <summary>One line, at most <paramref name="max"/> characters, and an ellipsis when it was cut.</summary>
    private static string Clip(string text, int max)
    {
        var line = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

        return line.Length <= max ? line : line[..max].TrimEnd() + "…";
    }

    private enum ItemKind
    {
        Heading,
        Rejection,
        Consultation,
    }

    private sealed record Item(ItemKind Kind, string Text);
}
