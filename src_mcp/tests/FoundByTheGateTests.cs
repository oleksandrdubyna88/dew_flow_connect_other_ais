using CoaiMcp.Core.Rounds;
using System.Text;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The defects this product's own review gate found in it, one test each.
/// </summary>
/// <remarks>
/// <para>Five models reviewed commit <c>2b7d3ab</c> under the operator's settings on 2026-09-02 and
/// between them named eight real defects nobody knew about
/// (<c>research/RESULTS_findings_that_are_worth_something.md</c>). Three of them are on this side of
/// the wire; a fourth arrived from CI the next morning, which is the same class of thing found by a
/// different reviewer.</para>
/// <para>Each test here was watched fail against the unfixed code, and the failure message is
/// recorded beside it.</para>
/// </remarks>
public class FoundByTheGateTests
{
    // ---------------------------------------------------------------- flash and luna, together

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"a bare string\"")]
    [InlineData("null")]
    [InlineData("true")]
    public void AValidJsonAnswerThatIsNotAnObject_IsNoAnswer_NotACrash(string response)
    {
        // `JsonDocument.Parse` succeeds for every one of these, and `TryGetProperty` on a root that
        // is not an object throws `InvalidOperationException` — which the `catch (JsonException)`
        // below it does not catch. An engine answering `[]` took the whole round down instead of
        // being reported as an unparseable reviewer.
        //
        // RED: System.InvalidOperationException : The requested operation requires an element of
        //      type 'Object', but the target element has type 'Array'.
        var (answer, usage) = LocalAsk.ReadResponse(response);

        answer.Should().BeNull();
        usage.TokensIn.Should().Be(0);
        usage.TokensOut.Should().Be(0);
    }

    // ---------------------------------------------------------------- qwen, luna and sonnet

    [Fact]
    public void TheSeedIsAFunctionOfThePromptBytes_NotOfThisProcess()
    {
        // `prompt.GetHashCode()` is randomised per process by .NET, so the seed differed on every
        // run — under a comment claiming "the same round asked twice is the same request, which is
        // what makes a local reviewer reproducible at all". The property cannot be observed from
        // inside one process, so it is pinned instead: the seed must equal an independent FNV-1a
        // over the UTF-8 bytes, computed here from the algorithm rather than from the code.
        //
        // RED: Expected LocalAsk.SeedFor("…") to be 40639, but found 5195 (and a different number
        //      on the next run).
        const string prompt = "Review this diff. It is the same prompt as last time.";

        LocalAsk.SeedFor(prompt).Should().Be(ReferenceSeed(prompt));
    }

    [Fact]
    public void TwoDifferentPromptsAreTwoDifferentRequests()
    {
        LocalAsk.SeedFor("one").Should().NotBe(LocalAsk.SeedFor("two"));
    }

    [Fact]
    public void TheSeedIsAlwaysAValueTheWireAccepts()
    {
        // Negative seeds are rejected by the engines, and `Math.Abs(int.MinValue)` throws — which
        // is reachable from a hash and was one `OverflowException` away from a dead round.
        foreach (var prompt in new[] { "", "a", new string('x', 10_000), new string((char)0, 1) + "￿" })
        {
            LocalAsk.SeedFor(prompt).Should().BeInRange(0, 99_999);
        }
    }

    /// <summary>FNV-1a 32-bit, written from the algorithm rather than from the code under test.</summary>
    private static int ReferenceSeed(string prompt)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(prompt))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return (int)(hash % 100_000u);
    }

    // ---------------------------------------------------------------- flash

    [Theory]
    [InlineData("http://127.0.0.1:11434", "http://127.0.0.1:11434/v1")]
    [InlineData("http://127.0.0.1:11434/", "http://127.0.0.1:11434/v1")]
    [InlineData("http://127.0.0.1:11434/v1", "http://127.0.0.1:11434/v1")]
    [InlineData("http://127.0.0.1:11434/v1/", "http://127.0.0.1:11434/v1")]
    [InlineData("http://box.lan:8000/v1", "http://box.lan:8000/v1")]
    public void AnEndpointReachesTheWireAsTheOpenAiBase(string typed, string expected)
    {
        // The panel normalises to `/v1` for the PROBE only. An endpoint typed without it therefore
        // answered discovery at the server root and 404'd at review time — the model list looked
        // healthy and every round failed.
        //
        // RED: Expected args to contain "http://127.0.0.1:11434/v1", but found
        //      {"--ask-local", "--endpoint", "http://127.0.0.1:11434", …}.
        var invocation = Build(typed);

        var endpoint = invocation.Request.Arguments
            .SkipWhile(a => a != "--endpoint").Skip(1).First();
        endpoint.Should().Be(expected);
    }

    [Fact]
    public void AnEmptyEndpointStillMeansTheDefaultOne()
    {
        var endpoint = Build("").Request.Arguments
            .SkipWhile(a => a != "--endpoint").Skip(1).First();

        endpoint.Should().Be(LocalRuntime.DefaultEndpoint);
    }

    private static ReviewerInvocation Build(string baseUrl)
    {
        var dir = TempDir.For("coai-endpointtest-").Path;
        try
        {
            return new LocalRuntime("local", baseUrl).Build(
                RoleCatalog.ArchitectureRole,
                "the prompt",
                dir,
                Path.Combine(dir, "schema.json"),
                Path.Combine(dir, "answers"),
                new ReviewerSettings("local") { Model = "qwen", Timeout = TimeSpan.FromMinutes(10) });
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // ---------------------------------------------------------------- luna, alone

    [Theory]
    [InlineData("")]
    [InlineData("a-file-that-is-not-there.json")]
    public async Task AMissingFindingSchemaRefusesTheRequest_RatherThanSendingAnUnconstrainedOne(string schemaArg)
    {
        // `{}` was substituted for an absent schema file, which is the SAME unconstrained request
        // that `LocalAsk.RequestBody` was corrected to refuse — the fix landed in the callee and
        // the caller kept its own copy of the decision. Every local model tried answers an
        // unconstrained request with an invented shape, so it buys a full generation and an
        // unparseable round.
        //
        // RED: the request was built and POSTed; the shim came back 70 after a connection failure
        //      instead of 65 before spending anything.
        var dir = TempDir.For("coai-schematest-").Path;
        try
        {
            var promptFile = Path.Combine(dir, "p.txt");
            await File.WriteAllTextAsync(promptFile, "review this", TestContext.Current.CancellationToken);
            var schema = schemaArg.Length == 0 ? "" : Path.Combine(dir, schemaArg);

            var code = await Program.AskLocalAsync(
            [
                "--ask-local",
                "--endpoint", "http://127.0.0.1:1/v1", // nothing listens here, and nothing should be sent
                "--prompt-file", promptFile,
                "--schema-file", schema,
                "--out", Path.Combine(dir, "answer.json"),
                "--timeout-seconds", "5",
            ]);

            code.Should().Be(65, "an unconstrained request is refused before the card is spent");
            File.Exists(Path.Combine(dir, "answer.json")).Should().BeFalse();
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // ---------------------------------------------------------------- CI, the next morning

    [Fact]
    public async Task ABudgetThatRunsOutMidLoop_IsSilence_NotAnException()
    {
        // Observed on the linux-x64 release runner: the loop tests `UtcNow < deadline`, then reads
        // the clock AGAIN to size the wait, and between the two reads the budget can go negative —
        // `Task.Delay` then throws ArgumentOutOfRangeException out of a `call_human` that had
        // merely run out of time.
        //
        // RED: System.ArgumentOutOfRangeException : The value needs to translate in milliseconds to
        //      -1 (signifying an infinite timeout), 0, or a positive integer … (Parameter 'delay')
        var dir = TempDir.For("coai-escalationtest-").Path;
        try
        {
            var escalations = new Escalations(dir, TimeSpan.FromMilliseconds(5));
            var question = new EscalationQuestion(
                "q1", "s-1", "D:/repo", "main", "Ship anyway?", "Ship anyway?", "en", "", [],
                DateTime.UtcNow.ToString("O"));

            for (var i = 0; i < 50; i++)
            {
                var outcome = await escalations.AskAsync(
                    question with { Id = $"q{i}" },
                    TimeSpan.FromMilliseconds(1),
                    TestContext.Current.CancellationToken);

                outcome.Should().BeOfType<EscalationOutcome.NoAnswerYet>();
            }
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3000, 2000)]
    public void TheNextWaitIsNeverNegativeAndNeverLongerThanTheBudget(int remainingMs, int expectedMs)
    {
        Escalations.NextWait(TimeSpan.FromMilliseconds(remainingMs), TimeSpan.FromSeconds(2))
            .Should().Be(TimeSpan.FromMilliseconds(expectedMs));
    }
}
