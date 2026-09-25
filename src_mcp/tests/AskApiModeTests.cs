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

    private Task<int> RunAsync(string? key = Key, string endpoint = "", string dialect = "openai", string schema = "", string vendor = "grok")
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
}
