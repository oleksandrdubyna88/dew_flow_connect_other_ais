using System.Net;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;

namespace CoaiMcp.Api;

/// <summary>
/// <c>coai-mcp --probe-api --vendor &lt;id&gt; [--model &lt;id&gt;] [--endpoint &lt;base&gt;]
/// [--dialect &lt;name&gt;] [--timeout-seconds 60]</c> — the measurement behind a vendor dialect.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> A dialect row in <c>shared/api-dialects.json</c> is written from what
/// a vendor ANSWERED, never from its documentation (PLAN_feature_review.md §4.10, §6 S0.5). This mode
/// is the product's own key path used as the instrument: it reads the vault exactly as
/// <see cref="KeyVault.ReadAsync"/> does — <c>COAI_CREDS_KEY</c> from its own environment — runs
/// <c>GET /models</c> and then the S0.5 request matrix against the row's endpoint, and prints what
/// the endpoint said in a shape a person can copy into the table.</para>
/// <para><b>What it prints is an ALLOWLIST</b>, decided on the plan round: status codes, model ids,
/// which request field was refused, token and cached-token counts, cost — and any vendor error text
/// only after <see cref="Redaction.SafeText"/> and a length cap. Nothing else the vendor sent is
/// echoed, and the key is never written anywhere. <c>ProbeApiModeTests</c> holds stdout and stderr
/// to that with a stub that echoes the request's <c>Authorization</c> back.</para>
/// <para><b>Each HTTP call has its own timeout</b>, so one hung request cannot spend the whole probe;
/// and the probe starts no child of its own — the vault read is the one process it runs, and that is
/// the same <c>creds config</c> every other mode runs.</para>
/// <para><b>Exit codes.</b> 0 the probe ran — an endpoint's refusals are RESULTS, on stdout; 65 bad
/// arguments (never 64: the mode is known); 78 the vault could not be read, or holds no key under
/// this vendor, with the vault's own reason.</para>
/// </remarks>
internal static class ProbeApiMode
{
    internal const int Ok = 0;
    internal const int BadRequest = 65;
    internal const int NoVault = 78; // EX_CONFIG

    /// <summary>How much of a vendor's error text is printed, after redaction.</summary>
    internal const int QuoteLimit = 300;

    /// <summary>The request fields a refusal is read for. Names only — the vendor's sentence is redacted and capped separately.</summary>
    internal static readonly string[] KnownFields =
    [
        "frequency_penalty", "presence_penalty", "temperature", "seed", "max_completion_tokens", "max_tokens",
        "reasoning_effort", "response_format", "json_schema", "json_object", "strict", "stop",
    ];

    /// <summary>The effort values every candidate model is asked with, one request each.</summary>
    internal static readonly string[] Efforts = ["low", "medium", "high"];

    /// <summary>A key that is wrong ON PURPOSE, so the 401 shape is observed rather than assumed.</summary>
    private const string WrongKey = "invalid-key-on-purpose";

    /// <summary>The prompt every matrix request carries — review-sized on purpose, so the cost row means something.</summary>
    private static readonly string Prompt =
        "You are reviewing a small change. Answer ONLY in the JSON schema you were given.\n\n"
        + "```diff\n"
        + string.Concat(Enumerable.Range(1, 40).Select(i => $"+    var value{i} = Compute({i}); // line {i} of a sample diff\n"))
        + "```\n\nReport at most two findings, or none.";

    internal static async Task<int> RunAsync(
        string[] args,
        Action<string> note,
        TextWriter output,
        Func<string, string?> env,
        IProcessLauncher launcher,
        HttpMessageHandler? handler = null)
    {
        var flags = Program.Flags(args);
        var vendor = flags.GetValueOrDefault("--vendor", string.Empty).Trim().ToLowerInvariant();
        if (vendor.Length == 0)
        {
            note("--probe-api needs --vendor <id> — the reviewer row whose key and endpoint to probe; "
                 + "[--model <id>] [--endpoint <base>] [--dialect <name>] [--timeout-seconds 60]");

            return BadRequest;
        }

        var row = RowFor(vendor, env, note);
        var endpoint = flags.GetValueOrDefault("--endpoint", row?.BaseUrl ?? string.Empty);
        if (!IsHttpUrl(endpoint))
        {
            note(row is null
                ? $"no reviewer '{vendor}' in this machine's settings — pass --endpoint <base URL> to probe one anyway"
                : $"reviewer '{vendor}' has no usable endpoint ('{endpoint}') — pass --endpoint <base URL>");

            return BadRequest;
        }

        var dialect = ApiDialects.Named(flags.GetValueOrDefault("--dialect", DialectOf(row)));
        if (dialect is null)
        {
            note($"--probe-api: no dialect '{flags.GetValueOrDefault("--dialect", "")}' — this build knows: {string.Join(", ", ApiDialects.Names)}");

            return BadRequest;
        }

        var keys = await new KeyVault(launcher).ReadAsync(env(KeyVault.KeyVariable));
        if (!keys.Available)
        {
            note($"the vault could not be read: {keys.Unavailability}");

            return NoVault;
        }

        if (keys.Keys.GetValueOrDefault(vendor, string.Empty) is not { Length: > 0 } key)
        {
            note($"the vault holds no key under '{vendor}' — add one to the creds config entry under that name");

            return NoVault;
        }

        var perCall = TimeSpan.FromSeconds(
            int.TryParse(flags.GetValueOrDefault("--timeout-seconds", ""), out var seconds) && seconds > 0 ? seconds : 60);
        var probe = new Probe(vendor, LocalRuntime.OpenAiBaseOf(endpoint), flags.GetValueOrDefault("--model", row?.Model ?? string.Empty), dialect, perCall);

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        // No client-wide timeout: each call carries its own, below.
        http.Timeout = Timeout.InfiniteTimeSpan;
        var report = await ProbeAsync(probe, key, http, note);
        await output.WriteLineAsync(Render(probe, report));

        return Ok;
    }

    /// <summary>The configured row, read the way every other one-shot reads settings — file under environment.</summary>
    private static ProviderSettings? RowFor(string vendor, Func<string, string?> env, Action<string> note)
    {
        var configuration = SettingsFile.Layer(SettingsFile.DataDirFrom(env).Path, env, note);
        var settings = PanelSettings.FromEnvironment(configuration);

        return settings.Providers.FirstOrDefault(p => string.Equals(p.Provider, vendor, StringComparison.OrdinalIgnoreCase));
    }

    private static string DialectOf(ProviderSettings? row) =>
        row is { Dialect.Length: > 0 } ? row.Dialect : ApiDialects.OpenAiName;

    private static async Task<Report> ProbeAsync(Probe probe, string key, HttpClient http, Action<string> note)
    {
        var models = await ModelsAsync(probe, key, http);
        note($"GET {probe.Endpoint}/models answered {models.Status} with {models.Ids.Count} model id(s)");

        if (probe.Model.Length == 0)
        {
            return new Report(models, [],
                ["no model named — pass --model <id> to run the request matrix; the ids above are what this key can call"]);
        }

        var requests = new List<RequestResult>();
        foreach (var variant in Variants(probe))
        {
            requests.Add(await CompletionAsync(probe, variant, key, http));
            note($"{probe.Model} · {variant.Name}: HTTP {requests[^1].Status}");
        }

        return new Report(models, requests,
        [
            "a 429 is not provoked on purpose: a burst against a paid key is not something a probe does unasked — "
                + "record one from a real round, or run the matrix twice in quick succession",
            "cost is null: an OpenAI-compatible response carries no price; multiply the token counts by the vendor's rate",
        ]);
    }

    /// <summary>The S0.5 matrix, as one list of named body variants.</summary>
    /// <remarks>
    /// Every variant is the dialect with ONE thing changed, so a refusal names the thing. The bare
    /// <c>json_schema</c> request comes first and again last — the second turn with the same prefix
    /// is what a vendor's cached-token count is read from. The wrong-key request is the same body
    /// with a key that is wrong on purpose, so the 401 text is observed rather than assumed.
    /// </remarks>
    internal static IReadOnlyList<Variant> Variants(Probe probe)
    {
        var d = probe.Dialect;
        var variants = new List<Variant>
        {
            new("json_schema", d, WrongKey: false),
            new("json_object", d with { ResponseFormat = "json_object" }, false),
            new("temperature", d with { Temperature = 0 }, false),
            new("seed", d with { Seed = true }, false),
            new("frequency_penalty", d with { FrequencyPenalty = 0.2 }, false),
        };
        variants.AddRange(Efforts.Select(effort =>
            new Variant($"reasoning_effort={effort}", d with { ReasoningEffortMap = new Dictionary<string, string>() }, false, effort)));
        variants.Add(new("second_turn", d, false));
        variants.Add(new("wrong_key", d, WrongKey: true));

        return variants;
    }

    private static async Task<ModelsResult> ModelsAsync(Probe probe, string key, HttpClient http)
    {
        var (status, text) = await SendAsync(http, HttpMethod.Get, $"{probe.Endpoint}/models", key, body: null, probe.PerCall);
        if (status is < 200 or > 299)
        {
            return new ModelsResult(status, [], Quoted(text));
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var ids = document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data.EnumerateArray()
                    .Where(m => m.ValueKind == JsonValueKind.Object && m.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    .Select(m => m.GetProperty("id").GetString() ?? string.Empty)
                    .Where(id => id.Length > 0)
                    .Take(200)
                    .ToList()
                : [];

            return new ModelsResult(status, ids, string.Empty);
        }
        catch (JsonException)
        {
            return new ModelsResult(status, [], "the /models answer is not JSON");
        }
    }

    private static async Task<RequestResult> CompletionAsync(Probe probe, Variant variant, string key, HttpClient http)
    {
        var body = ChatRequest.Body(variant.Dialect, probe.Model, Prompt, FindingSchema.Json, LocalAsk.SeedFor(Prompt), variant.Effort, 2048);
        var (status, text) = await SendAsync(
            http, HttpMethod.Post, $"{probe.Endpoint}/chat/completions", variant.WrongKey ? WrongKey : key, body, probe.PerCall);
        var ok = status is >= 200 and <= 299;
        var quoted = ok ? string.Empty : Quoted(text);

        return new RequestResult(
            variant.Name,
            status,
            ok ? string.Empty : RefusedField(quoted),
            quoted,
            ok ? Usage(text) : new Tokens(0, 0, 0));
    }

    /// <summary>One call under its own deadline. A timeout or a refused connection is a status of 0 and a sentence.</summary>
    private static async Task<(int Status, string Text)> SendAsync(
        HttpClient http, HttpMethod method, string url, string key, string? body, TimeSpan perCall)
    {
        using var cts = new CancellationTokenSource(perCall);
        using var message = new HttpRequestMessage(method, url);
        message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
        if (body is not null)
        {
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            var text = await BoundedBody.ReadAsync(response.Content, AskApiMode.MaxAnswerBytes, cts.Token);

            return ((int)response.StatusCode, text ?? $"answered more than {AskApiMode.MaxAnswerBytes / (1024 * 1024)} MiB — not read past the ceiling");
        }
        catch (OperationCanceledException)
        {
            return (0, $"no answer within {perCall.TotalSeconds:F0}s");
        }
        catch (HttpRequestException e)
        {
            return (0, $"could not be reached: {e.Message}");
        }
    }

    /// <summary>Which known request field the refusal names — the first one found, or empty.</summary>
    internal static string RefusedField(string redactedError) =>
        KnownFields.FirstOrDefault(field => redactedError.Contains(field, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

    private static Tokens Usage(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return new Tokens(0, 0, 0);
            }

            var cached = usage.TryGetProperty("prompt_tokens_details", out var details)
                         && details.ValueKind == JsonValueKind.Object
                         && details.TryGetProperty("cached_tokens", out var c) && c.TryGetInt64(out var cachedTokens)
                ? cachedTokens
                : 0;

            return new Tokens(
                usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt64(out var prompt) ? prompt : 0,
                usage.TryGetProperty("completion_tokens", out var o) && o.TryGetInt64(out var completion) ? completion : 0,
                cached);
        }
        catch (JsonException)
        {
            return new Tokens(0, 0, 0);
        }
    }

    /// <summary>The vendor's words, one line, redacted, capped — the ONLY vendor text that is ever printed.</summary>
    internal static string Quoted(string text) =>
        Redaction.SafeText(text.Replace('\r', ' ').Replace('\n', ' ').Trim(), QuoteLimit);

    /// <summary>The report on stdout: the allowlisted fields and nothing else.</summary>
    private static string Render(Probe probe, Report report)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("vendor", probe.Vendor);
            json.WriteString("endpoint", probe.Endpoint);
            json.WriteString("dialect", probe.Dialect.Name);
            json.WriteString("model", probe.Model);
            json.WriteStartObject("models");
            json.WriteNumber("status", report.Models.Status);
            json.WriteStartArray("ids");
            foreach (var id in report.Models.Ids)
            {
                json.WriteStringValue(id);
            }

            json.WriteEndArray();
            json.WriteString("error", report.Models.Error);
            json.WriteEndObject();
            json.WriteStartArray("requests");
            foreach (var request in report.Requests)
            {
                WriteRequest(json, probe.Model, request);
            }

            json.WriteEndArray();
            json.WriteStartArray("notes");
            foreach (var line in report.Notes)
            {
                json.WriteStringValue(line);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteRequest(Utf8JsonWriter json, string model, RequestResult request)
    {
        json.WriteStartObject();
        json.WriteString("model", model);
        json.WriteString("case", request.Case);
        json.WriteNumber("status", request.Status);
        json.WriteString("refusedField", request.RefusedField);
        json.WriteString("error", request.Error);
        json.WriteNumber("promptTokens", request.Tokens.Prompt);
        json.WriteNumber("completionTokens", request.Tokens.Completion);
        json.WriteNumber("cachedTokens", request.Tokens.Cached);
        json.WriteNull("cost");
        json.WriteEndObject();
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    internal sealed record Probe(string Vendor, string Endpoint, string Model, ApiDialect Dialect, TimeSpan PerCall);

    internal sealed record Variant(string Name, ApiDialect Dialect, bool WrongKey, string Effort = "");

    private sealed record ModelsResult(int Status, IReadOnlyList<string> Ids, string Error);

    private sealed record Tokens(long Prompt, long Completion, long Cached);

    private sealed record RequestResult(string Case, int Status, string RefusedField, string Error, Tokens Tokens);

    private sealed record Report(ModelsResult Models, IReadOnlyList<RequestResult> Requests, IReadOnlyList<string> Notes);
}
