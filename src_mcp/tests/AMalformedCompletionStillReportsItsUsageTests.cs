using CoaiMcp.Core.Api;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A completion that is valid JSON with a malformed SHAPE — a <c>null</c> choice, a message that is a string,
/// a count that is a string — is read as "no usable content", never thrown out of the reader, and the usage it
/// did report still reaches the parent (the code round's accepted finding, codex, on the calibration branch).
/// </summary>
/// <remarks>
/// <c>JsonElement.TryGetProperty</c> on a value that is not an object, and <c>TryGetInt64</c> on one that is not
/// a number, THROW <see cref="InvalidOperationException"/>; the reader caught only <c>JsonException</c>. The
/// shim then died before it printed the usage line, so a billed call was filed as free — the defect §9.23–29
/// fixed for every OTHER failure shape.
/// </remarks>
public sealed class AMalformedCompletionStillReportsItsUsageTests : IDisposable
{
    private const string Key = "sk-malformed-0123456789abcdefghijklmnopqrstu";
    private const string Usage = "\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":80}";

    private readonly ApiEndpointStub _stub = ApiEndpointStub.Start();
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-malformed-").FullName;

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

    [Theory]
    [InlineData("{\"choices\":[null]," + Usage + "}")]
    [InlineData("{\"choices\":[42]," + Usage + "}")]
    [InlineData("{\"choices\":[{\"message\":\"hello\",\"finish_reason\":\"stop\"}]," + Usage + "}")]
    [InlineData("{\"choices\":[{\"message\":null}]," + Usage + "}")]
    public void A_malformed_choice_is_no_content_and_keeps_the_usage(string response)
    {
        var answer = CompletionReader.Read(response);

        answer.Content.Should().BeNull("a choice that is not an object has no message to read");
        answer.Usage.TokensIn.Should().Be(1200, "the vendor reported the usage, and the call was billed");
        answer.Usage.TokensOut.Should().Be(80);
    }

    [Fact]
    public void A_count_that_is_not_a_number_is_zero_never_a_throw()
    {
        var answer = CompletionReader.Read(
            "{\"choices\":[{\"message\":{\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":\"1200\",\"completion_tokens\":80,"
            + "\"prompt_tokens_details\":{\"cached_tokens\":\"9\"}}}");

        answer.Content.Should().Be("ok");
        answer.Usage.TokensIn.Should().Be(0, "a string is not a count the vendor reported");
        answer.Usage.TokensOut.Should().Be(80);
    }

    /// <summary>Through the REAL binary: the shim answers 70 with its usage line, rather than crashing without one.</summary>
    [Fact]
    public async Task The_shim_reports_the_billed_usage_of_a_malformed_answer_and_exits_70()
    {
        _stub.Answers = _ => new ApiEndpointStub.Answer(200, "{\"choices\":[null]," + Usage + "}");
        var schema = Path.Combine(_dir, "schema.json");
        File.WriteAllText(schema, FindingSchema.Json);
        var invocation = new ApiRuntime("grok", _stub.Endpoint).Build(
            RoleCatalog.FeatureRole, "review this", _dir, schema, _dir,
            new ReviewerSettings("grok") { Model = "grok-4.7", ApiKey = Key, Timeout = TimeSpan.FromMinutes(2) });

        var result = await new ProcessLauncher().RunAsync(
            new ProcessRequest(ServerBinary.Path, [.. invocation.Request.Arguments.SkipWhile(a => a != "--ask-api")], _dir)
            {
                Environment = invocation.Request.Environment,
                Timeout = TimeSpan.FromMinutes(1),
            },
            TestContext.Current.CancellationToken);

        result.ExitCode.Should().Be(70, $"a malformed answer is a failed review, not a crash; stderr said: {result.StdErr}");
        var usage = new ApiRuntime("grok", _stub.Endpoint).ReadUsage(invocation, result);
        usage.TokensIn.Should().Be(1200, "the billed call's usage crossed the process boundary");
        usage.NotCaptured.Should().BeFalse();
    }
}
