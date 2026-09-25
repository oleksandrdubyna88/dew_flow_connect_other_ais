using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Consultation;

namespace CoaiMcp.Server;

/// <summary>
/// The caller's answer to the risk question, read — or the sentence that says what is wrong with it
/// (<c>todo/PLAN_consult_on_a_cadence.md</c>, D6).
/// </summary>
/// <remarks>
/// <para><b>Bounded before it is built</b> (epic 3's code round, codex and local): the text is refused past
/// <see cref="MostBytes"/>, and an array longer than the cap is refused on its length alone, before a single
/// item is made — a million valid-looking entries must not cost a million allocations to be told "at most
/// three".</para>
/// <para><b>Each entry's own fields are checked before the list's count</b> (epic 3's plan round), so a
/// missing reason is not hidden behind "too many". A story must be one of its own epic's (<c>7.2</c> is epic
/// 7's): a story of another epic is a contradiction, not a key (epic 3's code round, gemini).</para>
/// </remarks>
public abstract partial record RiskAnswer
{
    /// <summary>A usable answer: the items (possibly none) and the note that explains none.</summary>
    public sealed record Given(IReadOnlyList<RiskItem> Items, string Note) : RiskAnswer;

    /// <summary>An answer that cannot be used, and the sentence that says how to fix it.</summary>
    public sealed record Refused(string Sentence) : RiskAnswer;

    private RiskAnswer() { }

    /// <summary>The largest risk answer read at all — three items with reasons fit in a fraction of it.</summary>
    public const int MostBytes = 16 * 1024;

    private const int MatchTimeoutMs = 1000;

    /// <summary>Reads <paramref name="json"/> against the cap of <paramref name="most"/> items.</summary>
    public static RiskAnswer Of(string json, string note, int most)
    {
        if (json.Length > MostBytes)
        {
            return new Refused($"riskItems is {json.Length} characters — too large for an answer of at most {most} items; name only the ones that matter.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);

            return document.RootElement.ValueKind == JsonValueKind.Array
                ? OfArray(document.RootElement, note, most)
                : new Refused(Shape(json));
        }
        catch (JsonException)
        {
            return new Refused(Shape(json));
        }
    }

    private static RiskAnswer OfArray(JsonElement array, string note, int most)
    {
        var length = array.GetArrayLength();
        if (length > most)
        {
            return new Refused($"riskItems names {length} — name at most {most}, the ones where being wrong is most expensive; "
                + "a list of everything answers nothing.");
        }
        if (length == 0 && note.Length == 0)
        {
            return new Refused("riskItems is empty — say why nothing in this plan is risky in riskNote, so the answer is a decision rather than a skip.");
        }

        var items = new List<RiskItem>();
        foreach (var element in array.EnumerateArray())
        {
            switch (Entry(element))
            {
                case Refused refused:
                    return refused;
                case GivenItem given:
                    items.Add(given.Item);
                    break;
            }
        }

        return new Given(items, note);
    }

    /// <summary>One entry, read: the item, or the sentence about what it lacks.</summary>
    private static RiskAnswer Entry(JsonElement element)
    {
        var epic = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("epic", out var e) && e.TryGetInt32(out var n) ? n : 0;
        var story = Canonical(Text(element, "story"));
        var reason = Text(element, "reason");
        if (epic < 1)
        {
            return new Refused("every riskItems entry needs \"epic\": the epic's own number, 1 or more.");
        }
        if (reason.Length == 0)
        {
            return new Refused($"riskItems entry for epic {epic} has no \"reason\" — say why being wrong there is expensive.");
        }

        return story.Length > 0 && (!StoryNumber().IsMatch(story) || !ConsultAim.StoryBelongsTo(story, epic))
            ? new Refused($"riskItems entry for epic {epic} names story '{story}', which is not a story of epic {epic} like '{epic}.2'.")
            : new GivenItem(new RiskItem(epic, story, reason));
    }

    /// <summary>One entry that is usable — private to the reading, never an answer on its own.</summary>
    private sealed record GivenItem(RiskItem Item) : RiskAnswer;

    /// <summary>
    /// The story in the form <c>consult</c> keys it — <c>2.01</c> and <c>02.1</c> are <c>2.1</c> — or as written when
    /// it is not a story number at all, which <see cref="Entry"/> then refuses. Stored raw, <c>2.01</c> was an item
    /// no consultation could ever satisfy (PR #549, CodeRabbit).
    /// </summary>
    private static string Canonical(string story) =>
        StoryNumber().IsMatch(story) ? ConsultAim.CanonicalStory(story) : story;

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : string.Empty;

    private static string Shape(string json) =>
        $"riskItems must be a JSON array like [{{\"epic\": 7, \"story\": \"7.2\", \"reason\": \"why\"}}] — '{(json.Length > 60 ? json[..60] + "…" : json)}' is not one.";

    [GeneratedRegex(@"^[0-9]{1,3}(?:\.[0-9]{1,3})?$", RegexOptions.CultureInvariant, MatchTimeoutMs)]
    private static partial Regex StoryNumber();
}
