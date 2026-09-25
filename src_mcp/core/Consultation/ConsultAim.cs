using System.Globalization;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Cadence;

namespace CoaiMcp.Core.Consultation;

/// <summary>The three things a consultation can be FOR — and which prompt the consultant is given for each.</summary>
/// <remarks>
/// <c>todo/PLAN_consult_on_a_cadence.md</c>, epic 2. <c>stuck</c> is every consultation there was before:
/// the caller recognised it was stuck and asked. <c>cadence</c> and <c>risk</c> are the ones the gate ORDERS,
/// before a group of epics or a risky piece is built.
/// </remarks>
public static class ConsultKinds
{
    public const string Stuck = "stuck";
    public const string Cadence = "cadence";
    public const string Risk = "risk";

    /// <summary>
    /// The consultant's prompt for a kind. A kind this build does not know is prompted as stuck: a record
    /// written by a newer build still gets a consultant rather than a missing file.
    /// </summary>
    public static string PromptId(string kind) => kind switch
    {
        Cadence => "consult-cadence",
        Risk => "consult-risk",
        _ => "consult",
    };
}

/// <summary>
/// What one consultation is for, in the canonical form the gate matches on.
/// </summary>
/// <param name="Kind">One of <see cref="ConsultKinds"/>.</param>
/// <param name="Plan">The plan file, repo-relative with forward slashes; empty for a stuck consultation.</param>
/// <param name="Epics">A group's range (<c>4-6</c>, <c>7</c>) or a risk item's key (<c>7</c>, <c>7/7.2</c>);
/// empty for a stuck consultation.</param>
/// <remarks>
/// ONE path from the caller's spelling to the identity (epic 2's plan round, codex): <c>research\PLAN_x.md</c>
/// and <c>todo/PLAN_x.md</c> are one plan, <c>04-06</c> and <c>4-6</c> one group. A consultation recorded under
/// a spelling the gate does not match would be taken, paid for, and never count.
/// </remarks>
public sealed partial record ConsultAim(string Kind, string Plan, string Epics)
{
    /// <summary>Every consultation before the cadence existed.</summary>
    public static ConsultAim Stuck { get; } = new(ConsultKinds.Stuck, string.Empty, string.Empty);

    public bool IsStuck => Kind == ConsultKinds.Stuck;

    /// <summary>The plan's cadence key — its file name, as <see cref="EpicRef.PlanKey"/> keys it.</summary>
    public string PlanKey => EpicRef.PlanKey(Plan);

    private const int MatchTimeoutMs = 1000;

    /// <summary>Reads the three arguments; a refusal names what to send instead.</summary>
    public static (ConsultAim Aim, string Refusal) Parse(string kind, string plan, string epics)
    {
        var said = (kind ?? string.Empty).Trim().ToLowerInvariant();
        return said switch
        {
            "" or ConsultKinds.Stuck => (Stuck, string.Empty),
            ConsultKinds.Cadence or ConsultKinds.Risk => Aimed(said, (plan ?? string.Empty).Trim(), (epics ?? string.Empty).Trim()),
            _ => (Stuck, $"kind must be '{ConsultKinds.Stuck}' (the default: you are stuck), '{ConsultKinds.Cadence}' (a group of "
                         + $"epics before it is built) or '{ConsultKinds.Risk}' (a piece you named as risky) — '{kind}' is none of them."),
        };
    }

    private static (ConsultAim, string) Aimed(string kind, string plan, string epics)
    {
        if (plan.Length == 0 || epics.Length == 0)
        {
            return (Stuck, $"a {kind} consultation needs plan (the plan file, repo-relative, e.g. 'todo/PLAN_x.md') and epics "
                           + $"({Shape(kind)}) — this call sent no {(plan.Length == 0 ? "plan" : "epics")}.");
        }
        if (!IsRepoRelative(plan))
        {
            return (Stuck, $"plan must be the plan file's repo-relative path, e.g. 'todo/PLAN_x.md' — '{plan}' is absolute or "
                           + "climbs out of the repository with '..'.");
        }
        var canonical = kind == ConsultKinds.Cadence ? GroupRange(epics) : RiskKey(epics);

        return canonical.Length > 0
            ? (new ConsultAim(kind, plan.Replace('\\', '/'), canonical), string.Empty)
            : (Stuck, $"epics for a {kind} consultation is {Shape(kind)} — '{epics}' does not read that way.");
    }

    /// <summary>
    /// A path inside the repository: not rooted, no drive, no '..' segment (epic 2's code round, gemini) — the
    /// plan names a record and an audit line, and one that names something outside the repository misleads both.
    /// </summary>
    public static bool IsRepoRelative(string plan)
    {
        var slashed = plan.Replace('\\', '/');

        return !slashed.StartsWith('/')
            && !(slashed.Length > 1 && slashed[1] == ':')
            && !slashed.Contains('\0')
            && !slashed.Split('/').Contains("..");
    }

    private static string Shape(string kind) =>
        kind == ConsultKinds.Cadence ? "a group of epics, '4-6' or '7'" : "a risky epic or story, '7' or '7/7.2'";

    private static string GroupRange(string epics)
    {
        var match = Group().Match(epics);
        if (!match.Success)
        {
            return string.Empty;
        }
        var first = Number(match.Groups[1].Value);
        var last = match.Groups[2].Success ? Number(match.Groups[2].Value) : first;

        return first >= 1 && first <= last ? new EpicGroup(first, last).Range : string.Empty;
    }

    private static string RiskKey(string epics)
    {
        var match = Risk().Match(epics);
        if (!match.Success || Number(match.Groups[1].Value) < 1)
        {
            return string.Empty;
        }
        var epic = Number(match.Groups[1].Value);
        var story = match.Groups[2].Success ? CanonicalStory(match.Groups[2].Value) : string.Empty;

        // A story of ANOTHER epic is a contradiction, not a key (epic 3's code round, gemini).
        return StoryBelongsTo(story, epic) ? new RiskItem(epic, story, string.Empty).Key : string.Empty;
    }

    /// <summary>Whether a story number is one of this epic's: <c>7.2</c> is epic 7's; an empty story is the epic itself.</summary>
    public static bool StoryBelongsTo(string story, int epic) =>
        story.Length == 0 || story.StartsWith($"{epic}.", StringComparison.Ordinal);

    /// <summary>A story number without leading zeros on either side of its dot: <c>07.02</c> is <c>7.2</c>.</summary>
    /// <remarks>Public because the risk answer must store the SAME key <c>consult</c> matches (PR #549, CodeRabbit).
    /// The caller has already matched it as digits and a dot.</remarks>
    public static string CanonicalStory(string story) =>
        string.Join('.', story.Split('.').Select(part => Number(part).ToString(CultureInfo.InvariantCulture)));

    private static int Number(string digits) => int.Parse(digits, CultureInfo.InvariantCulture);

    // [0-9], never \d: .NET's \d is every Unicode digit, and int.Parse refuses the ones that are not ASCII.
    [GeneratedRegex(@"^([0-9]{1,3})\s*(?:-\s*([0-9]{1,3}))?$", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Group();

    [GeneratedRegex(@"^([0-9]{1,3})\s*(?:/\s*([0-9]{1,3}(?:\.[0-9]{1,3})?))?$", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex Risk();
}
