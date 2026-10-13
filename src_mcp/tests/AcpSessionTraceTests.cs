using CoaiMcp.Runners.Acp;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The session's trace lists are read while its pump may still be adding to them — the report reads them after the turn,
/// and a pump that outlives the turn's wait (a grandchild holding the pipe, a tail line at disposal) can still add.
/// </summary>
/// <remarks>
/// CodeRabbit on #785 (2026-10-12): <c>AcpOutcome</c> took <c>session.Refusals</c>, <c>ToolCalls</c> and <c>Reads</c> by
/// reference — the session's live <c>List&lt;T&gt;</c> fields — so an enumeration racing an <c>Add</c> threw "Collection
/// was modified" and the shim's report was never written. Every read is now a copy taken under the trace's lock.
/// </remarks>
public sealed class AcpSessionTraceTests
{
    [Fact]
    public async Task TheTraceCanBeReadWhileThePumpIsStillAddingToIt()
    {
        var steps = Enumerable.Range(0, 900)
            .Select(static i => FakeAcpAgent.ToolCall($"t{i}", "Grep", "search", "completed"))
            .Append(FakeAcpAgent.Chunk("x"));
        using var agent = new FakeAcpAgent(steps);
        using var transport = AcpTransport.Start(FakeAcpAgent.FakeCliExe, agent.Profile().Arguments, agent.Cwd);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var session = new AcpSession(transport, new FolderReadBoundary(agent.Cwd, []), deadline.Token);
        try
        {
            session.Pump();
            await session.CallAsync("initialize", AcpMessages.Initialize());
            var id = AcpMessages.Text(await session.CallAsync("session/new", AcpMessages.NewSession(agent.Cwd)), "sessionId");
            var prompt = session.CallAsync("session/prompt", AcpMessages.Prompt(id, "go"));

            var read = () =>
            {
                while (!prompt.IsCompleted)
                {
                    // A foreach, as the report's serialiser walks it: an enumerator checks the list's version; an indexer does not.
                    var chars = 0;
                    foreach (var call in session.ToolCalls)
                    {
                        chars += call.Title.Length;
                    }

                    _ = chars + session.Answer.Length;
                }
            };

            read.Should().NotThrow("a reader of the trace never sees a list the pump is changing under it");
            await prompt;
            session.ToolCalls.Should().HaveCount(900);
        }
        finally
        {
            await transport.EndAsync(static () => Task.CompletedTask);
        }
    }
}
