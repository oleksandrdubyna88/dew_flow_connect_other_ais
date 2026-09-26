using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Core.Feature;

/// <summary>An input the feature review can use, or the sentence that tells the caller how to fix it.</summary>
/// <remarks>
/// A closed pair rather than a throw, because a malformed input is an expected answer (doctrine §5):
/// the stage turns <see cref="Refused"/> into its refusal and never has to catch anything.
/// </remarks>
public abstract record FeatureInput<T>
{
    public sealed record Accepted(T Value) : FeatureInput<T>;

    public sealed record Refused(string Sentence) : FeatureInput<T>;

    private FeatureInput() { }
}

/// <summary>The implementer's own account of building the feature — three arrays, none of them empty.</summary>
public sealed record FeatureLessons(
    IReadOnlyList<string> Pitfalls,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Findings);

/// <summary>One epic as the implementer describes it. <see cref="Branch"/> and <see cref="Pr"/> are empty when not given.</summary>
public sealed record FeatureEpic(string Title, string Summary, string Branch, string Pr);

/// <summary>The epics of the plan, in the caller's order.</summary>
/// <remarks>
/// <see cref="Count"/> is the D17 number and nothing more: whether a plan of this many epics is worth a
/// feature review is the stage's decision (its gate reads <c>COAI_FEATURE_MIN_EPICS</c>), so this type
/// answers "how many", never "whether".
/// </remarks>
public sealed record FeatureEpics(IReadOnlyList<FeatureEpic> Items)
{
    public int Count => Items.Count;
}

/// <summary>
/// The two JSON arguments of <c>review_feature</c> the caller writes by hand — parsed and refused
/// here, before any git or model work (plan §4.5).
/// </summary>
/// <remarks>
/// <para><b>Pure, and it reads no file.</b> Everything here is a decision about text the caller sent,
/// so it is testable without a repository and runs before the stage spends anything.</para>
/// <para><b>Why "none" needs a reason.</b> The plan round of 2026-09-25 found the first draft
/// contradicting itself: "every array must be non-empty" and "an empty array is fine when there was
/// nothing" cannot both hold for one empty array. The resolution is that "nothing here" is written as an
/// entry that says so AND why — the "why" is what a reviewer of the whole feature can use, and a bare
/// "none" is the empty array wearing a word.</para>
/// <para>Parsed through <see cref="JsonDocument"/>, not a serializer: the core is AOT with reflection
/// off, and an unknown key has to be SEEN to be refused — a serializer would drop it silently.</para>
/// </remarks>
public static class FeatureInputs
{
    /// <summary>The most <c>lessons</c> may weigh, in UTF-8 bytes: 32 KB (plan §4.5).</summary>
    public const int LessonsMaxBytes = 32 * 1024;

    /// <summary>The most <c>epics</c> may weigh, in UTF-8 bytes: 16 KB (plan §4.5).</summary>
    public const int EpicsMaxBytes = 16 * 1024;

    /// <summary>At least one epic — a plan with none is not a plan that was built.</summary>
    public const int MinEpics = 1;

    /// <summary>At most twenty — past that the list is a changelog, not a plan's epics (plan §4.5).</summary>
    public const int MaxEpics = 20;

    /// <summary>After a "none", this many characters of reason at least.</summary>
    /// <remarks>
    /// "none" followed by "blockers" is still a bare none; "none — every epic merged without a blocker"
    /// is an answer. Twenty characters is about where a phrase stops naming the array and starts saying
    /// something about the work.
    /// </remarks>
    public const int NoneReasonFloor = 20;

    /// <summary>The three keys of <c>lessons</c>, in the order a reviewer reads them.</summary>
    public static readonly IReadOnlyList<string> LessonKeys = ["pitfalls", "blockers", "findings"];

    /// <summary>The four keys an epic may carry; <c>title</c> and <c>summary</c> are required.</summary>
    public static readonly IReadOnlyList<string> EpicKeys = ["title", "summary", "branch", "pr"];

    /// <summary>The questions every <c>lessons</c> refusal asks — the answer the gate actually needs.</summary>
    public static readonly IReadOnlyList<string> LessonQuestions =
    [
        "What went wrong or nearly wrong, and where?",
        "What blocked you, and how was it resolved — or is it still open?",
        "What must a reviewer of the WHOLE feature know — a seam between epics, a workaround, something deliberately left undone?",
        "Which rejected gate finding are you least sure of?",
    ];

    /// <summary>Words that, opening an entry, say "nothing here".</summary>
    private static readonly string[] NoneWords = ["none", "nothing", "n/a", "na", "nil", "null", "no"];

    /// <summary>Reads <c>lessons</c>: <c>{"pitfalls":[…],"blockers":[…],"findings":[…]}</c>, every array non-empty.</summary>
    public static FeatureInput<FeatureLessons> ParseLessons(string json)
    {
        var text = json ?? string.Empty;
        var bytes = Encoding.UTF8.GetByteCount(text);

        return text.Trim().Length == 0 ? LessonsRefusal("lessons was not given")
            : bytes > LessonsMaxBytes ? LessonsRefusal($"lessons is {bytes} bytes, over the {LessonsMaxBytes}-byte cap — keep each entry to what a reviewer needs")
            : Document(text, LessonsFrom, LessonsRefusal);
    }

    /// <summary>Reads <c>epics</c>: <c>[{title, summary, branch?, pr?}]</c>, 1–20 entries, ≤16 KB.</summary>
    public static FeatureInput<FeatureEpics> ParseEpics(string json)
    {
        var text = json ?? string.Empty;
        var bytes = Encoding.UTF8.GetByteCount(text);

        return text.Trim().Length == 0 ? EpicsRefusal("epics was not given")
            : bytes > EpicsMaxBytes ? EpicsRefusal($"epics is {bytes} bytes, over the {EpicsMaxBytes}-byte cap — a summary is a paragraph, not the epic's diff")
            : Document(text, EpicsFrom, EpicsRefusal);
    }

    /// <summary>
    /// The refusal for <c>lessons</c>, whatever is wrong with it: the problem, the shape, and ALL four
    /// questions — so a caller who sent nothing and a caller who sent one empty array are both told what
    /// the gate wants to know, not only what the parser disliked.
    /// </summary>
    public static FeatureInput<FeatureLessons>.Refused LessonsRefusal(string problem)
    {
        var questions = string.Join(" ", LessonQuestions.Select((q, i) => $"({i + 1}) {q}"));

        return new(
            "review_feature needs `lessons` — your own account of building this feature, as "
            + "{\"pitfalls\":[…],\"blockers\":[…],\"findings\":[…]}, each array with at least one entry: "
            + $"{problem}. Answer these four questions in it: {questions} When an array genuinely has "
            + "nothing, write an entry that says so AND why (\"none — every epic merged without a blocker; "
            + "the one risk was X and it did not happen\"), never []. A reviewer of the whole feature has "
            + "only your account of what the slices could not show.");
    }

    /// <summary>
    /// Whether an entry is "nothing here" with no reason: a none-word, then fewer than
    /// <see cref="NoneReasonFloor"/> characters of anything else.
    /// </summary>
    /// <remarks>
    /// "no retry on a 429 in the api shim, fixed in S1.2" opens with "no" and is a real pitfall — the
    /// reason floor is what tells it from "no blockers".
    /// </remarks>
    public static bool IsBareNone(string entry)
    {
        var lowered = entry.Trim().ToLowerInvariant();
        var word = Array.Find(NoneWords, w => lowered.StartsWith(w, StringComparison.Ordinal)
            && (lowered.Length == w.Length || !char.IsLetterOrDigit(lowered[w.Length])));

        return word is not null && Reason(lowered[word.Length..]).Length < NoneReasonFloor;
    }

    private static string Reason(string rest) => rest.Trim().TrimStart('—', '-', ':', ';', ',', '.', '(', ')').Trim();

    private static FeatureInput<FeatureEpics>.Refused EpicsRefusal(string problem) =>
        new($"review_feature needs `epics` as a JSON array of {MinEpics} to {MaxEpics} entries, each "
            + "{\"title\":\"…\",\"summary\":\"…\",\"branch\":\"…\",\"pr\":\"…\"} — title and summary required, "
            + $"branch and pr optional: {problem}. The branch is what lets the gate's history of a "
            + "squash-merged epic be found, so give it where the epic had one.");

    private static FeatureInput<T> Document<T>(
        string text,
        Func<JsonElement, FeatureInput<T>> read,
        Func<string, FeatureInput<T>.Refused> refuse)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            return read(document.RootElement);
        }
        catch (JsonException e)
        {
            return refuse($"it is not JSON ({e.Message})");
        }
    }

    private static FeatureInput<FeatureLessons> LessonsFrom(JsonElement root) =>
        root.ValueKind != JsonValueKind.Object
            ? LessonsRefusal($"it is a JSON {Kind(root)}, not an object")
            : UnknownKey(root, LessonKeys) is { Length: > 0 } unknown
                ? LessonsRefusal($"'{unknown}' is not a key of lessons — the keys are {string.Join(", ", LessonKeys)}")
                : Arrays(root);

    /// <summary>The three arrays in key order; the first fault found is the one named.</summary>
    private static FeatureInput<FeatureLessons> Arrays(JsonElement root)
    {
        var arrays = new List<IReadOnlyList<string>>();
        foreach (var key in LessonKeys)
        {
            var problem = ArrayProblem(root, key);
            if (problem.Length > 0)
            {
                return LessonsRefusal(problem);
            }

            arrays.Add([.. root.GetProperty(key).EnumerateArray().Select(e => e.GetString()!.Trim())]);
        }

        return Substance(new FeatureLessons(arrays[0], arrays[1], arrays[2]));
    }

    /// <summary>The total is the last check: shapes first, so the sentence names the most specific fault.</summary>
    private static FeatureInput<FeatureLessons> Substance(FeatureLessons lessons)
    {
        var total = lessons.Pitfalls.Concat(lessons.Blockers).Concat(lessons.Findings).Sum(e => e.Length);

        return total >= ReviewScope.Floor
            ? new FeatureInput<FeatureLessons>.Accepted(lessons)
            : LessonsRefusal($"the three arrays hold {total} characters together, under the {ReviewScope.Floor} a reviewer can use — say what happened, not that something did");
    }

    /// <summary>What is wrong with one array of <c>lessons</c>, or empty when nothing is.</summary>
    private static string ArrayProblem(JsonElement root, string key) =>
        !root.TryGetProperty(key, out var array) || array.ValueKind != JsonValueKind.Array
            ? $"'{key}' is missing or not an array"
            : array.GetArrayLength() == 0
                ? $"'{key}' is empty"
                : array.EnumerateArray().Select((e, i) => EntryProblem(key, e, i + 1)).FirstOrDefault(p => p.Length > 0) ?? string.Empty;

    private static string EntryProblem(string key, JsonElement element, int number)
    {
        var entry = element.ValueKind == JsonValueKind.String ? (element.GetString() ?? string.Empty).Trim() : string.Empty;

        return entry.Length == 0 ? $"'{key}' entry {number} is not a non-empty string"
            : IsBareNone(entry) ? $"'{key}' entry {number} says \"{entry}\" without saying why — a \"none\" needs its reason"
            : string.Empty;
    }

    private static FeatureInput<FeatureEpics> EpicsFrom(JsonElement root) =>
        root.ValueKind != JsonValueKind.Array
            ? EpicsRefusal($"it is a JSON {Kind(root)}, not an array")
            : root.GetArrayLength() is < MinEpics or > MaxEpics
                ? EpicsRefusal($"it has {root.GetArrayLength()} entries")
                : Epics(root);

    private static FeatureInput<FeatureEpics> Epics(JsonElement root)
    {
        var epics = new List<FeatureEpic>();
        foreach (var element in root.EnumerateArray())
        {
            var problem = EpicProblem(element, epics.Count + 1);
            if (problem.Length > 0)
            {
                return EpicsRefusal(problem);
            }

            epics.Add(new FeatureEpic(Text(element, "title"), Text(element, "summary"), Text(element, "branch"), Text(element, "pr")));
        }

        return new FeatureInput<FeatureEpics>.Accepted(new FeatureEpics(epics));
    }

    private static string EpicProblem(JsonElement element, int number) =>
        element.ValueKind != JsonValueKind.Object ? $"entry {number} is a JSON {Kind(element)}, not an object"
        : UnknownKey(element, EpicKeys) is { Length: > 0 } unknown
            ? $"entry {number} has '{unknown}', which is not a key of an epic — the keys are {string.Join(", ", EpicKeys)}"
        : Text(element, "title").Length == 0 ? $"entry {number} has no title"
        : Text(element, "summary").Length == 0 ? $"entry {number} has no summary"
        : string.Empty;

    /// <summary>A string or a number, trimmed; anything else (absent, null, an object) is empty.</summary>
    /// <remarks>A number is accepted because a pull request is a number and a caller will write it as one.</remarks>
    private static string Text(JsonElement element, string key) =>
        !element.TryGetProperty(key, out var value) ? string.Empty
        : value.ValueKind switch
        {
            JsonValueKind.String => (value.GetString() ?? string.Empty).Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => string.Empty,
        };

    private static string UnknownKey(JsonElement element, IReadOnlyList<string> legal) =>
        element.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => !legal.Contains(name, StringComparer.Ordinal)) ?? string.Empty;

    private static string Kind(JsonElement element) => element.ValueKind.ToString().ToLowerInvariant();
}
