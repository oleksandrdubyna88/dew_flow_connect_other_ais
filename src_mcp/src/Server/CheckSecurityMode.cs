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

    internal static async Task<int> RunAsync(string[] args)
    {
        var stdin = await Console.In.ReadToEndAsync();
        var (code, output, error) = Answer(stdin, Array.IndexOf(args, "--validate") >= 0);
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
            return (ExDataErr, string.Empty, $"the request is {stdin.Length} characters; --check-security reads at most {MaxRequestCharacters}");
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
        var text = TextOf(request);
        if (!validate && text is null)
        {
            return (ExDataErr, string.Empty, "--check-security needs \"text\": the sample to try, a string (or --validate to check the patterns alone)");
        }
        var lane = LaneOf(request);

        return (0, validate ? Validation(lane) : Trial(lane, text ?? string.Empty), string.Empty);
    }

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
