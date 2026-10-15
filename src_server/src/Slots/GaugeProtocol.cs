using System.Globalization;
using System.Text.Json;

namespace CoaiServer;

/// <summary>What the reader's stdout amounted to — a closed set, never the text.</summary>
public abstract record GaugeAnswer
{
    private GaugeAnswer()
    {
    }

    /// <summary>The read answered, and the answer carried at least one limit.</summary>
    public sealed record Read(GaugeReadingDto Reading) : GaugeAnswer;

    /// <summary>An authentication error, a refresh request or a login request: the capture suspends, for this <see cref="GaugeCodes"/> code.</summary>
    public sealed record Suspend(string Code) : GaugeAnswer;

    /// <summary>No reading, for this code: an error that was not about authentication, an unreadable result, no answer at all.</summary>
    public sealed record Failed(string Code) : GaugeAnswer;
}

/// <summary>
/// The three lines a gauge read sends to <c>codex app-server</c>, and the reading of what comes back — numbers out, the
/// envelope dropped, never a message kept (research/RESULTS_vendor_quota_gauges.md §1 procedure A, §2.2, §2.6).
/// </summary>
/// <remarks>
/// <para>The request lines are the measured ones: <c>initialize</c> with a client name, the <c>initialized</c>
/// notification, then <c>account/rateLimits/read</c> as request 2. <c>account/rateLimitResetCredit/consume</c> and
/// every <c>account/login/*</c> method are never sent — a read reads.</para>
/// <para>A line from the server is one of three things: the answer to request 2, a request FROM the server (it carries
/// both an <c>id</c> and a <c>method</c> — <c>chatgptAuthTokens/refresh</c> is the one the design must never answer),
/// or a notification (<c>account/updated</c>, <c>remoteControl/status/changed</c>, which names the machine and is why
/// no line is ever logged). The reader stops at the first of the first two.</para>
/// </remarks>
public static class GaugeProtocol
{
    /// <summary>The id of the one request whose answer is the reading.</summary>
    public const int ReadId = 2;

    private const string RefreshMethod = "chatgptAuthTokens/refresh";

    private const string LoginMethodPrefix = "account/login";

    /// <summary>The stdin a reader is handed: initialize, initialized, the read — one JSON-RPC line each.</summary>
    public static string Requests(string clientVersion) =>
        "{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"coai-gauge\",\"version\":\"" + clientVersion + "\"}}}\n"
        + "{\"method\":\"initialized\"}\n"
        + "{\"id\":" + ReadId + ",\"method\":\"account/rateLimits/read\",\"params\":{}}\n";

    /// <summary>Whether the stdout so far holds what ends the reader: the answer to the read, or a request from the server.</summary>
    public static bool Answered(string stdout) =>
        Lines(stdout).Any(line => line.Id == ReadId || line.IsServerRequest);

    /// <summary>What the reader's stdout amounted to.</summary>
    public static GaugeAnswer Parse(string stdout, DateTimeOffset observedUtc)
    {
        foreach (var line in Lines(stdout))
        {
            if (line.IsServerRequest)
            {
                return RequestFromServer(line.Method);
            }

            if (line.Id == ReadId)
            {
                return AnswerTo(line.Root, observedUtc);
            }
        }

        return new GaugeAnswer.Failed(GaugeCodes.NoAnswer);
    }

    private static GaugeAnswer RequestFromServer(string method) => method switch
    {
        RefreshMethod => new GaugeAnswer.Suspend(GaugeCodes.RefreshRequested),
        _ when method.StartsWith(LoginMethodPrefix, StringComparison.Ordinal) => new GaugeAnswer.Suspend(GaugeCodes.LoginRequested),
        _ => new GaugeAnswer.Failed(GaugeCodes.UnexpectedRequest),
    };

    private static GaugeAnswer AnswerTo(JsonElement root, DateTimeOffset observedUtc)
    {
        if (root.TryGetProperty("error", out var error))
        {
            return LooksLikeAuth(error) ? new GaugeAnswer.Suspend(GaugeCodes.AuthRefused) : new GaugeAnswer.Failed(GaugeCodes.ReadError);
        }

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            return new GaugeAnswer.Failed(GaugeCodes.Unreadable);
        }

        var reading = ReadingOf(result, observedUtc);

        return reading.Windows.Count == 0 ? new GaugeAnswer.Failed(GaugeCodes.Unreadable) : new GaugeAnswer.Read(reading);
    }

    /// <summary>Whether an error is about authentication — judged by a few words, and the words are never kept.</summary>
    private static bool LooksLikeAuth(JsonElement error)
    {
        var message = error.TryGetProperty("message", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;

        return new[] { "auth", "unauthori", "401", "sign in", "login", "token" }
            .Any(word => message.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The reading: every limit's windows (<c>rateLimitsByLimitId</c>, else <c>rateLimits</c>), the preferred limit's words, the credits.</summary>
    private static GaugeReadingDto ReadingOf(JsonElement result, DateTimeOffset observedUtc)
    {
        var preferred = Property(result, "rateLimits");
        var limits = Property(result, "rateLimitsByLimitId") is { ValueKind: JsonValueKind.Object } map
            ? map.EnumerateObject().Select(entry => entry.Value).ToList()
            : preferred is { } one ? [one] : [];
        // The preferred limit's windows first, so `Primary`/`Secondary` name what codex itself names.
        var ordered = preferred is { } first ? limits.OrderBy(limit => SameLimit(limit, first) ? 0 : 1).ToList() : limits;

        return new GaugeReadingDto(
            observedUtc,
            Text(preferred, "planType"),
            Text(preferred, "rateLimitReachedType"),
            Property(result, "ordinaryUsageAllowed") is { ValueKind: JsonValueKind.True or JsonValueKind.False } allowed ? allowed.GetBoolean() : null,
            Property(Property(result, "rateLimitResetCredits"), "availableCount") is { ValueKind: JsonValueKind.Number } count ? count.GetInt32() : null,
            [.. ordered.SelectMany(WindowsOf)]);
    }

    private static bool SameLimit(JsonElement a, JsonElement b) => LimitIdOf(a) == LimitIdOf(b);

    private static string LimitIdOf(JsonElement limit) =>
        Text(limit, "limitId") is { Length: > 0 } id ? id : "codex";

    private static IEnumerable<GaugeWindowDto> WindowsOf(JsonElement limit)
    {
        var id = LimitIdOf(limit);
        foreach (var kind in new[] { "primary", "secondary" })
        {
            if (Property(limit, kind) is { ValueKind: JsonValueKind.Object } window && Property(window, "usedPercent") is { ValueKind: JsonValueKind.Number } used)
            {
                yield return new GaugeWindowDto(id, kind, used.GetDouble(), Minutes(window), ResetsAt(window));
            }
        }
    }

    private static long? Minutes(JsonElement window) =>
        Property(window, "windowDurationMins") is { ValueKind: JsonValueKind.Number } minutes ? minutes.GetInt64() : null;

    private static string ResetsAt(JsonElement window) =>
        Property(window, "resetsAt") is { ValueKind: JsonValueKind.Number } at
            ? DateTimeOffset.FromUnixTimeSeconds(at.GetInt64()).UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            : string.Empty;

    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } found && found.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    private static string Text(JsonElement? element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } text ? text.GetString() ?? string.Empty : string.Empty;

    /// <summary>One line of the reader's stdout that parsed as a JSON object: its id, its method, and the object itself.</summary>
    private sealed record Line(int? Id, string Method, JsonElement Root)
    {
        public bool IsServerRequest => Id is not null && Method.Length > 0;
    }

    private static IEnumerable<Line> Lines(string stdout)
    {
        foreach (var raw in stdout.Split('\n'))
        {
            if (ParseLine(raw.Trim()) is { } line)
            {
                yield return line;
            }
        }
    }

    /// <summary>The line's id when it is an Int32; any other id (fractional, out of range, not a number) is no id.</summary>
    private static int? IdOf(JsonElement root) =>
        root.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.Number && idValue.TryGetInt32(out var id) ? id : null;

    private static Line? ParseLine(string text)
    {
        if (!text.StartsWith('{'))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement.Clone();

            return new Line(IdOf(root), Text(root, "method"), root);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
