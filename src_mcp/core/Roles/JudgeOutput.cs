using System.Text.Json;

namespace CoaiMcp.Core.Roles;

/// <summary>One finding of the judge: the gate's own severity words, what is wrong, where it shows, and the smallest fix (empty when the judge gave none).</summary>
public sealed record JudgeFinding(string Id, string Severity, string Title, string Detail, IReadOnlyList<EvidenceItem> Evidence, string Fix);

/// <summary>An option the judge did not choose, and why.</summary>
public sealed record JudgeRejected(string Option, string Why);

/// <summary>The judge's choice when the brief asked it to choose; <see cref="None"/> (empty <see cref="Chosen"/>) when it did not.</summary>
public sealed record JudgeDecision(string Chosen, string Why, IReadOnlyList<JudgeRejected> Rejected)
{
    public static JudgeDecision None { get; } = new(string.Empty, string.Empty, []);

    public bool Made => Chosen.Length > 0;
}

/// <summary>The judge's answer: a verdict, its summary, the findings behind it, and a decision when one was asked for.</summary>
public sealed record JudgeAnswer(string Verdict, string Summary, IReadOnlyList<JudgeFinding> Findings, JudgeDecision Decision);

/// <summary>What reading the judge's answer gave: an answer, a declared refusal, or everything that is wrong with it. A closed set — a caller cannot forget a case.</summary>
public abstract record JudgeRead
{
    private JudgeRead()
    {
    }

    /// <summary>The answer has the shape of <c>shared/judge-output.schema.json</c>, arm <c>ok</c>, and obeys the three rules of the judge.</summary>
    public sealed record Answer(JudgeAnswer Value) : JudgeRead;

    /// <summary>The model declared it would not do the work (arm <c>refused</c>).</summary>
    public sealed record Refused(string Reason) : JudgeRead;

    /// <summary>The answer is not that shape, or breaks a rule; each entry says where and what.</summary>
    public sealed record Wrong(IReadOnlyList<string> Problems) : JudgeRead;
}

/// <summary>
/// Reads the judge's answer against <c>shared/judge-output.schema.json</c> (epoch 3 E3 s1 t3) — strictly, as <see cref="ScoutOutputReader"/> does, and then against the three rules of the judge (spec A): <c>approve</c> with a
/// <c>blocking</c> finding is an error, <c>revise</c> or <c>reject</c> with no finding is an error, and a finding id is used once. The severity words are the gate's own four (<see cref="Verdicts"/> and <see cref="Severities"/>
/// are the tables the schema file is held to by <c>AJudgeContractTests</c>).
/// </summary>
/// <remarks>Pure: JSON in, a value out. It never reads a file and never throws for an answer that is wrong. Whether an evidence item is TRUE is <see cref="RoleEvidence"/>'s, not decided here.</remarks>
public static class JudgeOutputReader
{
    /// <summary>The keys of the <c>ok</c> arm; <c>decision</c> is the only one that may be absent.</summary>
    public static readonly IReadOnlyList<string> OkKeys = ["status", "verdict", "summary", "findings", "decision"];

    /// <summary>The keys of the <c>refused</c> arm.</summary>
    public static readonly IReadOnlyList<string> RefusedKeys = ["status", "reason"];

    /// <summary>The keys of one finding; <c>evidence</c> and <c>fix</c> may be absent.</summary>
    public static readonly IReadOnlyList<string> FindingKeys = ["id", "severity", "title", "detail", "evidence", "fix"];

    /// <summary>The keys of the decision; <c>rejected</c> may be absent.</summary>
    public static readonly IReadOnlyList<string> DecisionKeys = ["chosen", "why", "rejected"];

    /// <summary>The keys of one rejected option; both are required.</summary>
    public static readonly IReadOnlyList<string> RejectedKeys = ["option", "why"];

    public static readonly IReadOnlyList<string> Verdicts = ["approve", "revise", "reject"];

    /// <summary>The gate's own four words (<c>FindingSchema</c>), most serious first.</summary>
    public static readonly IReadOnlyList<string> Severities = ["blocking", "major", "minor", "nit"];

    public const string IdPattern = "^f[1-9][0-9]*$";
    public const int MaxSummary = 1500;
    public const int MaxFindings = 30;
    public const int MaxTitle = 120;
    public const int MaxDetail = 1500;
    public const int MinEvidence = 0;
    public const int MaxEvidence = 5;
    public const int MaxFix = 600;
    public const int MaxChosen = 120;
    public const int MaxDecisionWhy = 1500;
    public const int MaxRejected = 8;
    public const int MaxOption = 120;
    public const int MaxRejectedWhy = 1500;

    public static JudgeRead Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new JudgeRead.Wrong(["the answer is not one JSON object"]);
        }

        var problems = new List<string>();
        var status = root.TryGetProperty("status", out var word) && word.ValueKind == JsonValueKind.String ? word.GetString() : null;
        RoleOutputShape.UnknownKeys(root, status == "refused" ? RefusedKeys : OkKeys, string.Empty, problems);
        if (status == "refused")
        {
            var reason = RoleOutputShape.Required(root, "reason", string.Empty, int.MaxValue, problems);

            return problems.Count > 0 ? new JudgeRead.Wrong(problems) : new JudgeRead.Refused(reason);
        }

        if (status != "ok")
        {
            problems.Add($"status: must be \"ok\" or \"refused\", not '{RoleOutputShape.Printable(status ?? "(missing)")}'");
        }

        var answer = new JudgeAnswer(Verdict(root, problems), RoleOutputShape.Required(root, "summary", string.Empty, MaxSummary, problems), Findings(root, problems), Decision(root, problems));
        JudgeRules.Apply(answer, problems);

        return problems.Count > 0 ? new JudgeRead.Wrong(problems) : new JudgeRead.Answer(answer);
    }

    private static string Verdict(JsonElement root, List<string> problems)
    {
        var verdict = root.TryGetProperty("verdict", out var word) && word.ValueKind == JsonValueKind.String ? word.GetString()! : string.Empty;
        if (!Verdicts.Contains(verdict, StringComparer.Ordinal))
        {
            problems.Add($"verdict: required, one of {string.Join(", ", Verdicts)}, not '{RoleOutputShape.Printable(verdict)}'");

            return string.Empty;
        }

        return verdict;
    }

    private static List<JudgeFinding> Findings(JsonElement root, List<string> problems)
    {
        var findings = new List<JudgeFinding>();
        foreach (var (item, where) in RoleOutputShape.Items(root, "findings", string.Empty, 0, MaxFindings, problems))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object with {string.Join(", ", FindingKeys)}");

                continue;
            }

            RoleOutputShape.UnknownKeys(item, FindingKeys, where, problems);
            findings.Add(Finding(item, where, problems));
        }

        return findings;
    }

    private static JudgeFinding Finding(JsonElement item, string where, List<string> problems) => new(
        Id(item, where, problems),
        Severity(item, where, problems),
        RoleOutputShape.Required(item, "title", where, MaxTitle, problems),
        RoleOutputShape.Required(item, "detail", where, MaxDetail, problems),
        item.TryGetProperty("evidence", out _) ? RoleOutputShape.Evidence(item, "evidence", where, MinEvidence, MaxEvidence, problems) : [],
        RoleOutputShape.Optional(item, "fix", where, MaxFix, problems));

    private static string Id(JsonElement item, string where, List<string> problems)
    {
        var id = item.TryGetProperty("id", out var word) && word.ValueKind == JsonValueKind.String ? word.GetString()! : string.Empty;
        if (!IsId(id))
        {
            problems.Add($"{where}.id: required, f1, f2, … (matching {IdPattern}), not '{RoleOutputShape.Printable(id)}'");

            return string.Empty;
        }

        return id;
    }

    /// <summary>True for <c>f1</c>, <c>f2</c>, … — matched in full, digits from 1 with no leading zero.</summary>
    public static bool IsId(string id) => id.Length >= 2 && id[0] == 'f' && id[1] is >= '1' and <= '9' && id[2..].All(char.IsAsciiDigit);

    private static string Severity(JsonElement item, string where, List<string> problems)
    {
        var severity = item.TryGetProperty("severity", out var word) && word.ValueKind == JsonValueKind.String ? word.GetString()! : string.Empty;
        if (!Severities.Contains(severity, StringComparer.Ordinal))
        {
            problems.Add($"{where}.severity: required, one of {string.Join(", ", Severities)}, not '{RoleOutputShape.Printable(severity)}'");

            return string.Empty;
        }

        return severity;
    }

    private static JudgeDecision Decision(JsonElement root, List<string> problems)
    {
        if (!root.TryGetProperty("decision", out var decision))
        {
            return JudgeDecision.None;
        }

        if (decision.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"decision: must be an object with {string.Join(", ", DecisionKeys)}");

            return JudgeDecision.None;
        }

        RoleOutputShape.UnknownKeys(decision, DecisionKeys, "decision", problems);

        return new JudgeDecision(
            RoleOutputShape.Required(decision, "chosen", "decision", MaxChosen, problems),
            RoleOutputShape.Required(decision, "why", "decision", MaxDecisionWhy, problems),
            decision.TryGetProperty("rejected", out _) ? Rejected(decision, problems) : []);
    }

    private static List<JudgeRejected> Rejected(JsonElement decision, List<string> problems)
    {
        var rejected = new List<JudgeRejected>();
        foreach (var (item, where) in RoleOutputShape.Items(decision, "rejected", "decision", 0, MaxRejected, problems))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object with {string.Join(", ", RejectedKeys)}");

                continue;
            }

            RoleOutputShape.UnknownKeys(item, RejectedKeys, where, problems);
            rejected.Add(new JudgeRejected(RoleOutputShape.Required(item, "option", where, MaxOption, problems), RoleOutputShape.Required(item, "why", where, MaxRejectedWhy, problems)));
        }

        return rejected;
    }
}

/// <summary>The three rules of the judge (spec A, scenario S8), applied to an answer that has its shape. Each is a sentence the repair turn can carry back.</summary>
internal static class JudgeRules
{
    public static void Apply(JudgeAnswer answer, List<string> problems)
    {
        if (answer.Verdict == "approve" && answer.Findings.FirstOrDefault(finding => finding.Severity == "blocking") is { } blocking)
        {
            problems.Add($"verdict: \"approve\" cannot stand with a blocking finding ({blocking.Id} is blocking); answer \"revise\" or \"reject\", or take the finding out");
        }

        if (answer.Verdict is "revise" or "reject" && answer.Findings.Count == 0)
        {
            problems.Add($"findings: the verdict \"{answer.Verdict}\" needs at least one finding that says what to change; give the finding, or answer \"approve\"");
        }

        foreach (var id in answer.Findings.Where(finding => finding.Id.Length > 0).GroupBy(finding => finding.Id).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"findings: the id '{id}' is used more than once; every finding has its own id (f1, f2, …)");
        }
    }
}
