using System.Text.Json.Nodes;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The characterisation S1 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c> asks for: what ModelContextProtocol 2.2.0 puts on the wire
/// for a request the client cancelled with <c>notifications/cancelled</c>, whether the handler RETURNS normally after its token fired or THROWS
/// <see cref="OperationCanceledException"/>. The MCP specification says the receiver SHOULD NOT answer a cancelled request.
/// </summary>
/// <remarks>
/// Measured on 2.2.0 (2026-10-21): the SDK suppresses the response for a request whose token fired, whichever way the handler ends — so D5's
/// transport fallback is NOT needed and does not ship. The control (an uncancelled request IS answered) and the handler-finished proof are what
/// make the silence mean something: a harness that never delivered the cancel, or a handler that never ended, would be silent too.
/// </remarks>
public sealed class WhatTheSdkSendsForACancelledRequestTests
{
    private static McpServerTool HandlerThatReturns(TaskCompletionSource ended) => McpServerTool.Create(
        async (CancellationToken cancellationToken) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The shape of a handler that caught its own cancellation (the round does this: it reports instead of throwing).
            }

            ended.TrySetResult();

            return "an answer for a request nobody waits for";
        },
        new McpServerToolCreateOptions { Name = "returns" });

    private static McpServerTool HandlerThatThrows(TaskCompletionSource ended) => McpServerTool.Create(
        async (CancellationToken cancellationToken) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                ended.TrySetResult();
                throw;
            }

            return "unreachable";
        },
        new McpServerToolCreateOptions { Name = "throws" });

    private static McpServerTool Immediate() => McpServerTool.Create(() => "an answer", new McpServerToolCreateOptions { Name = "immediate" });

    [Fact]
    public async Task Control_AnUncancelledRequest_IsAnswered()
    {
        await using var server = await InMemoryMcpServer.StartAsync([Immediate()]);

        await server.Call(3, "immediate", new JsonObject());

        (await server.ReplyFor(3, TimeSpan.FromSeconds(30))).Should().NotBeNull("the harness reads what the SDK sends");
    }

    [Theory]
    [InlineData("returns")]
    [InlineData("throws")]
    public async Task ACancelledRequest_GetsNoMessageWithItsId(string tool)
    {
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await InMemoryMcpServer.StartAsync([HandlerThatReturns(ended), HandlerThatThrows(ended)]);

        await server.Call(7, tool, new JsonObject());
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await server.Cancel(7);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var reply = await server.ReplyFor(7, TimeSpan.FromSeconds(5));

        reply.Should().BeNull($"the specification says a cancelled request is not answered; the SDK sent {reply?.GetType().Name}: {reply}");
    }
}
