using System.Text.Json.Nodes;
using Xunit;
using FluentAssertions;
using CoaiBench.Running;

namespace CoaiBench.Tests;

/// <summary>
/// A refused `resolve` is a result, not a detail to drop.
/// </summary>
/// <remarks>
/// <para>The bench resolves every finding straight after each stage — accept-all, because it
/// measures the gate and not a policy for arguing with it — and until 2026-09-05 it threw the
/// server's answer away. So the one failure the on-disk check was written for — an answer carrying
/// findings whose round record was never written, so every index points into nothing and the
/// server refuses the resolve — was invisible at the exact call where the server SAYS so.</para>
/// <para>Found by the first campaign on 0.17.1: every run came back tagged
/// <c>NOT RESOLVABLE: 0 still running, 0 pending</c>. Zero pending was correct — the bench had just
/// resolved everything — and the tag was a definition that could only be true for somebody else's
/// session, which is what yesterday's forty pending findings were.</para>
/// </remarks>
public sealed class ResolveIsCheckedTests
{
    [Fact]
    public void ARefusedResolve_IsNamed()
    {
        var refused = JsonNode.Parse("""{ "error": "finding 3 does not exist in this session" }""");

        RoundRunner.RefusalIn(refused).Should().Be("finding 3 does not exist in this session");
    }

    [Fact]
    public void AnAcceptedResolve_IsSilent() =>
        RoundRunner.RefusalIn(JsonNode.Parse("""{ "resolved": 12, "pending": 0 }""")).Should().BeEmpty();

    [Fact]
    public void NoAnswerAtAll_IsARefusalToo() =>
        RoundRunner.RefusalIn(null).Should().Contain("nothing");
}
