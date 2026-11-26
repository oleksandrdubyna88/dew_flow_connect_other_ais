using System.Text.Json;

namespace CoaiMcp.Core.Roles;

/// <summary>One thing the scout found: a claim and the places that show it.</summary>
public sealed record ScoutFact(string Claim, IReadOnlyList<EvidenceItem> Evidence);

/// <summary>The scout's answer: what it found, the files a reader of the task needs, and what it looked for and did not find.</summary>
public sealed record ScoutAnswer(IReadOnlyList<ScoutFact> Facts, IReadOnlyList<string> Files, IReadOnlyList<string> Unknown);

/// <summary>What reading the scout's answer gave: an answer, a declared refusal, or everything that is wrong with the shape. A closed set — a caller cannot forget a case.</summary>
public abstract record ScoutRead
{
    private ScoutRead()
    {
    }

    /// <summary>The answer has the shape of <c>shared/scout-output.schema.json</c>, arm <c>ok</c>.</summary>
    public sealed record Answer(ScoutAnswer Value) : ScoutRead;

    /// <summary>The model declared it would not do the work (arm <c>refused</c>).</summary>
    public sealed record Refused(string Reason) : ScoutRead;

    /// <summary>The answer is not that shape; each entry says where and what.</summary>
    public sealed record Wrong(IReadOnlyList<string> Problems) : ScoutRead;
}

/// <summary>
/// Reads the scout's answer against <c>shared/scout-output.schema.json</c> (epoch 3 E3 s1 t2) — strictly: an unknown key, a missing key, a value of the wrong type, a text over its length, a list over its count, a blank text and a
/// path that may not be opened are each a problem, and ALL are listed. Neither half of the product has a JSON Schema engine, so this is the engine for the one shape, and <c>AScoutContractTests</c> holds its tables to the file.
/// </summary>
/// <remarks>
/// <para>The limits are public constants so the prompt is built from them (<c>ScoutPrompt</c>): a changed limit shows in what the model is told. Whether an evidence item is TRUE is not decided here — that needs the file, and
/// <see cref="RoleEvidence"/> does it; this reader only refuses what is not a well-formed item, a blank text first among them (S12).</para>
/// <para>Pure: JSON in, a value out. It never reads a file and never throws for an answer that is wrong.</para>
/// </remarks>
public static class ScoutOutputReader
{
    /// <summary>The keys of the <c>ok</c> arm; the schema file's arm lists the same.</summary>
    public static readonly IReadOnlyList<string> OkKeys = ["status", "facts", "files", "unknown"];

    /// <summary>The keys of the <c>refused</c> arm.</summary>
    public static readonly IReadOnlyList<string> RefusedKeys = ["status", "reason"];

    /// <summary>The keys of one fact; both are required.</summary>
    public static readonly IReadOnlyList<string> FactKeys = ["claim", "evidence"];

    public const int MaxFacts = 40;
    public const int MaxClaim = 400;
    public const int MinEvidence = 1;
    public const int MaxEvidence = 5;
    public const int MaxFiles = 60;
    public const int MaxUnknown = 20;
    public const int MaxUnknownText = 300;

    public static ScoutRead Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new ScoutRead.Wrong(["the answer is not one JSON object"]);
        }

        var problems = new List<string>();
        var status = root.TryGetProperty("status", out var word) && word.ValueKind == JsonValueKind.String ? word.GetString() : null;
        RoleOutputShape.UnknownKeys(root, status == "refused" ? RefusedKeys : OkKeys, string.Empty, problems);
        if (status == "refused")
        {
            var reason = RoleOutputShape.Required(root, "reason", string.Empty, int.MaxValue, problems);

            return problems.Count > 0 ? new ScoutRead.Wrong(problems) : new ScoutRead.Refused(reason);
        }

        if (status != "ok")
        {
            problems.Add($"status: must be \"ok\" or \"refused\", not '{RoleOutputShape.Printable(status ?? "(missing)")}'");
        }

        var answer = new ScoutAnswer(Facts(root, problems), Files(root, problems), RoleOutputShape.Texts(root, "unknown", string.Empty, 0, MaxUnknown, MaxUnknownText, problems));

        return problems.Count > 0 ? new ScoutRead.Wrong(problems) : new ScoutRead.Answer(answer);
    }

    private static List<ScoutFact> Facts(JsonElement root, List<string> problems)
    {
        var facts = new List<ScoutFact>();
        foreach (var (item, where) in RoleOutputShape.Items(root, "facts", string.Empty, 0, MaxFacts, problems))
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{where}: must be an object with {string.Join(", ", FactKeys)}");

                continue;
            }

            RoleOutputShape.UnknownKeys(item, FactKeys, where, problems);
            facts.Add(new ScoutFact(
                RoleOutputShape.Required(item, "claim", where, MaxClaim, problems),
                RoleOutputShape.Evidence(item, "evidence", where, MinEvidence, MaxEvidence, problems)));
        }

        return facts;
    }

    private static List<string> Files(JsonElement root, List<string> problems)
    {
        var files = new List<string>();
        foreach (var (item, where) in RoleOutputShape.Items(root, "files", string.Empty, 0, MaxFiles, problems))
        {
            var path = item.ValueKind == JsonValueKind.String ? item.GetString()! : string.Empty;
            var problem = item.ValueKind == JsonValueKind.String ? RoleEvidence.PathProblem(where, path) : $"{where}: must be a path inside the repository, as a string";
            if (problem.Length > 0)
            {
                problems.Add(problem);
            }
            else
            {
                files.Add(path);
            }
        }

        return files;
    }
}
