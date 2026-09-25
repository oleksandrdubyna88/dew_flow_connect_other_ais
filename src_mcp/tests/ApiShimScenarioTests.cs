using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The api reviewer as it actually runs: the REAL <c>coai-mcp --ask-api</c> binary, in its own process,
/// launched with the argv and the environment <see cref="ApiRuntime"/> built, against a real socket.
/// </summary>
/// <remarks>
/// <para>The twin of <see cref="RemoteShimScenarioTests"/>, for the same reason it exists: the pieces
/// are proven in-process by <c>AskApiModeTests</c>, and the wiring — that the key the adapter put in
/// <c>ProcessRequest.Environment</c> is the key the child's <c>Authorization</c> header carries, through
/// the real launcher's environment rules — is exactly what an in-process test cannot see.</para>
/// <para>The launcher is the real <see cref="ProcessLauncher"/>: it is the thing under test.</para>
/// </remarks>
public sealed class ApiShimScenarioTests : IDisposable
{
    private const string Key = "sk-scenario-0123456789abcdefghijklmnopqrstuv";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-api-shim-").FullName;

    public void Dispose()
    {
        TestContext.Current.TestOutputHelper?.WriteLine(_stub.Journal());
        _stub.Dispose();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Run what the ADAPTER says to run — argv and environment included — as the real binary.</summary>
    private async Task<(ProcessResult Result, ReviewerInvocation Invocation)> RunAsync(string key = Key)
    {
        var schema = Path.Combine(_dir, "schema.json");
        await File.WriteAllTextAsync(schema, FindingSchema.Json, TestContext.Current.CancellationToken);
        var invocation = new ApiRuntime("grok", _stub.Endpoint).Build(
            RoleCatalog.ArchitectureRole, "review this", _dir, schema, _dir,
            new ReviewerSettings("grok") { Model = "grok-4", ApiKey = key, Timeout = TimeSpan.FromMinutes(2) });

        // The adapter names this binary through the dotnet host when it is running from a dll; the
        // test runs the built exe directly, so the arguments from `--ask-api` on are taken from it —
        // and the ENVIRONMENT is taken whole, because that is the half under test.
        var request = new ProcessRequest(
            ServerBinary.Path,
            [.. invocation.Request.Arguments.SkipWhile(a => a != "--ask-api")],
            _dir)
        {
            Environment = invocation.Request.Environment,
            Timeout = TimeSpan.FromMinutes(2),
        };

        return (await new ProcessLauncher().RunAsync(request, TestContext.Current.CancellationToken), invocation);
    }

    [Fact]
    public async Task AReviewGoesOut_WithTheKeyFromTheEnvironment_AndItsAnswerComesBack()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, ApiEndpointStub.Completion("{\"findings\":[]}", 77, 5));

        var (result, invocation) = await RunAsync();

        result.ExitCode.Should().Be(0, $"stderr said: {result.StdErr}; {_stub.Journal()}");
        _stub.Requests.Should().ContainSingle().Which.Authorization.Should().Be($"Bearer {Key}",
            "the key the adapter put in the environment is the key the child sent");
        File.ReadAllText(invocation.OutputFile).Should().Be("{\"findings\":[]}");
        var usage = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(invocation, result);
        usage.TokensIn.Should().Be(77);
        usage.TokensOut.Should().Be(5);
    }

    [Fact]
    public async Task TheKeyIsOnNeitherStream_AndInNoArgument_OfTheRealProcess()
    {
        _stub.Answers = seen => new ApiEndpointStub.Answer(500, "echo: " + seen.Authorization);

        var (result, invocation) = await RunAsync();

        result.ExitCode.Should().Be(70);
        result.StdOut.Should().NotContain(Key);
        result.StdErr.Should().NotContain(Key).And.Contain("[redacted]");
        invocation.Request.Arguments.Should().NotContain(a => a.Contains(Key));
    }

    [Fact]
    public async Task A401FromTheRealProcess_Exits77()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(401, "{\"error\":\"bad key\"}");

        var (result, _) = await RunAsync();

        result.ExitCode.Should().Be(77, result.StdErr);
        result.StdErr.Should().Contain("vendor 'grok'").And.NotContain("bad key");
    }
}
