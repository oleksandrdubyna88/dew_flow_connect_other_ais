using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>--ask-local</c> refuses an answer its engine CUT at the token ceiling, and says so with the ceiling, the tokens and
/// the effort it sent (todo/PLAN_local_effort_reaches_every_local_launch.md, S1.2).
/// </summary>
/// <remarks>
/// <para>Found 2026-10-13: a Gemma4 check generated exactly 8 192 tokens of thinking and no content. Ollama answered
/// <c>finish_reason: "length"</c>; the shim did not read it and said "the local engine returned no message content:
/// {raw json}", which named neither the ceiling nor the missing <c>reasoning_effort</c> that caused it. A cut answer WITH
/// content was worse: it was written as the answer and parsed — the defect the api shim already refuses.</para>
/// <para>Driven end to end through the real shim against a loopback endpoint answering recorded Ollama bodies. In the
/// <c>fakecli-env</c> collection, which runs alone: it swaps <see cref="Console"/>'s streams and the engine lease's
/// process-wide directory, both of which another class running beside it would see.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ALocalAnswerCutAtItsCeilingIsNamedTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-localcut-").FullName;
    private readonly string _leases = Directory.CreateTempSubdirectory("coai-localcut-leases-").FullName;
    private readonly string _previousLeases = EngineLease.Directory;
    private readonly ApiEndpointStub _engine = ApiEndpointStub.Start();

    public ALocalAnswerCutAtItsCeilingIsNamedTests() => EngineLease.Directory = _leases;

    public void Dispose()
    {
        _engine.Dispose();
        EngineLease.Directory = _previousLeases;
        foreach (var dir in (string[])[_dir, _leases])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is not a failing test.
            }
        }
    }

    /// <summary>What Ollama's OpenAI route answered on 2026-10-13: everything in <c>reasoning</c>, nothing in content, cut.</summary>
    private static string Ollama(string content, string finish, long completionTokens) =>
        "{\"id\":\"chatcmpl-965\",\"object\":\"chat.completion\",\"model\":\"Gemma4-26B-A4B-Uncensored_vk128:latest\","
        + "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + JsonSerializer.Serialize(content)
        + ",\"reasoning\":\"*   Instruction: reply with the two lines...\"},\"finish_reason\":\"" + finish + "\"}],"
        + "\"usage\":{\"prompt_tokens\":1437,\"completion_tokens\":" + completionTokens + ",\"total_tokens\":" + (1437 + completionTokens) + "}}";

    private string AnswerFile => Path.Combine(_dir, "answer.json");

    private async Task<(int Code, string Out, string Err)> AskAsync(string body, params string[] extra)
    {
        _engine.Answers = _ => new ApiEndpointStub.Answer(200, body);
        var prompt = Path.Combine(_dir, "prompt.txt");
        var schema = Path.Combine(_dir, "schema.json");
        await File.WriteAllTextAsync(prompt, "health check", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(schema, """{"type":"object","properties":{"answer":{"type":"string"}}}""", TestContext.Current.CancellationToken);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var (wasOut, wasErr) = (Console.Out, Console.Error);
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var code = await Program.AskLocalAsync(
            [
                "--ask-local", "--endpoint", _engine.Endpoint, "--model", "gemma4",
                "--prompt-file", prompt, "--schema-file", schema, "--out", AnswerFile,
                "--timeout-seconds", "30", "--max-tokens", "8192", .. extra,
            ]);

            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(wasOut);
            Console.SetError(wasErr);
        }
    }

    [Fact]
    public async Task AThinkingModelCutBeforeItAnswered_IsRefused_NamingTheCeilingAndTheMissingEffort()
    {
        var (code, stdout, stderr) = await AskAsync(Ollama(string.Empty, "length", 8192));

        code.Should().Be(70, stderr);
        File.Exists(AnswerFile).Should().BeFalse("nothing was answered");
        stderr.Should().Contain("cut at the token limit (max_tokens 8192)", "the ceiling the request carried is the number a person raises")
            .And.Contain("8192 tokens generated")
            .And.Contain("0 characters of content arrived")
            .And.Contain("sent no reasoning_effort", "the missing effort is the cause on a thinking model, and the sentence must say so");
        stdout.Should().Contain("\"tokensOut\":8192", "a refused generation still spent the card, and its usage is reported before it is judged");
    }

    [Fact]
    public async Task ACutAnswerWithContent_IsRefused_NotWrittenAsTheAnswer()
    {
        // Schema-whole yet cut: the shape of a clean review nobody gave.
        var (code, _, stderr) = await AskAsync(Ollama("""{"status":"SECURE","findings":[]}""", "length", 8192), "--reasoning-effort", "low");

        code.Should().Be(70, stderr);
        File.Exists(AnswerFile).Should().BeFalse("a fragment where the executor reads an answer is the defect");
        stderr.Should().Contain("reasoning_effort was low");
    }

    [Fact]
    public async Task AnEffortOfEngine_IsSaidAsNoneSent_BecauseNoneWas()
    {
        // `engine` is the panel's documented way to send NOTHING (ApiDialect.EffortToSend drops it from the body), so
        // the sentence must not claim an effort the request never carried (the code round's correctness review).
        var (code, _, stderr) = await AskAsync(Ollama(string.Empty, "length", 8192), "--reasoning-effort", "engine");

        code.Should().Be(70, stderr);
        stderr.Should().Contain("sent no reasoning_effort").And.NotContain("reasoning_effort was engine");
    }

    [Fact]
    public async Task AnAnswerThatStopped_IsWritten_AsBefore()
    {
        var (code, stdout, stderr) = await AskAsync(Ollama("marker: ok\ncanary: CANNOT", "stop", 12), "--reasoning-effort", "none");

        code.Should().Be(0, stderr);
        File.ReadAllText(AnswerFile).Should().Be("marker: ok\ncanary: CANNOT");
        stdout.Should().Contain("\"tokensOut\":12");
    }

    [Fact]
    public void TheSharedSentence_IsTheApiShimsSentenceToTheByte()
    {
        // The api shim's cut sentence moved into CompletionReader.CutSentence; this is the text it composed inline before
        // the move, so the move changed no word of what an api row's failure says.
        var answer = new Core.Api.ChatAnswer("{\"", new Core.Findings.Usage(900, 4096, null, TokensReasoning: 4000), "length", 4000);

        Core.Api.CompletionReader.CutSentence("https://api.example/v1", answer, Core.Api.ApiDialects.Local, 4096, "raise the ceiling or bound the reasoning")
            .Should().Be("the answer from https://api.example/v1 was cut at the token limit (max_tokens 4096): 4096 tokens generated "
                         + "(4000 reasoning tokens), 2 characters of content arrived — raise the ceiling or bound the reasoning; the fragment was not kept");
    }

    [Fact]
    public async Task TheCutSentence_IsNeitherARateLimitNorAKnownVendorRefusal()
    {
        var (_, _, stderr) = await AskAsync(Ollama(string.Empty, "length", 8192));

        RateLimit.Hit(new ProcessResult(70, string.Empty, stderr, TimedOut: false)).Should().BeFalse(
            "a cut is not worth an automatic retry — the same request under the same ceiling cuts again");
        VendorDiagnosis.Classify(stderr).Should().BeNull("it is this product's own sentence, read as an exit with what it said");
    }
}
