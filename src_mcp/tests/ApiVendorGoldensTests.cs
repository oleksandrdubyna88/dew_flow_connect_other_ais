using CoaiMcp.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The wire behaviour of every measured api row, pinned BEFORE the vendor modules were carved out of the
/// shim: the exact request body and headers of a sample turn, how a recorded vendor answer is read into
/// the usage line and the exit code, and what each HTTP refusal is classified as.
/// </summary>
/// <remarks>
/// <para>The grok-4.7 and qwen3.8-max calibrations of 2026-09-26/27 (<c>RESULTS_feature_reviewer_models.md</c>)
/// were measured through <c>--ask-api</c> as it was on that day. A refactor that changed one byte of a
/// request, one number of a usage line or one exit code would silently invalidate them — so the whole of
/// it is written down here first, and the refactor is held to it. The same discipline as
/// <c>LocalRequestBodyIsPinnedTests</c>.</para>
/// <para><b>The request goldens are RECORDED files</b> (<c>fixtures/api-goldens/*.request.json</c>): the
/// first run against a missing golden writes the observed bytes there and FAILS, naming the file; the
/// bytes are then reviewed and committed, and every later run holds the code to them. A golden that
/// recomputed its expectation through the code under test would pin nothing.</para>
/// <para><b>The response fixtures are hand-written from measured answers</b> — the usage objects xAI and
/// the Alibaba route actually returned (reasoning outside <c>completion_tokens</c> on xAI, inside on the
/// Alibaba route; the cached subset; a cut at the ceiling), with the ids and the content replaced.</para>
/// </remarks>
public sealed class ApiVendorGoldensTests : IDisposable
{
    private const string Key = "sk-live-0123456789abcdefghijklmnopqrstuv";
    private const string Conversation = "c0ffee0123456789abcdef0123456789";

    /// <summary>A turn with everything the writer escapes: quotes, ticks, angle brackets, an ampersand, a newline, non-ASCII.</summary>
    private const string Prompt =
        "You are the feature reviewer.\nReview this plan — it has \"quotes\", `ticks`, a <tag> & an apostrophe's, and a café.\n\n```diff\n+    var value = Compute(1);\n```\n";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-api-golden-").FullName;
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

    // ---------- the request: body and headers, byte for byte ----------

    /// <summary>
    /// One representative turn per measured row: grok-4.7 at the vendor's default depth with its
    /// conversation key (the xai row), qwen3.8-max at <c>medium</c> (the dashscope row), and the generic
    /// row at <c>high</c> with no conversation.
    /// </summary>
    [Theory]
    [InlineData("openai", "gpt-5", "high", "")]
    [InlineData("xai", "grok-4.7", "", Conversation)]
    [InlineData("dashscope", "qwen3.8-max", "medium", Conversation)]
    public async Task TheRequestOfASampleTurn_IsByteIdentical_ToTheRecordedGolden(string row, string model, string effort, string conversation)
    {
        var code = await RunAsync(row, model, effort, conversation);

        code.Should().Be(0, Stderr);
        var seen = _stub.Requests.Should().ContainSingle().Which;
        seen.Path.Should().Be("/v1/chat/completions");
        seen.Authorization.Should().Be($"Bearer {Key}");
        seen.Body.Should().Be(Golden($"{row}.request.json", seen.Body));
    }

    [Fact]
    public async Task TheXaiRow_RoutesTheConversation_AndTheOthersSendNoSuchHeader()
    {
        await RunAsync("xai", "grok-4.7", "", Conversation);
        _stub.Requests[^1].Header("x-grok-conv-id").Should().Be(Conversation);

        await RunAsync("dashscope", "qwen3.8-max", "medium", Conversation);
        _stub.Requests[^1].Header("x-grok-conv-id").Should().BeEmpty("the Alibaba route's cache is content-addressed and takes no key");

        await RunAsync("openai", "gpt-5", "high", Conversation);
        _stub.Requests[^1].Header("x-grok-conv-id").Should().BeEmpty("the generic row sends nothing a vendor is not documented to read");
    }

    // ---------- the answer: what a recorded vendor response is read as ----------

    /// <summary>
    /// The usage line the parent prices, from the vendors' own usage objects: xAI files its reasoning
    /// OUTSIDE <c>completion_tokens</c> (generated = total − prompt), the Alibaba route inside; the cached
    /// subset and the reasoning count ride along; a cut at the ceiling and a reasoning-only answer are
    /// 70 with the tokens still on stdout.
    /// </summary>
    [Theory]
    [InlineData("xai", "xai-turn.response.json", 0, """{"tokensIn":41803,"tokensOut":37597,"tokensCached":34944,"tokensReasoning":35952}""", true)]
    [InlineData("xai", "xai-cut.response.json", 70, """{"tokensIn":19681,"tokensOut":26433,"tokensCached":1152,"tokensReasoning":26369}""", false)]
    [InlineData("dashscope", "dashscope-turn.response.json", 0, """{"tokensIn":40422,"tokensOut":6396,"tokensCached":33792,"tokensReasoning":4538}""", true)]
    [InlineData("dashscope", "dashscope-cut.response.json", 70, """{"tokensIn":948,"tokensOut":64,"tokensCached":0,"tokensReasoning":64}""", false)]
    [InlineData("dashscope", "dashscope-reasoning-only.response.json", 70, """{"tokensIn":32523,"tokensOut":16382,"tokensCached":0,"tokensReasoning":16382}""", false)]
    [InlineData("openai", "openai-turn.response.json", 0, """{"tokensIn":120,"tokensOut":34,"tokensCached":0,"tokensReasoning":0}""", true)]
    public async Task ARecordedAnswer_IsReadIntoTheSameUsageLineAndExitCode(string row, string fixture, int exit, string usageLine, bool answerKept)
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, File.ReadAllText(Fixture(fixture)));

        var code = await RunAsync(row, "m", "", "");

        code.Should().Be(exit, Stderr);
        _stdout.ToString().Trim().Should().Be(usageLine);
        File.Exists(Path.Combine(_dir, "answer.json")).Should().Be(answerKept);
        BothStreamsAreClean();
    }

    /// <summary>
    /// A cut that left a fragment names the row's ceiling field and the number sent (the floor, on the
    /// Alibaba route); a cut that left NOTHING is reported as no content, with its reasoning tokens — the
    /// order the shim judged in on the day of the measurements, pinned as it was.
    /// </summary>
    [Fact]
    public async Task ACutAnswer_NamesTheRowsCeilingField_AndTheFloorItWasRaisedTo()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, File.ReadAllText(Fixture("xai-cut.response.json")));
        await RunAsync("xai", "grok-4.7", "", "");
        Stderr.Should().Contain("cut at the token limit (max_completion_tokens 8192)").And.Contain("26433 tokens generated (26369 reasoning tokens)");

        _stderr.Clear();
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, File.ReadAllText(Fixture("xai-cut.response.json")));
        await RunAsync("dashscope", "qwen3.8-max", "medium", "");
        Stderr.Should().Contain("cut at the token limit (max_completion_tokens 65536)", "the Alibaba row names the floor it raised the ceiling to");

        _stderr.Clear();
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, File.ReadAllText(Fixture("dashscope-cut.response.json")));
        await RunAsync("dashscope", "qwen3.8-max", "medium", "");
        Stderr.Should().Contain("returned no message content (64 reasoning tokens)");
    }

    // ---------- refusals: what each status is classified as, per row ----------

    /// <summary>
    /// 77 for a key the vendor refuses — 401, 403, and xAI's 400 whose error says so; 75 for 429 and 503
    /// with the sentence <c>RateLimit.Hit</c> reads; 70 for any other failure. The same on every row.
    /// </summary>
    [Theory]
    [InlineData("xai", 400, """{"code":"Client specified an invalid argument","error":"Incorrect API key provided: sk-live-0123. You can obtain an API key from https://console.x.ai."}""", 77, "refused the key")]
    [InlineData("xai", 401, """{"error":{"message":"Unauthorized"}}""", 77, "refused the key")]
    [InlineData("openai", 401, """{"error":{"message":"Incorrect API key provided","code":"invalid_api_key"}}""", 77, "refused the key")]
    [InlineData("dashscope", 401, """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error","code":"invalid_api_key"}}""", 77, "refused the key")]
    [InlineData("dashscope", 403, "forbidden", 77, "refused the key")]
    [InlineData("xai", 429, """{"error":"Too many requests"}""", 75, "HTTP 429 Too Many Requests")]
    [InlineData("dashscope", 429, """{"error":{"message":"Requests rate limit exceeded, please try again later.","code":"limit_requests"}}""", 75, "HTTP 429 Too Many Requests")]
    [InlineData("openai", 503, "upstream unavailable", 75, "HTTP 503 Service Unavailable")]
    [InlineData("xai", 400, """{"code":"Client specified an invalid argument","error":"Argument not supported on this model: frequencyPenalty"}""", 70, "answered HTTP 400")]
    [InlineData("dashscope", 500, """{"error":{"message":"internal"}}""", 70, "answered HTTP 500")]
    [InlineData("xai", 500, """{"code":"internal","error":"Auth context expired."}""", 75, "HTTP 500 Internal Server Error")]
    [InlineData("dashscope", 500, """{"code":"internal","error":"Auth context expired."}""", 75, "HTTP 500 Internal Server Error")]
    [InlineData("openai", 500, """{"error":{"message":"Auth context expired."}}""", 75, "HTTP 500 Internal Server Error")]
    public async Task ARefusal_IsClassifiedTheSameWay_OnEveryRow(string row, int status, string body, int exit, string said)
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(status, body);

        var code = await RunAsync(row, "m", "", "");

        code.Should().Be(exit, Stderr);
        Stderr.Should().Contain(said);
        _stdout.ToString().Should().BeEmpty("a refusal is not a billed completion, so no usage line");
        BothStreamsAreClean();
    }

    /// <summary>
    /// The vendor transient measured in phase 2 (2026-09-27): xAI answered 8 of 20 reviews' calls with HTTP 500
    /// <c>{"code":"internal","error":"Auth context expired."}</c>, on first calls and later ones alike, with the same
    /// key, body and concurrency as the successful calls before and after — a "try again" the ladder retries only if the
    /// shim says so in the words <c>RateLimit.Hit</c> reads. A 500 with any other body stays a failed request.
    /// </summary>
    [Fact]
    public async Task A_transient_500_whose_body_says_the_auth_context_expired_is_retryable_and_any_other_500_is_not()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(500, """{"code":"internal","error":"Auth context expired."}""");
        var transient = await RunAsync("xai", "grok-4.7", "medium", Conversation);
        transient.Should().Be(75, Stderr);
        Stderr.Should().Contain("HTTP 500 Internal Server Error from").And.Contain("Auth context expired");
        RateLimit.Hit(new ProcessResult(transient, _stdout.ToString(), Stderr, false)).Should().BeTrue("the ladder reads this sentence");

        _stderr.Clear();
        _stub.Answers = _ => new ApiEndpointStub.Answer(500, """{"code":"internal","error":"model overloaded"}""");
        var plain = await RunAsync("xai", "grok-4.7", "medium", Conversation);
        plain.Should().Be(70, Stderr);
        RateLimit.Hit(new ProcessResult(plain, _stdout.ToString(), Stderr, false)).Should().BeFalse("a 500 with no observed transient phrase is not retried");
    }

    [Fact]
    public async Task ARefusedKey_IsNeverQuoted_OnAnyRow()
    {
        foreach (var row in (string[])["openai", "xai", "dashscope"])
        {
            _stderr.Clear();
            _stub.Answers = _ => new ApiEndpointStub.Answer(401, $$$"""{"error":"Unauthorized: {{{Key}}} was echoed back"}""");
            (await RunAsync(row, "m", "", "")).Should().Be(77);
            Stderr.Should().NotContain("echoed back", $"{row}: the 401 body is not shown at all");
            BothStreamsAreClean();
        }
    }

    // ---------- helpers ----------

    private string Stderr => string.Join("\n", _stderr);

    private Task<int> RunAsync(string row, string model, string effort, string conversation)
    {
        var args = new List<string>
        {
            "--ask-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", model, "--dialect", row,
            "--prompt-file", Write("prompt.txt", Prompt), "--schema-file", Write("schema.json", FindingSchema.Json),
            "--out", Path.Combine(_dir, "answer.json"), "--timeout-seconds", "20", "--max-tokens", "8192",
        };
        if (effort.Length > 0)
        {
            args.AddRange(["--reasoning-effort", effort]);
        }

        if (conversation.Length > 0)
        {
            args.AddRange(["--conversation", conversation]);
        }

        return AskApiMode.RunAsync([.. args], _stderr.Add, _stdout, name => name == ApiRuntime.KeyVariable ? Key : null);
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);

        return path;
    }

    private void BothStreamsAreClean()
    {
        Stderr.Should().NotContain(Key, "the key must never reach stderr");
        _stdout.ToString().Should().NotContain(Key, "the key must never reach stdout");
    }

    /// <summary>The golden's bytes — or, when there is no golden yet, the observed bytes recorded there and a failure naming the file.</summary>
    private static string Golden(string name, string observed)
    {
        var path = Fixture(name);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        File.WriteAllText(path, observed);

        throw new Xunit.Sdk.XunitException(
            $"no golden at {path}: the observed bytes were RECORDED there. Review them, commit them, run again — "
            + "a golden is held to, never regenerated silently.");
    }

    /// <summary><c>src_mcp/tests/fixtures/api-goldens/&lt;name&gt;</c> in the SOURCE tree, so a recording lands where it is committed.</summary>
    internal static string Fixture(string name)
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, "src_mcp", "tests", "fixtures")))
        {
            here = here.Parent;
        }

        var folder = here is null
            ? throw new DirectoryNotFoundException($"src_mcp/tests/fixtures was not found above {AppContext.BaseDirectory}")
            : Path.Combine(here.FullName, "src_mcp", "tests", "fixtures", "api-goldens");
        Directory.CreateDirectory(folder);

        return Path.Combine(folder, name);
    }
}
