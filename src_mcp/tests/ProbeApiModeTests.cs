using System.Text.Json;
using CoaiMcp.Api;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>coai-mcp --probe-api</c>: the vault read the product's way, <c>GET /models</c>, the S0.5 matrix
/// — and an OUTPUT that is an allowlist of fields, with the key on neither stream whatever the
/// vendor echoes back.
/// </summary>
/// <remarks>
/// The vault is a fake launcher answering <c>creds config</c> with a key; the endpoint is a real socket
/// whose error bodies echo the request's <c>Authorization</c> header and a key-shaped token — the
/// exact thing the plan round asked to see redacted (PLAN_feature_review.md §7.2, S1.2).
/// </remarks>
public sealed class ProbeApiModeTests : IDisposable
{
    private const string Key = "sk-vault-0123456789abcdefghijklmnopqrstuv";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-probe-api-").FullName;
    private readonly List<string> _stderr = [];
    private readonly StringWriter _stdout = new();

    public ProbeApiModeTests()
    {
        _stub.Answers = Matrix;
    }

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

    /// <summary>The endpoint's side of the matrix: refuse a penalty, refuse a wrong key, cache the second turn.</summary>
    private static ApiEndpointStub.Answer Matrix(ApiEndpointStub.Seen seen)
    {
        if (seen.Path.EndsWith("/models", StringComparison.Ordinal))
        {
            return new ApiEndpointStub.Answer(200, "{\"object\":\"list\",\"data\":[{\"id\":\"grok-4\",\"object\":\"model\"},{\"id\":\"grok-3-mini\"}]}");
        }

        if (seen.Authorization != $"Bearer {Key}")
        {
            return new ApiEndpointStub.Answer(401, "{\"error\":\"Incorrect API key provided: " + seen.Authorization + "\"}");
        }

        if (seen.Body.Contains("frequency_penalty", StringComparison.Ordinal))
        {
            return new ApiEndpointStub.Answer(
                400, "{\"error\":{\"message\":\"Argument not supported on this model: frequency_penalty. Authorization: "
                     + seen.Authorization + " sk-echoed-0123456789abcdefghij\"}}");
        }

        // Every accepted request reports cached tokens as if it were a second turn: the case that
        // matters is that the number is READ, and the matrix's own second turn is what reads it.
        return new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}", 900, 40, cachedTokens: 640));
    }

    private Task<int> RunAsync(string[] args, IProcessLauncher? vault = null, string? credsKey = "the-config-key") =>
        ProbeApiMode.RunAsync(
            args,
            _stderr.Add,
            _stdout,
            name => name switch
            {
                "COAI_CREDS_KEY" => credsKey,
                "COAI_DATA_DIR" => _dir,
                _ => null,
            },
            vault ?? new VaultAnswers("{\"grok\":\"" + Key + "\"}"));

    private JsonElement Report() => JsonDocument.Parse(_stdout.ToString()).RootElement;

    private void BothStreamsAreClean()
    {
        _stdout.ToString().Should().NotContain(Key, "the key must never reach stdout");
        Stderr.Should().NotContain(Key, "the key must never reach stderr");
    }

    [Fact]
    public async Task WithoutAVendor_ItExits65_Never64()
    {
        var code = await RunAsync(["--probe-api"]);

        code.Should().Be(65);
        Stderr.Should().Contain("--vendor");
        _stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task WithNoVaultConfigured_ItExits78_WithTheVaultsOwnReason()
    {
        var code = await RunAsync(["--probe-api", "--vendor", "grok", "--endpoint", _stub.Endpoint], credsKey: null);

        code.Should().Be(78);
        Stderr.Should().Contain("COAI_CREDS_KEY");
        _stub.Requests.Should().BeEmpty("a probe with no key has nothing to ask with");
    }

    [Fact]
    public async Task WithNoKeyUnderTheVendor_ItExits78_NamingTheEntry()
    {
        var code = await RunAsync(
            ["--probe-api", "--vendor", "qwen", "--endpoint", _stub.Endpoint],
            new VaultAnswers("{\"grok\":\"" + Key + "\"}"));

        code.Should().Be(78);
        Stderr.Should().Contain("'qwen'");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task TheVaultIsReadTheWayEveryOtherModeReadsIt()
    {
        var vault = new VaultAnswers("{\"grok\":\"" + Key + "\"}");

        await RunAsync(["--probe-api", "--vendor", "grok", "--endpoint", _stub.Endpoint], vault);

        vault.Requests.Should().ContainSingle().Which.Arguments.Should().Equal("config", "the-config-key");
        vault.Requests.Single().Executable.Should().Be("creds");
    }

    [Fact]
    public async Task ItListsTheModelsTheKeyCanCall_AndWithoutAModelRunsNoMatrix()
    {
        var code = await RunAsync(["--probe-api", "--vendor", "grok", "--endpoint", _stub.Endpoint]);

        code.Should().Be(0, Stderr);
        var report = Report();
        report.GetProperty("models").GetProperty("status").GetInt32().Should().Be(200);
        report.GetProperty("models").GetProperty("ids").EnumerateArray().Select(m => m.GetString()).Should().Equal("grok-4", "grok-3-mini");
        report.GetProperty("requests").GetArrayLength().Should().Be(0);
        report.GetProperty("notes").EnumerateArray().Select(n => n.GetString()).Should().Contain(n => n!.Contains("--model"));
        _stub.Requests.Should().ContainSingle().Which.Authorization.Should().Be($"Bearer {Key}");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task TheMatrix_RecordsWhichFieldWasRefused_WithTheVendorsWordsRedacted()
    {
        var code = await RunAsync(["--probe-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", "grok-4"]);

        code.Should().Be(0, Stderr);
        var requests = Report().GetProperty("requests").EnumerateArray().ToList();
        var refused = requests.Single(r => r.GetProperty("case").GetString() == "frequency_penalty");
        refused.GetProperty("status").GetInt32().Should().Be(400);
        refused.GetProperty("refusedField").GetString().Should().Be("frequency_penalty");
        refused.GetProperty("error").GetString().Should().Contain("[redacted]").And.NotContain("sk-echoed");
        BothStreamsAreClean();
    }

    [Fact]
    public async Task TheMatrix_ProvokesA401OnPurpose_AndReadsCachedTokensOffTheSecondTurn()
    {
        await RunAsync(["--probe-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", "grok-4"]);

        var requests = Report().GetProperty("requests").EnumerateArray().ToList();
        var wrongKey = requests.Single(r => r.GetProperty("case").GetString() == "wrong_key");
        wrongKey.GetProperty("status").GetInt32().Should().Be(401);
        wrongKey.GetProperty("error").GetString().Should().Contain("[redacted]");
        var second = requests.Single(r => r.GetProperty("case").GetString() == "second_turn");
        second.GetProperty("status").GetInt32().Should().Be(200);
        second.GetProperty("cachedTokens").GetInt64().Should().Be(640);
        second.GetProperty("promptTokens").GetInt64().Should().Be(900);
        second.GetProperty("cost").ValueKind.Should().Be(JsonValueKind.Null, "no price table ships; the note says so");
        requests.Select(r => r.GetProperty("case").GetString()).Should().Contain(["json_schema", "json_object", "temperature", "seed", "reasoning_effort=high"]);
        BothStreamsAreClean();
    }

    [Fact]
    public async Task EveryReportedFieldIsOnTheAllowlist()
    {
        await RunAsync(["--probe-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", "grok-4"]);

        var report = Report();
        report.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["vendor", "endpoint", "dialect", "model", "models", "requests", "notes"]);
        foreach (var request in report.GetProperty("requests").EnumerateArray())
        {
            request.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
                ["model", "case", "status", "refusedField", "error", "promptTokens", "completionTokens", "cachedTokens", "cost"]);
        }
    }

    [Fact]
    public async Task EachCallHasItsOwnTimeout_SoOneHungRequestDoesNotSpendTheProbe()
    {
        var hung = 0;
        _stub.Answers = seen =>
        {
            if (seen.Path.EndsWith("/models", StringComparison.Ordinal) && Interlocked.Increment(ref hung) == 1)
            {
                Thread.Sleep(TimeSpan.FromSeconds(3));
            }

            return Matrix(seen);
        };

        var code = await RunAsync(["--probe-api", "--vendor", "grok", "--endpoint", _stub.Endpoint, "--model", "grok-4", "--timeout-seconds", "1"]);

        code.Should().Be(0);
        Report().GetProperty("models").GetProperty("status").GetInt32().Should().Be(0, "the models call timed out on its own clock");
        Report().GetProperty("requests").GetArrayLength().Should().BeGreaterThan(0, "the matrix still ran after it");
    }

    /// <summary>The vault, answering `creds config` with a body — and recording what it was asked.</summary>
    private sealed class VaultAnswers(string stdout, int exitCode = 0) : IProcessLauncher
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            return Task.FromResult(new ProcessResult(exitCode, stdout, string.Empty, false));
        }
    }
}
