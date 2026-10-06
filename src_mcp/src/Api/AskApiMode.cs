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
    internal const int Unavailable = ApiRuntime.EndedBeforeAnAnswerExit; // 69, EX_UNAVAILABLE — read as "usage not captured"
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

    /// <summary>
    /// The longest one line of a stream may be. A chunk is a few hundred bytes; a line past this is a body with no line
    /// ends, refused rather than held (todo/PLAN_api_streaming.md).
    /// </summary>
    internal const int MaxStreamLineChars = 1024 * 1024;

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
        var model = flags.GetValueOrDefault("--model", string.Empty);
        // The module, from the row's dialect (a module name, or the row it was written with) and the
        // model — the registry is the one place a name becomes a type (ApiVendors).
        var module = ApiVendors.Resolve(flags.GetValueOrDefault("--dialect", ApiDialects.OpenAiName), model);

        if (Refusal(flags, vendor, endpoint, key, module) is { } refused)
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
            model,
            module!,
            flags.GetValueOrDefault("--prompt-file", string.Empty),
            flags.GetValueOrDefault("--schema-file", string.Empty),
            flags.GetValueOrDefault("--out", string.Empty),
            flags.GetValueOrDefault("--reasoning-effort", string.Empty),
            int.TryParse(flags.GetValueOrDefault("--max-tokens", ""), out var cap) && cap > 0 ? cap : 8192,
            deadline,
            flags.GetValueOrDefault("--conversation", string.Empty).Trim(),
            // Absent is on: the switch is spelled only when a row turned thinking OFF on a module that has one.
            ThinkingOn: !string.Equals(flags.GetValueOrDefault("--thinking", "on").Trim(), "off", StringComparison.OrdinalIgnoreCase),
            // Absent is off: a row asks for a stream only when a person switched it on (todo/PLAN_api_streaming.md).
            Stream: string.Equals(flags.GetValueOrDefault("--stream", "off").Trim(), "on", StringComparison.OrdinalIgnoreCase));

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
        IReadOnlyDictionary<string, string> flags, string vendor, string endpoint, string key, IApiVendor? module)
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

        return module is null
            ? $"{Mode}: no dialect '{flags.GetValueOrDefault("--dialect", "")}' — this build knows the modules "
                + $"{string.Join(", ", ApiVendors.Names)} and the rows {string.Join(", ", ApiDialects.Names)}"
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
        foreach (var (name, value) in ask.Vendor.Headers(ask.Conversation))
        {
            // What the module needs beyond the key — the conversation key in the header its row names,
            // which routes every turn of one reviewer's conversation to the server holding its prompt
            // cache (xAI, 2026-09-26 — without it, 1,152 cached tokens on each of three byte-identical
            // 64 KB prefixes). Nothing, for a module whose cache takes no key.
            message.Headers.TryAddWithoutValidation(name, value);
        }

        message.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // The body is STREAMED against a ceiling (BoundedBody), so the headers come back first — and the
        // client's Timeout stops covering the read, which is why the same deadline rides on a token.
        using var deadline = new CancellationTokenSource(ask.Deadline);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (ask.Stream && IsEventStream(response))
        {
            return await StreamedAsync(ask, response, deadline.Token, note, output);
        }

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
            return ask.Vendor.RequestBody(
                new ApiTurn(ask.Model, prompt, schema, LocalAsk.SeedFor(prompt), ask.ReasoningEffort, ask.MaxTokens, ask.ThinkingOn, ask.Stream));
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
        // The module says what the status MEANS; this switch is on that meaning, never on a vendor.
        switch (ask.Vendor.Classify(status, text))
        {
            case ApiOutcome.KeyRefused:
                // The body is NOT quoted: a refusal from a gateway can echo the request, and a sentence
                // about a bad key is complete without the vendor's wording of it.
                note($"the API refused the key for vendor '{ask.Row}' (HTTP {status}) — check the vault entry "
                     + $"under '{ask.Row}'; the response body is not shown");

                return Refused;

            case ApiOutcome.RateLimited:
                // The exact shape `RateLimit.Hit` reads: "HTTP 429" and "429 Too Many Requests" both
                // match its status-code pattern, with a non-zero exit. The retry hint is the vendor's
                // own Retry-After when it sent one, because that is the number the backoff wants.
                note($"HTTP {status} {ReasonPhrase(response)} from {ask.Endpoint}{RetryAfter(response)}: {Quoted(text)}");

                return RateLimited;

            case ApiOutcome.Failed:
                note($"the API at {ask.Endpoint} answered HTTP {status}: {Quoted(text)}");

                return Failed;
        }

        var answer = ask.Vendor.ReadAnswer(text);
        // The tokens go to stdout BEFORE the answer is judged: a 200 is a billed call whatever this shim
        // decides about its content, and a failed call's cost has to cross the process boundary the same
        // way an answered one's does (2026-09-26 — a reasoning-only answer at 16,382 output tokens was
        // reported as free). The shape is the one `ApiRuntime.ReadUsage` reads — safe here and only here,
        // because this mode never speaks the protocol. `tokensCached` rides along for the follow-up
        // turns of a feature review (D25): the vendor's own cached-prefix count, zero when it reported none.
        await output.WriteLineAsync(UsageLine(answer.Usage));

        return await KeptAsync(ask, answer, text, note);
    }

    /// <summary>A read answer, judged: no content, a cut fragment, or the answer written to the out file.</summary>
    private static async Task<int> KeptAsync(Ask ask, ChatAnswer answer, string text, Action<string> note)
    {
        if (answer.Content is null)
        {
            note($"the API at {ask.Endpoint} returned no message content{Thinking(answer)}: {Quoted(text)}");

            return Failed;
        }

        if (answer.WasCut)
        {
            // Not written to the out file: a fragment where the executor reads an answer is the defect
            // this branch exists for — `{"findings":[]}` cut before its findings is a clean review nobody
            // gave (the coordinator's acceptance case of 2026-09-26; measured on GLM-5.3 and Qwen3.8-max).
            note($"the answer from {ask.Endpoint} was cut at the token limit ({ask.Vendor.Dialect.MaxTokensField} {ask.Vendor.Dialect.CeilingFor(ask.MaxTokens)}): "
                 + $"{answer.Usage.TokensOut} tokens generated{Thinking(answer)}, {answer.Content.Length} characters of content arrived — "
                 + "raise the ceiling or bound the reasoning; the fragment was not kept");

            return Failed;
        }

        await File.WriteAllTextAsync(ask.OutFile, answer.Content);

        return Ok;
    }

    /// <summary>
    /// Whether a 400's ERROR field says the key is wrong — xAI's answer to a bad key (§9.11). The rule
    /// itself lives with the modules (<see cref="ApiClassification"/>); this name stays for the tests
    /// that have always asked here.
    /// </summary>
    internal static bool SaysTheKeyIsWrong(string body) => ApiClassification.SaysTheKeyIsWrong(body);

    /// <summary>
    /// The standard phrase of a temporary refusal — "Too Many Requests", "Service Unavailable", "Internal
    /// Server Error" — spelled here rather than read off the response, because HTTP/2 carries no reason
    /// phrase and the sentence is the one <c>RateLimit.Hit</c> reads (a 500 is retried on the vendor's
    /// own transient phrase, which the quoted body carries).
    /// </summary>
    private static string ReasonPhrase(HttpResponseMessage response) => response.StatusCode switch
    {
        HttpStatusCode.TooManyRequests => "Too Many Requests",
        HttpStatusCode.ServiceUnavailable => "Service Unavailable",
        HttpStatusCode.BadGateway => "Bad Gateway",
        HttpStatusCode.GatewayTimeout => "Gateway Timeout",
        _ => "Internal Server Error",
    };

    /// <summary>Whether the answer is a stream — decided by what was ANSWERED, not by what was asked: a gateway that
    /// ignores <c>stream</c> answers one JSON, which is read whole as always.</summary>
    private static bool IsEventStream(HttpResponseMessage response) =>
        response.IsSuccessStatusCode
        && string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A streamed answer (todo/PLAN_api_streaming.md, S4 and S5): read, assembled into the one answer shape, and its usage
    /// line printed FIRST however it ended — the last usage the stream carried, or "not captured", never a zero for a
    /// generation the vendor billed. Then the exit: the answer judged as any other; a failure the stream reported inside
    /// its 200, quoted so the retry rule reads the vendor's words; a stream that stopped before its answer finished.
    /// </summary>
    private static async Task<int> StreamedAsync(Ask ask, HttpResponseMessage response, CancellationToken token, Action<string> note, TextWriter output)
    {
        var read = await StreamedBody.ReadAsync(response.Content, MaxAnswerBytes, MaxStreamLineChars, token);
        var answer = ask.Vendor.ReadAnswer(read.Outcome.Completion);
        // `streamed` says a stream WAS read — a row's Check tells a stream it asked for and got from one it did not get.
        await output.WriteLineAsync(UsageLine(answer.Usage, streamed: true));

        return read.Stop != StreamStop.Ended ? Stopped(ask, read, note) : await EndedAsync(ask, answer, read, note);
    }

    /// <summary>A stream that ran to its end: the answer judged as any other, or the failure it carried inside its 200.</summary>
    private static async Task<int> EndedAsync(Ask ask, ChatAnswer answer, StreamRead read, Action<string> note)
    {
        return read.Outcome.End switch
        {
            StreamEnd.Failed => Said(note, $"the stream from {ask.Endpoint} reported a failure inside its HTTP 200: {Quoted(read.Outcome.ErrorText)}", Failed),
            StreamEnd.Broken => Said(note, $"the stream from {ask.Endpoint} ended before the answer finished ({Arrived(read.Outcome)})", Unavailable),
            _ => await KeptAsync(ask, answer, read.Outcome.Completion, note),
        };
    }

    /// <summary>A stream whose reading stopped early: a ceiling, a drop or the deadline — each said with how far it got.</summary>
    private static int Stopped(Ask ask, StreamRead read, Action<string> note) => read.Stop switch
    {
        StreamStop.Capped => Said(note, $"the API at {ask.Endpoint} streamed more than {MaxAnswerBytes / (1024 * 1024)} Mi characters of answer — refused, not read past the ceiling", Failed),
        StreamStop.LineTooLong => Said(note, $"the API at {ask.Endpoint} sent a stream line longer than {MaxStreamLineChars / (1024 * 1024)} Mi characters — not a stream; refused", Failed),
        StreamStop.TimedOut => Said(note, $"the API at {ask.Endpoint} did not finish in time — the {ask.Deadline.TotalSeconds:F0}s this reviewer was given ran out mid-stream ({Arrived(read.Outcome)}). "
            + "Give it more time (COAI_REVIEWER_TIMEOUT_MINUTES) or a smaller prompt (the Fast context).", Unavailable),
        _ => Said(note, $"the stream from {ask.Endpoint} ended before the answer finished — the connection dropped ({Arrived(read.Outcome)})", Unavailable),
    };

    /// <summary>How much had arrived: what tells a model that was still thinking from one that was writing.</summary>
    private static string Arrived(StreamOutcome outcome) =>
        $"{outcome.ContentChars} characters of answer and {outcome.ReasoningChars} of reasoning had arrived";

    private static int Said(Action<string> note, string text, int exit)
    {
        note(text);

        return exit;
    }

    /// <summary>
    /// The one line on stdout: the raw tokens the vendor reported, and no money.
    /// </summary>
    /// <remarks>
    /// The cost is worked out in the PARENT (epic 3's code round, #23) — <see cref="ApiRuntime.ReadUsage"/>
    /// prices this line from the row's <c>TokenPrice</c>, an answered call and a failed one alike — so no
    /// rate rides on a command line and one arithmetic serves every launch. <c>tokensOut</c> is everything
    /// generated (reasoning included, wherever the vendor filed it); <c>tokensReasoning</c> is the
    /// reasoning share for the record.
    /// </remarks>
    /// <param name="usage">What the vendor reported the call consumed.</param>
    /// <param name="streamed">The answer was READ as a stream (todo/PLAN_api_streaming.md, Story C); absent otherwise —
    /// a gateway that answered one JSON, and an older coai-mcp that ignored <c>--stream on</c>, both write none.</param>
    internal static string UsageLine(Usage usage, bool streamed = false) =>
        "{" + $"\"tokensIn\":{usage.TokensIn},\"tokensOut\":{usage.TokensOut},\"tokensCached\":{usage.TokensCached},\"tokensReasoning\":{usage.TokensReasoning}"
        // The vendor answered and said nothing about what the call consumed: the parent records it as unknown.
        + (usage.NotCaptured ? ",\"notCaptured\":true" : string.Empty)
        + (streamed ? ",\"streamed\":true" : string.Empty) + "}";

    /// <summary>" (N reasoning tokens)" when the vendor reported any — the number a person needs to size the ceiling.</summary>
    private static string Thinking(ChatAnswer answer) =>
        answer.ReasoningTokens > 0 ? $" ({answer.ReasoningTokens} reasoning tokens)" : string.Empty;

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
    /// <param name="Row">The vendor ROW's id — what names the vault entry in a refusal.</param>
    /// <param name="Vendor">The module that spells the request, reads the answer and classifies a refusal.</param>
    /// <param name="Conversation">The reviewer's conversation key for the module's cache-routing header; empty sends none.</param>
    /// <param name="ThinkingOn">Whether the model thinks; off only on a module with a switch, and only when a row said so.</param>
    /// <param name="Stream">Whether the answer is asked for as a stream (todo/PLAN_api_streaming.md).</param>
    private sealed record Ask(
        string Row,
        string Endpoint,
        string Model,
        IApiVendor Vendor,
        string PromptFile,
        string SchemaFile,
        string OutFile,
        string ReasoningEffort,
        int MaxTokens,
        TimeSpan Deadline,
        string Conversation = "",
        bool ThinkingOn = true,
        bool Stream = false);
}
