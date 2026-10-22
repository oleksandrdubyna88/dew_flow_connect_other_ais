using System.Reflection;
using System.Text.Json.Nodes;
using CoaiMcp.Server;
using FluentAssertions;
using ModelContextProtocol.Server;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S1 of <c>todo/PLAN_a_cancelled_review_stops_its_round.md</c> (D1): the MCP request's own token reaches the four review tools, exactly as it
/// reaches <c>ask_human</c>, so a client that cancels a <c>review_*</c> call with <c>notifications/cancelled</c> stops the reviewers it started
/// instead of leaving them to run to the round's clock on the person's subscriptions.
/// </summary>
[Collection("fakecli-env")]
public sealed class TheRequestsTokenReachesTheReviewToolsTests : IAsyncLifetime
{
    private static readonly string[] ReviewTools = ["review_plan", "review_code", "review_document", "review_feature"];

    private WithdrawalRig _rig = null!;

    public async ValueTask InitializeAsync() => _rig = await WithdrawalRig.CreateAsync();

    public async ValueTask DisposeAsync() => await _rig.DisposeAsync();

    private sealed class Source(PanelService service) : IPanelServiceSource
    {
        public ValueTask<PanelService> CurrentAsync(CancellationToken ct = default) => ValueTask.FromResult(service);

        public Task Ready => Task.CompletedTask;
    }

    /// <summary>The method a tool was created from — read through the SDK's own function wrapper, the only place it is kept.</summary>
    private static MethodInfo MethodOf(McpServerTool tool)
    {
        var function = tool.GetType().GetProperty("AIFunction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tool)
            ?? throw new InvalidOperationException($"{tool.GetType().Name} exposes no AIFunction: this test reads the SDK's wrapper and the SDK changed");

        return (MethodInfo?)function.GetType().GetProperty("UnderlyingMethod")?.GetValue(function)
            ?? throw new InvalidOperationException("the wrapped function names no method");
    }

    [Fact]
    public void TheReviewTools_TakeTheRequestsToken()
    {
        var tools = Tools.All(new Source(null!)).ToDictionary(t => t.ProtocolTool.Name);

        foreach (var name in ReviewTools)
        {
            var parameters = MethodOf(tools[name]).GetParameters();

            parameters.Should().Contain(p => p.ParameterType == typeof(CancellationToken),
                $"{name} must declare the SDK's per-request token, as ask_human does, or a cancelled call cannot stop its round");
        }
    }

    [Fact]
    public void TheToken_IsNotPartOfAnyReviewToolsSchema()
    {
        // The SDK binds a CancellationToken per request: it is not an argument a client sends, so it must not be published as one.
        var tools = Tools.All(new Source(null!)).ToDictionary(t => t.ProtocolTool.Name);

        foreach (var name in ReviewTools)
        {
            tools[name].ProtocolTool.InputSchema.GetRawText().Should().NotContain("cancellationToken", $"{name}'s token is bound by the SDK, never sent");
        }
    }

    [Fact]
    public async Task ACancelledReviewPlan_CancelsItsRound()
    {
        var reviewers = _rig.PlayAll(Play.BlockThenHoldTheAnswer);
        var service = _rig.Service(reviewers);
        await service.OpenAsync(_rig.Repo, "feature");
        await using var server = await InMemoryMcpServer.StartAsync(Tools.All(new Source(service)));

        try
        {
            await server.Call(11, "review_plan", new JsonObject { ["repoPath"] = _rig.Repo, ["branch"] = "feature", ["planText"] = "a plan" });
            await reviewers.Of("codex").Arrived.Task.WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
            await server.Cancel(11);

            var killed = async () => await reviewers.Of("codex").Killed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await killed.Should().NotThrowAsync("the client took the call back, so the reviewer's token must fire within seconds, not at the round's clock");
        }
        finally
        {
            // A held reviewer ends the round; without this a failing run would wait for the in-flight handler on disposal.
            reviewers.Of("codex").Release.TrySetResult();
            reviewers.Abort();
        }
    }
}
