using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Security;

namespace CoaiMcp.Server;

/// <summary>
/// <c>--check-security [--validate]</c>: what the security lane would make of a piece of text — the signals it raises,
/// the cards that would be due, the patterns it refuses — read from ONE JSON object on stdin, answered as one on stdout
/// (todo/PLAN_one_model_catalog.md, epic 2, story 4: what the editor's "Try it" calls).
/// </summary>
/// <remarks>
/// <para><b>Stdin, never argv.</b> A sample can hold a token, and a command line is in process listings, in shell history,
/// and limited to 32 K characters on Windows. The lane being EDITED travels with it (<c>lane</c>), so the answer is about
/// the words on the screen rather than the ones saved; absent, the shipped words answer.</para>
/// <para><b><c>--validate</c></b> only compiles the lane's patterns and says which it refuses — the save-time check, which
/// needs no text.</para>
/// <para><b>65 for a request it cannot read, never 64.</b> 64 is "never heard of this mode": it is how the extension
/// recognises an older binary and falls back, and a bad request wearing it would hide itself behind that fallback.</para>
/// </remarks>
internal static class CheckSecurityMode
{
    /// <summary>The largest request read: a sample past the detector's own limit is not what "Try it" is for.</summary>
    internal const int MaxRequestCharacters = 2 * 1024 * 1024;

    private const int ExDataErr = 65;

    /// <summary>
    /// Why the arguments are refused — the mode reads its name and at most one <c>--validate</c>, everything else is on
    /// stdin — or nothing. An argument it does not read is 65, never ignored into a successful answer.
    /// </summary>
    internal static string ArgumentRefusal(string[] args)
    {
        var rest = args.Skip(1).ToList();

        return rest.Count == 0 || (rest.Count == 1 && rest[0] == "--validate")
            ? string.Empty
            : $"--check-security takes at most one --validate and reads everything else on stdin; it does not read '{string.Join(" ", rest)}'";
    }

    /// <summary>
    /// Stdin, up to <paramref name="limit"/> characters and no further: a request at the limit is refused by
    /// <see cref="Answer"/>, and nothing past it is ever held in memory.
    /// </summary>
    internal static async Task<string> ReadBoundedAsync(TextReader reader, int limit = MaxRequestCharacters)
    {
        var text = new System.Text.StringBuilder();
        var chunk = new char[8192];
        int got;
        while (text.Length < limit && (got = await reader.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, limit - text.Length)))) > 0)
        {
            text.Append(chunk, 0, got);
        }

        return text.ToString();
    }

    internal static async Task<int> RunAsync(string[] args)
    {
        if (ArgumentRefusal(args) is { Length: > 0 } refused)
        {
            Program.Note(refused);

            return ExDataErr;
        }

        var stdin = await ReadBoundedAsync(Console.In);
        var (code, output, error) = Answer(stdin, args.Length > 1);
        if (output.Length > 0)
        {
            await Console.Out.WriteLineAsync(output);
        }
        if (error.Length > 0)
        {
            Program.Note(error);
        }

        return code;
    }

    /// <summary>The whole mode with no console in it: the exit code, what goes to stdout, and what goes to stderr.</summary>
    internal static (int Code, string Out, string Err) Answer(string stdin, bool validate)
    {
        if (stdin.Length >= MaxRequestCharacters)
        {
            return (ExDataErr, string.Empty, $"the request reaches {MaxRequestCharacters} characters; --check-security reads less than that");
        }
        try
        {
            using var request = JsonDocument.Parse(stdin);

            return Answered(request.RootElement, validate);
        }
        catch (JsonException e)
        {
            return (ExDataErr, string.Empty, $"--check-security reads one JSON object on stdin — {{\"text\": …, \"lane\": …}}: {e.Message}");
        }
    }

    private static (int Code, string Out, string Err) Answered(JsonElement request, bool validate)
    {
        var refusal = RequestRefusal(request, validate);
        var lane = refusal.Length == 0 ? LaneOf(request) : new SecurityLaneSetting();

        return refusal.Length > 0
            ? (ExDataErr, string.Empty, refusal)
            : (0, validate ? Validation(lane) : Trial(lane, TextOf(request) ?? string.Empty), string.Empty);
    }

    /// <summary>What in the request keeps it from being read as asked — never answered as though it had been.</summary>
    private static string RequestRefusal(JsonElement request, bool validate) =>
        (request.ValueKind == JsonValueKind.Object, validate || TextOf(request) is not null, LaneIsReadable(request)) switch
        {
            (false, _, _) => "--check-security reads one JSON object on stdin — {\"text\": …, \"lane\": …}",
            (_, false, _) => "--check-security needs \"text\": the sample to try, a string (or --validate to check the patterns alone)",
            (_, _, false) => "--check-security reads \"lane\" as the lane setting, an object; leave it out for the shipped words",
            _ => string.Empty,
        };

    /// <summary>No lane, or one that is an object: a lane of any other shape is not silently replaced by the shipped one.</summary>
    private static bool LaneIsReadable(JsonElement request) =>
        request.ValueKind != JsonValueKind.Object || !request.TryGetProperty("lane", out var lane) || lane.ValueKind == JsonValueKind.Object;

    private static string? TextOf(JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && request.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;

    /// <summary>The lane being edited, read by the lane's own parser — or the shipped one when the request names none.</summary>
    private static SecurityLaneSetting LaneOf(JsonElement request) =>
        request.ValueKind == JsonValueKind.Object && request.TryGetProperty("lane", out var lane) && lane.ValueKind == JsonValueKind.Object
            ? SecurityLaneSetting.Parse(lane.GetRawText(), [])
            : new SecurityLaneSetting();

    /// <summary>What the lane makes of the sample: as one file of code, through the lane's own table.</summary>
    private static string Trial(SecurityLaneSetting lane, string text)
    {
        var files = SecuritySignals.Classify([new FileDiff("try-it", text, false)], lane.Table);

        return Written(json =>
        {
            // The signals a person reads; a card's own words answer as that card, in `cards`.
            Strings(json, "signals", files[0].Signals.Where(signal => !SecurityPrompt.IsOwnSignal(signal)));
            Strings(json, "cards", [.. lane.Prompts.Where(p => p.Refusal.Length == 0 && SecuritySignals.Triggered(p, files)).Select(p => p.Id)]);
            Refusals(json, lane.Table.Refused);
            Strings(json, "complaints", lane.Complaints);
            // An empty signal list from a detector that did not finish (too large, a pattern out of time) is not "none".
            json.WriteBoolean("detectionIncomplete", files[0].DetectionIncomplete);
        });
    }

    private static string Validation(SecurityLaneSetting lane) =>
        Written(json =>
        {
            Refusals(json, lane.Table.Refused);
            Strings(json, "complaints", lane.Complaints);
        });

    private static string Written(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Strings(Utf8JsonWriter json, string name, IEnumerable<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }
        json.WriteEndArray();
    }

    private static void Refusals(Utf8JsonWriter json, IReadOnlyList<PatternRefusal> refused)
    {
        json.WriteStartArray("refused");
        foreach (var one in refused)
        {
            json.WriteStartObject();
            json.WriteString("signal", one.Signal);
            json.WriteString("pattern", one.Pattern);
            json.WriteString("why", one.Why);
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }
}
