using System.Diagnostics;
using System.Net;
using System.Text;
using CoaiMcp.Core.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Api;

/// <summary>
/// <c>coai-mcp --ask-api</c> — one completion against a hosted OpenAI-compatible endpoint, with a
/// bearer key from the environment, written where the executor looks.
/// </summary>
/// <remarks>
/// <para>The twin of <c>--ask-local</c>: <see cref="ApiRuntime"/> builds the command line and this
/// process does the HTTP, so the round's own code never learns that a reviewer was an API. It differs
/// in three things and each is the point (PLAN_feature_review.md §4.10): an <c>Authorization</c>
/// header, read from <see cref="ApiRuntime.KeyVariable"/> and never from argv; NO engine lease — a
/// vendor's fleet is bounded by its rate limit, not by a card on this machine; and a body spelled by
/// the named <see cref="ApiDialect"/> rather than the local one, because a hosted reasoning model
/// refuses fields a local engine wants.</para>
/// <para><b>Exit codes are the contract.</b> 0 answered; 65 a request this shim will not send —
/// missing arguments, no key, an unknown dialect, an unreadable schema — and <b>never 64</b>, which
/// means "this binary has never heard of that mode"; 69 the endpoint could not be reached or did not
/// answer in time; 70 it answered with something that is not a review; 75 a 429 or a 503, with a
/// sentence <see cref="RateLimit.Hit"/> recognises — that matcher reads the TEXT with a non-zero
/// exit, never the code alone; 77 a 401 or a 403 — or a 400 whose error says the key is wrong, which is
/// how xAI refuses one (§9.11) — naming the vendor and NOT echoing the body.</para>
/// <para><b>No vendor text reaches stderr unredacted.</b> A refusal body can echo the request's
/// headers back — some gateways do — so everything quoted from a response goes through
/// <see cref="Redaction.SafeText"/> and a length cap first, and the 401/403 body is not quoted at all.</para>
/// </remarks>
internal static class AskApiMode
{
    internal const int Ok = 0;
    internal const int BadRequest = 65; // EX_DATAERR — a known mode refusing its arguments; never 64
    internal const int Unavailable = 69; // EX_UNAVAILABLE
    internal const int Failed = 70; // EX_SOFTWARE
    internal const int RateLimited = 75; // EX_TEMPFAIL
    internal const int Refused = 77; // EX_NOPERM

    /// <summary>How much of a vendor's answer may be quoted on stderr, after redaction.</summary>
    internal const int QuoteLimit = 300;

    /// <summary>
    /// The most of an answer this mode will read. A review answer is tens of KB; 8 MiB is generous, and
    /// anything past it is an endpoint ignoring the token ceiling or an error page, read no further.
    /// </summary>
    internal const int MaxAnswerBytes = 8 * 1024 * 1024;

    private const string Mode = "--ask-api";

    internal static async Task<int> RunAsync(
        string[] args,
        Action<string> note,
        TextWriter output,
        Func<string, string?> env,
        HttpMessageHandler? handler = null)
    {
        var flags = Program.Flags(args);
        var vendor = flags.GetValueOrDefault("--vendor", "api");
        var endpoint = flags.GetValueOrDefault("--endpoint", string.Empty);
        var key = env(ApiRuntime.KeyVariable) ?? string.Empty;
        var dialect = ApiDialects.Named(flags.GetValueOrDefault("--dialect", ApiDialects.OpenAiName));

        if (Refusal(flags, vendor, endpoint, key, dialect) is { } refused)
        {
            note(refused);

            return BadRequest;
        }

        var deadline = int.TryParse(flags.GetValueOrDefault("--timeout-seconds", ""), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromMinutes(10);
        var waited = Stopwatch.StartNew();
        var request = new Ask(
            vendor,
            LocalRuntime.OpenAiBaseOf(endpoint),
            flags.GetValueOrDefault("--model", string.Empty),
            dialect!,
            flags.GetValueOrDefault("--prompt-file", string.Empty),
            flags.GetValueOrDefault("--schema-file", string.Empty),
            flags.GetValueOrDefault("--out", string.Empty),
            flags.GetValueOrDefault("--reasoning-effort", string.Empty),
            int.TryParse(flags.GetValueOrDefault("--max-tokens", ""), out var cap) && cap > 0 ? cap : 8192,
            deadline,
            TokenPrice.FromFlags(flags));

        try
        {
            return await AskAsync(request, key, note, output, handler);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            note(Unreachable(request, ex, waited.Elapsed));

            return Unavailable;
        }
    }

    /// <summary>Why this shim will not even try — or null when it will.</summary>
    /// <remarks>
    /// Every one of these is 65: the mode is known and the request is wrong. The key's absence is
    /// refused HERE, before any socket opens, because an unauthenticated request would spend a network
    /// round trip to be told 401 — and the sentence names the vault entry, which is the cure.
    /// </remarks>
    internal static string? Refusal(
        IReadOnlyDictionary<string, string> flags, string vendor, string endpoint, string key, ApiDialect? dialect)
    {
        if (flags.GetValueOrDefault("--prompt-file", "").Length == 0 || flags.GetValueOrDefault("--out", "").Length == 0)
        {
            return $"{Mode} needs --prompt-file and --out";
        }

        if (!IsHttpUrl(endpoint))
        {
            return $"{Mode} needs --endpoint <the OpenAI-compatible base URL, e.g. https://api.x.ai/v1>"
                + (endpoint.Length == 0 ? string.Empty : $" — '{endpoint}' is not an http(s) URL");
        }

        if (key.Length == 0)
        {
            return $"no key for vendor '{vendor}': {ApiRuntime.KeyVariable} is not set in this process's environment — "
                + $"the vault entry under '{vendor}' is where it comes from, and the round puts it there";
        }

        return dialect is null
            ? $"{Mode}: no dialect '{flags.GetValueOrDefault("--dialect", "")}' — this build knows: {string.Join(", ", ApiDialects.Names)}"
            : null;
    }

    private static async Task<int> AskAsync(Ask ask, string key, Action<string> note, TextWriter output, HttpMessageHandler? handler)
    {
        if (await BodyAsync(ask, note) is not { } body)
        {
            return BadRequest;
        }

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = ask.Deadline;
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ask.Endpoint.TrimEnd('/')}/chat/completions");
        // The key: a header on THIS request, from the environment, and nowhere else. Not a default
        // header on the client, so a redirect elsewhere cannot carry it along.
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // The body is STREAMED against a ceiling (BoundedBody), so the headers come back first — and the
        // client's Timeout stops covering the read, which is why the same deadline rides on a token.
        using var deadline = new CancellationTokenSource(ask.Deadline);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (await BoundedBody.ReadAsync(response.Content, MaxAnswerBytes, deadline.Token) is not { } text)
        {
            note($"the API at {ask.Endpoint} answered more than {MaxAnswerBytes / (1024 * 1024)} MiB — refused, not read past the ceiling");

            return Failed;
        }

        return await AnsweredAsync(ask, response, text, note, output);
    }

    /// <summary>The request body, or null after saying why there is none.</summary>
    private static async Task<string?> BodyAsync(Ask ask, Action<string> note)
    {
        if (ask.SchemaFile.Length == 0 || !File.Exists(ask.SchemaFile))
        {
            // Refused, not substituted: an unconstrained request is answered with an invented shape
            // after a full generation has been paid for — the same rule `--ask-local` follows.
            note($"{Mode} needs a finding schema and none was at '{ask.SchemaFile}', so no request was sent");

            return null;
        }

        var prompt = await File.ReadAllTextAsync(ask.PromptFile);
        var schema = await File.ReadAllTextAsync(ask.SchemaFile);
        try
        {
            return ChatRequest.Body(
                ask.Dialect, ask.Model, prompt, schema, LocalAsk.SeedFor(prompt), ask.ReasoningEffort, ask.MaxTokens);
        }
        catch (System.Text.Json.JsonException ex)
        {
            note($"the finding schema at {ask.SchemaFile} does not parse, so no request was sent: {ex.Message}");

            return null;
        }
    }

    /// <summary>What the status code means, and what is said about it.</summary>
    private static async Task<int> AnsweredAsync(
        Ask ask, HttpResponseMessage response, string text, Action<string> note, TextWriter output)
    {
        var status = (int)response.StatusCode;
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
            case HttpStatusCode.BadRequest when SaysTheKeyIsWrong(text):
                // The body is NOT quoted: a refusal from a gateway can echo the request, and a sentence
                // about a bad key is complete without the vendor's wording of it.
                note($"the API refused the key for vendor '{ask.Vendor}' (HTTP {status}) — check the vault entry "
                     + $"under '{ask.Vendor}'; the response body is not shown");

                return Refused;

            case HttpStatusCode.TooManyRequests:
                // The exact shape `RateLimit.Hit` reads: "HTTP 429" and "429 Too Many Requests" both
                // match its status-code pattern, with a non-zero exit. The retry hint is the vendor's
                // own Retry-After when it sent one, because that is the number the backoff wants.
                note($"HTTP 429 Too Many Requests from {ask.Endpoint}{RetryAfter(response)}: {Quoted(text)}");

                return RateLimited;

            case HttpStatusCode.ServiceUnavailable:
                note($"HTTP 503 Service Unavailable from {ask.Endpoint}{RetryAfter(response)}: {Quoted(text)}");

                return RateLimited;
        }

        if (!response.IsSuccessStatusCode)
        {
            note($"the API at {ask.Endpoint} answered HTTP {status}: {Quoted(text)}");

            return Failed;
        }

        var (answer, usage) = LocalAsk.ReadResponse(text);
        if (answer is null)
        {
            note($"the API at {ask.Endpoint} returned no message content: {Quoted(text)}");

            return Failed;
        }

        await File.WriteAllTextAsync(ask.OutFile, answer);
        // The tokens on stdout, in the shape `ApiRuntime.ReadUsage` reads — safe here and only here,
        // because this mode never speaks the protocol.
        // `tokensCached` rides along for the follow-up turns of a feature review (D25): the vendor's
        // own cached-prefix count, zero when it reported none.
        await output.WriteLineAsync(UsageLine(usage, ask.Price));

        return Ok;
    }

    /// <summary>
    /// Whether a 400's ERROR field says the key is wrong — xAI's answer to a bad key (§9.11).
    /// </summary>
    /// <remarks>
    /// <para>Measured, not documented: S0.5's probe sent a key that was wrong on purpose on 2026-09-26 and
    /// xAI answered <c>400</c> with <c>"error": "Incorrect API key provided: …"</c>, where OpenAI answers
    /// 401. Read as a plain 400, a revoked Grok key was reported as a malformed request and never sent
    /// anybody to the vault.</para>
    /// <para><b>General, not xAI-only</b>, because the test is narrow enough to be safe everywhere: only
    /// the body's own error field is read — <c>error</c> as a string, or <c>error.message</c> /
    /// <c>error.code</c> — never the whole text. A 400 that echoes the request back in some other field
    /// (and a review of this very file carries the phrase) stays a failed request.</para>
    /// </remarks>
    internal static bool SaysTheKeyIsWrong(string body)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error))
            {
                return false;
            }

            return ErrorTexts(error).Any(text => WrongKeyPhrases.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)));
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>What a vendor says when the key itself is wrong — each read off a real answer or a vendor's error code.</summary>
    private static readonly string[] WrongKeyPhrases = ["incorrect api key", "invalid api key", "invalid_api_key"];

    /// <summary>The error field's own words: the string itself, or an object's message and code.</summary>
    private static IEnumerable<string> ErrorTexts(System.Text.Json.JsonElement error) => error.ValueKind switch
    {
        System.Text.Json.JsonValueKind.String => [error.GetString() ?? string.Empty],
        System.Text.Json.JsonValueKind.Object =>
            ((string[])["message", "code"])
                .Where(name => error.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String)
                .Select(name => error.GetProperty(name).GetString() ?? string.Empty),
        _ => [],
    };

    /// <summary>
    /// The one line on stdout: the tokens, and — when the row carried a price — what this turn cost.
    /// </summary>
    /// <remarks>
    /// The cost is worked out HERE, per turn, because this is the only place that holds one request's
    /// tokens and the row's rates together (S3.7): a long-context tier is decided by one request's
    /// prompt, and a feature reviewer's conversation is several requests. No price, no <c>costUsd</c> —
    /// <see cref="ApiRuntime.ReadUsage"/> reads its absence as "no price set", never as $0.
    /// </remarks>
    internal static string UsageLine(Usage usage, TokenPrice price)
    {
        var tokens = $"\"tokensIn\":{usage.TokensIn},\"tokensOut\":{usage.TokensOut},\"tokensCached\":{usage.TokensCached}";

        return price.CostOf(usage) is { } usd
            ? "{" + tokens + ",\"costUsd\":" + usd.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) + "}"
            : "{" + tokens + "}";
    }

    /// <summary>" — try again in Ns" when the vendor said how long, or nothing.</summary>
    private static string RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta
            ? $" — try again in {Math.Ceiling(delta.TotalSeconds):0}s"
            : string.Empty;

    /// <summary>
    /// A vendor's text, fit to be written down: one line, redacted, capped.
    /// </summary>
    /// <remarks>
    /// Redacted BEFORE it is capped, through the same pass the notices file uses — so a bearer echo or
    /// a key-shaped token in the body reads <c>[redacted]</c> whatever else the vendor wrote around it.
    /// </remarks>
    internal static string Quoted(string text) =>
        Redaction.SafeText(text.Replace('\r', ' ').Replace('\n', ' ').Trim(), QuoteLimit);

    private static string Unreachable(Ask ask, Exception ex, TimeSpan waited) => ex switch
    {
        TaskCanceledException when waited >= ask.Deadline - TimeSpan.FromSeconds(1) =>
            $"the API at {ask.Endpoint} did not finish in time — it was still working after "
                + $"{waited.TotalSeconds:F0}s of the {ask.Deadline.TotalSeconds:F0}s this reviewer was given. "
                + "Give it more time (COAI_REVIEWER_TIMEOUT_MINUTES) or a smaller prompt (the Fast context).",
        TaskCanceledException =>
            $"this reviewer was cancelled after {waited.TotalSeconds:F0}s of the {ask.Deadline.TotalSeconds:F0}s "
                + "it was given — the round ended, or the client went away.",
        _ => $"the API at {ask.Endpoint} could not be reached: {Redaction.SafeText(ex.Message, QuoteLimit)}",
    };

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>One request, fully described — everything but the key, which is never stored on a record.</summary>
    private sealed record Ask(
        string Vendor,
        string Endpoint,
        string Model,
        ApiDialect Dialect,
        string PromptFile,
        string SchemaFile,
        string OutFile,
        string ReasoningEffort,
        int MaxTokens,
        TimeSpan Deadline,
        TokenPrice Price);
}
