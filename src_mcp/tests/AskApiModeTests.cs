using System.Text.Json;
using CoaiMcp.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --ask-api</c>, in-process, against a real socket: the exit-code contract, the header,
/// the body the generic dialect spells — and that the key never reaches stdout or stderr.
/// </summary>
/// <remarks>
/// The key here is a real-looking <c>sk-</c> string on purpose: the redaction pass recognises that shape,
/// and a stub that echoes the request's <c>Authorization</c> back in an error body is exactly what some
/// gateways do. Every test asserts both streams are clean, so a new sentence that quoted the wrong thing
/// would go red in the test that added it. (PLAN_feature_review.md §4.10, the plan round of 2026-09-25.)
/// </remarks>
public sealed class AskApiModeTests : IDisposable
{
    private const string Key = "sk-live-0123456789abcdefghijklmnopqrstuv";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-ask-api-").FullName;
    private readonly List<string> _stderr = [];
    private readonly StringWriter _stdout = new();

    public void Dispose()
    {
        _stub.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Stderr => string.Join("\n", _stderr);

    private string Prompt() => Write("prompt.txt", "review this");

    private string Schema() => Write("schema.json", FindingSchema.Json);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);

        return path;
    }

    private Task<int> RunAsync(
        string? key = Key, string endpoint = "", string dialect = "openai", string schema = "", string vendor = "grok", string conversation = "")
    {
        var args = new List<string>
        {
            "--ask-api", "--vendor", vendor,
            "--endpoint", endpoint.Length > 0 ? endpoint : _stub.Endpoint,
            "--model", "grok-4", "--dialect", dialect,
            "--prompt-file", Prompt(), "--schema-file", schema.Length > 0 ? schema : Schema(),
            "--out", Path.Combine(_dir, "answer.json"),
            "--timeout-seconds", "20", "--max-tokens", "4096",
        };
        if (conversation.Length > 0)
        {
            args.AddRange(["--conversation", conversation]);
        }

        return AskApiMode.RunAsync([.. args], _stderr.Add, _stdout, name => name == ApiRuntime.KeyVariable ? key : null);
    }

    private void BothStreamsAreClean()
    {
        Stderr.Should().NotContain(Key, "the key must never reach stderr");
        _stdout.ToString().Should().NotContain(Key, "the key must never reach stdout");
    }

    [Fact]
    public async Task WithNoArguments_ItExits65_Never64()
    {
        var code = await AskApiMode.RunAsync(["--ask-api"], _stderr.Add, _stdout, _ => Key);

        code.Should().Be(65, "64 means 'never heard of that mode' and would send the executor down a fallback");
        Stderr.Should().Contain("--prompt-file");
        _stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAKeyInTheEnvironment_ItRefusesBeforeAnySocketOpens()
    {
        var code = await RunAsync(key: null);

        code.Should().Be(65);
        Stderr.Should().Contain("'grok'").And.Contain("vault");
        _stub.Requests.Should().BeEmpty("an unauthenticated request would spend a round trip to be told 401");
    }

    [Fact]
    public async Task AnUnknownDialect_IsRefused_NamingTheOnesThisBuildKnows()
    {
        var code = await RunAsync(dialect: "grokish");

        code.Should().Be(65);
        Stderr.Should().Contain("grokish").And.Contain("openai").And.Contain("local");
        _stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingSchema_IsRefused_NotSubstituted()
    {
        var code = await RunAsync(schema: Path.Combine(_dir, "nowhere.json"));

        code.Should().Be(65);
        _stub.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// An answer over the ceiling is refused without being buffered whole.
    /// </summary>
    /// <remarks>
    /// Epic 1's code round (codex): `ReadAsStringAsync` read the entire body before any cap ran, so an
    /// endpoint that ignored the token ceiling — or answered with a huge error page — could take the
    /// MCP process's memory down with every session in it.
    /// </remarks>
    [Fact]
    public async Task AnAnswerOverTheCeiling_IsRefused_NotBufferedWhole()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, new string('x', AskApiMode.MaxAnswerBytes + 1));

        var code = await RunAsync();

        code.Should().Be(70);
        Stderr.Should().Contain("more than").And.Contain("refused");
    }

    [Fact]
    public async Task AnAnswer_IsWrittenToTheOutFile_AndTheTokensToStdout()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}", 120, 34));

        var code = await RunAsync();

        code.Should().Be(0, $"stderr said: {Stderr}; {_stub.Journal()}");
        File.ReadAllText(Path.Combine(_dir, "answer.json")).Should().Be("{\"findings\":[]}");
        var usage = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(
            new ReviewerInvocation("grok", "r", new ProcessRequest("x", [], ".")),
            new ProcessResult(0, _stdout.ToString(), string.Empty, false));
        usage.TokensIn.Should().Be(120);
        usage.TokensOut.Should().Be(34);
        BothStreamsAreClean();
    }

    [Fact]
    public async Task TheRequestCarriesTheBearer_OnTheCompletionsRoute()
    {
        await RunAsync();

        var seen = _stub.Requests.Should().ContainSingle().Subject;
        seen.Method.Should().Be("POST");
        seen.Path.Should().Be("/v1/chat/completions");
        seen.Authorization.Should().Be($"Bearer {Key}");
    }

    [Fact]
    public async Task TheGenericDialect_SendsNothingAReasoningModelRefuses_AndDemandsTheSchemaUnbounded()
    {
        await RunAsync();

        using var body = JsonDocument.Parse(_stub.Requests.Single().Body);
        var root = body.RootElement;
        root.TryGetProperty("temperature", out _).Should().BeFalse();
        root.TryGetProperty("seed", out _).Should().BeFalse();
        root.TryGetProperty("frequency_penalty", out _).Should().BeFalse();
        root.TryGetProperty("max_tokens", out _).Should().BeFalse();
        root.GetProperty("max_completion_tokens").GetInt32().Should().Be(4096);
        root.GetProperty("model").GetString().Should().Be("grok-4");
        var format = root.GetProperty("response_format");
        format.GetProperty("type").GetString().Should().Be("json_schema");
        format.GetProperty("json_schema").GetProperty("strict").GetBoolean().Should().BeTrue();
        _stub.Requests.Single().Body.Should().NotContain("maxLength",
            "OpenAI's strict structured outputs reject maxLength with a 400 — the bound is local-only");
    }

    [Fact]
    public async Task A429_Exits75_WithASentenceTheRateLimitMatcherReads()
    {
        // A body with NONE of the matcher's phrases in it, so only this shim's own sentence can make
        // the round wait: a vendor that says "slow down" is one the phrase list cannot see.
        _stub.Answers = _ => new ApiEndpointStub.Answer(
            429, "{\"error\":{\"message\":\"slow down\"}}",
            new Dictionary<string, string> { ["Retry-After"] = "7" });

        var code = await RunAsync();

        code.Should().Be(75);
        // The matcher reads the TEXT with a non-zero exit — never the code alone — so this is the
        // assertion that the round will actually wait and retry rather than report a failed vendor.
        RateLimit.Hit(new ProcessResult(code, _stdout.ToString(), Stderr, false)).Should().BeTrue(
            $"the sentence must carry the shape RateLimit.Hit reads; stderr said: {Stderr}");
        Stderr.Should().Contain("HTTP 429 Too Many Requests").And.Contain("try again in 7s");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task A503_Exits75_AndIsRecognisedTheSameWay()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(503, "upstream unavailable");

        var code = await RunAsync();

        code.Should().Be(75);
        Stderr.Should().Contain("HTTP 503 Service Unavailable");
        RateLimit.Hit(new ProcessResult(code, string.Empty, Stderr, false)).Should().BeTrue();
    }

    [Fact]
    public async Task A401_Exits77_NamesTheVendor_AndNeverEchoesTheBody()
    {
        _stub.Answers = seen => new ApiEndpointStub.Answer(
            401, "{\"error\":\"super-secret-body-text: " + seen.Authorization + "\"}");

        var code = await RunAsync();

        code.Should().Be(77);
        Stderr.Should().Contain("refused the key for vendor 'grok'").And.Contain("HTTP 401");
        Stderr.Should().NotContain("super-secret-body-text", "a refusal body can echo the request, so it is not quoted at all");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task A400_that_says_the_key_is_wrong_is_the_same_refusal_as_a_401()
    {
        // xAI's own answer to a wrong key, measured by S0.5's probe (`wrong_key`) on 2026-09-26: a 400,
        // not a 401 — so a revoked Grok key read as "the request was malformed" (§9.11).
        _stub.Answers = seen => new ApiEndpointStub.Answer(
            400, "{\"code\":\"Client specified an invalid argument\",\"error\":\"Incorrect API key provided: "
                 + seen.Authorization + ". You can obtain an API key from https://console.x.ai.\"}");

        var code = await RunAsync();

        code.Should().Be(77, $"stderr said: {Stderr}");
        Stderr.Should().Contain("refused the key for vendor 'grok'").And.Contain("HTTP 400");
        Stderr.Should().NotContain("Incorrect API key", "a key refusal's body is not quoted, whatever its status");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task An_OpenAI_shaped_invalid_key_code_on_a_400_is_a_refusal_too()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(
            400, "{\"error\":{\"message\":\"Invalid API key.\",\"code\":\"invalid_api_key\"}}");

        (await RunAsync()).Should().Be(77);
    }

    [Fact]
    public async Task A400_about_anything_else_stays_a_failed_request_even_when_it_quotes_the_phrase_elsewhere()
    {
        // The phrase is read off the ERROR field only: a vendor that echoes the prompt back in some
        // other field — and a review of this very file carries the phrase — must not become a key refusal.
        _stub.Answers = _ => new ApiEndpointStub.Answer(
            400, "{\"error\":{\"message\":\"Argument not supported: frequency_penalty\"},\"echo\":\"Incorrect API key provided\"}");

        (await RunAsync()).Should().Be(70);
        Stderr.Should().Contain("HTTP 400");
    }

    [Fact]
    public async Task A403_IsTheSameRefusal()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(403, "forbidden");

        (await RunAsync()).Should().Be(77);
        Stderr.Should().Contain("HTTP 403");
    }

    [Fact]
    public async Task AnErrorBodyThatEchoesTheHeader_ComesOutRedacted()
    {
        // What a gateway that echoes the request looks like: the bearer verbatim, and a key-shaped
        // token beside it. Both must read [redacted] on stderr.
        _stub.Answers = seen => new ApiEndpointStub.Answer(
            500, "{\"error\":\"bad gateway; Authorization: " + seen.Authorization + " token sk-echoed-0123456789abcdef\"}");

        var code = await RunAsync();

        code.Should().Be(70);
        Stderr.Should().Contain("HTTP 500").And.Contain("[redacted]");
        Stderr.Should().NotContain("sk-echoed");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task AnAnswerWithNoMessageContent_Exits70()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, "{\"choices\":[]}");

        (await RunAsync()).Should().Be(70);
        Stderr.Should().Contain("no message content");
    }

    [Fact]
    public async Task AnEndpointNobodyAnswersOn_Exits69_WithTheAddress()
    {
        var code = await RunAsync(endpoint: "http://127.0.0.1:1/v1");

        code.Should().Be(69);
        Stderr.Should().Contain("could not be reached").And.Contain("127.0.0.1:1");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task AVendorTextIsQuotedOnOneLine_RedactedAndCapped()
    {
        var text = "line one\nline two Authorization: Bearer " + Key + " and " + new string('x', 1000);

        var quoted = AskApiMode.Quoted(text);

        quoted.Should().NotContain("\n").And.NotContain(Key).And.Contain("[redacted]");
        quoted.Length.Should().BeLessThan(AskApiMode.QuoteLimit + 20);
    }

    // ---------- measured on 2026-09-26 against the Alibaba route (GLM-5.3, Qwen3.8-max) and xAI (grok-4.7) ----------

    /// <summary>
    /// An answer the vendor CUT at the token ceiling is a failure even when what arrived still parses.
    /// </summary>
    /// <remarks>
    /// Measured: at a 16,384 ceiling every GLM-5.3 call spent exactly 16,384 completion tokens and wrote
    /// <c>{"</c>; at another cut the fragment can be <c>{"findings":[]}</c> with the real findings never
    /// written — a clean review that was never given. <c>finish_reason: "length"</c> is the vendor saying
    /// so, and the shim ignored it: non-empty content became exit 0. The sentence must say the cause, and
    /// the tokens must still cross to stdout, because the cut call is billed like any other.
    /// </remarks>
    [Fact]
    public async Task AnAnswerCutAtTheTokenLimit_IsAFailure_EvenWhenTheFragmentParses()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, Completion("{\"findings\":[]}", finish: "length", prompt: 500, completion: 4096, reasoning: 4000));

        var code = await RunAsync();

        code.Should().Be(70, $"stderr said: {Stderr}");
        Stderr.Should().Contain("cut at the token limit").And.Contain("4096").And.Contain("4000 reasoning").And.Contain("max_completion_tokens");
        File.Exists(Path.Combine(_dir, "answer.json")).Should().BeFalse("a fragment must not be left where the executor reads an answer");
        UsageOnStdout().TokensIn.Should().Be(500, "the cut call was billed, so its tokens travel to the ledger");
        BothStreamsAreClean();
    }

    /// <summary>
    /// A reasoning-only answer — <c>reasoning_content</c> and no <c>content</c>, how Qwen3.8-max failed the
    /// first trial — is a failure that still declares what it consumed.
    /// </summary>
    [Fact]
    public async Task AReasoningOnlyAnswer_Exits70_AndStillDeclaresItsTokensOnStdout()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200,
            "{\"id\":\"cmpl-2\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"reasoning_content\":\"Let me think about this feature at length…\",\"content\":\"\"},"
            + "\"finish_reason\":\"length\"}],\"usage\":{\"prompt_tokens\":53092,\"completion_tokens\":16382,\"completion_tokens_details\":{\"reasoning_tokens\":16382}}}");

        var code = await RunAsync();

        code.Should().Be(70);
        Stderr.Should().Contain("no message content").And.Contain("16382 reasoning");
        var usage = UsageOnStdout();
        usage.TokensIn.Should().Be(53092);
        usage.TokensOut.Should().Be(16382);
        usage.TokensReasoning.Should().Be(16382, "the reasoning share is reported for the record");
        usage.CostUsd.Should().BeNull("the shim prints no money; an invocation without a price reads none");
        _stdout.ToString().Should().Contain("\"tokensReasoning\":16382").And.NotContain("costUsd", "the parent prices the raw tokens (#23)");
        BothStreamsAreClean();
    }

    /// <summary>
    /// xAI reports reasoning tokens OUTSIDE <c>completion_tokens</c> and bills them as output: measured
    /// 2026-09-26, grok-4.7 answered <c>prompt 19,681 · completion 1,557 · reasoning 27,728 · total 48,966</c>
    /// and its own <c>cost_in_usd_ticks</c> priced all 29,285 as output. The Alibaba route puts reasoning
    /// INSIDE <c>completion_tokens</c>. The one rule that reads both right: what the total says was
    /// generated — <c>total_tokens − prompt_tokens</c> — when it exceeds <c>completion_tokens</c>.
    /// </summary>
    [Fact]
    public async Task ReasoningTokensReportedOutsideTheCompletion_AreBilledAsOutput()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200,
            "{\"id\":\"cmpl-3\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"{\\\"findings\\\":[]}\"},\"finish_reason\":\"stop\"}],"
            + "\"usage\":{\"prompt_tokens\":19681,\"completion_tokens\":1557,\"total_tokens\":48966,"
            + "\"prompt_tokens_details\":{\"cached_tokens\":1152},\"completion_tokens_details\":{\"reasoning_tokens\":27728}}}");

        var code = await RunAsync();

        code.Should().Be(0, Stderr);
        var usage = UsageOnStdout();
        usage.TokensOut.Should().Be(29285, "1,557 answer tokens and 27,728 reasoning tokens were both generated and both billed");
        usage.TokensIn.Should().Be(19681);
        usage.TokensCached.Should().Be(1152);
    }

    // ---------- cache routing (xAI, measured 2026-09-26: 1,152 cached tokens on every turn of a byte-identical prefix) ----------

    /// <summary>
    /// A dialect that names a cache-routing header sends the reviewer's conversation key in it on every
    /// turn — "routes requests with the same conversation ID to the same server. Since cache entries are
    /// stored per-server, this maximizes your cache hit rate" (xAI's prompt-caching guide).
    /// </summary>
    [Fact]
    public async Task TheXaiDialect_SendsTheConversationKey_InTheRoutingHeader()
    {
        await RunAsync(dialect: "xai", conversation: "c0ffee0123456789abcdef01");

        _stub.Requests.Should().ContainSingle().Which.Header("x-grok-conv-id").Should().Be("c0ffee0123456789abcdef01");
    }

    [Fact]
    public async Task WithoutAConversationKey_NoRoutingHeaderIsSent_EvenByTheXaiDialect()
    {
        await RunAsync(dialect: "xai");

        _stub.Requests.Should().ContainSingle().Which.Header("x-grok-conv-id").Should().BeEmpty();
    }

    [Fact]
    public async Task TheGenericDialect_SendsNoRoutingHeader_WhateverTheKey()
    {
        await RunAsync(dialect: "openai", conversation: "c0ffee0123456789abcdef01");

        _stub.Requests.Should().ContainSingle().Which.Header("x-grok-conv-id").Should().BeEmpty(
            "the generic row sends nothing a vendor is not documented to read");
    }

    private Usage UsageOnStdout() =>
        new ApiRuntime("grok", _stub.Endpoint).ReadUsage(
            new ReviewerInvocation("grok", "r", new ProcessRequest("x", [], ".")),
            new ProcessResult(0, _stdout.ToString(), string.Empty, false));

    /// <summary>A completion with the finish reason and the token details a reasoning vendor reports.</summary>
    private static string Completion(string content, string finish, long prompt, long completion, long reasoning)
    {
        var escaped = content.Replace("\\", "\\\\").Replace("\"", "\\\"");

        return "{\"id\":\"cmpl-9\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"" + escaped + "\"},"
            + "\"finish_reason\":\"" + finish + "\"}],\"usage\":{\"prompt_tokens\":" + prompt + ",\"completion_tokens\":" + completion
            + ",\"completion_tokens_details\":{\"reasoning_tokens\":" + reasoning + "}}}";
    }
}
