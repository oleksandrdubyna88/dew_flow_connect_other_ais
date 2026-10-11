using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The deploy canary is a canary (story 3.5 of <c>todo/PLAN_every_round_is_counted.md</c>): the one real review per
/// vendor the release script sends is said as its OWN kind, so a spending report tells the deploy's check apart from
/// a person's review. Until now it was written <c>review</c>, indistinguishable from one except by its token's email
/// (the paid-path census of <c>research/RESULTS_vendor_quota_gauges.md</c>, row 11).
/// </summary>
/// <remarks>
/// Keeping it out of the ROUND count is story 5.1's: rounds do not exist on the server before epic 5. What lands here is
/// the kind itself — accepted, written and read back as itself, and reported apart by kind.
/// </remarks>
public sealed class TheCanaryIsACanaryTests : IDisposable
{
    private static readonly AcceptedRoles Shipped = AcceptedRoles.From([], allowAny: false);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-canary-" + Guid.NewGuid().ToString("N"));

    public TheCanaryIsACanaryTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void TheCanary_IsAReviewTheDeploySends_SaidAsItsOwnKind()
    {
        JobKinds.TryRead("canary", out var kind).Should().BeTrue("the release script says it");
        JobKinds.Wire(kind).Should().Be("canary");
        JobKinds.Refusal("canary", "PlanCritique", Shipped).Should().BeNull("a canary is a review: it carries a role");
        JobKinds.Refusal("canary", null, Shipped).Should().Contain("needs a role", "and like a review it is refused without one");
        UsageKinds.IsKnown("canary").Should().BeTrue("the ledger can name every kind the server writes");
    }

    [Fact]
    public void TheCanary_IsReadBackAsItsOwnKind_AndReportedApart()
    {
        File.WriteAllText(
            Path.Combine(_dir, "usage.jsonl"),
            """{"utc":"2026-10-10T10:00:00.0000000Z","provider":"codex","model":"m","role":"PlanCritique","stage":"TeamServer","seconds":4.1,"tokensIn":900,"tokensOut":12,"outcome":"ok","email":"canary@example.com","kind":"canary"}""" + "\n"
            + """{"utc":"2026-10-10T10:01:00.0000000Z","provider":"codex","model":"m","role":"Architecture","stage":"TeamServer","seconds":40,"tokensIn":15000,"tokensOut":300,"outcome":"ok","email":"dev@example.com","kind":"review"}""" + "\n");
        var at = new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

        var lines = new UsageReader(_dir).Read(new UsageRange(at.AddMinutes(-1), at.AddMinutes(5))).Lines;

        JobKinds.Wire(lines[0].Kind).Should().Be("canary", "a canary line read back as a review is the person's spend it is not");
        UsageTotals.ByKind(lines).Select(k => k.Kind).Should().BeEquivalentTo(["canary", "review"],
            "the deploy's check is reported apart from the people's reviews");
    }
}
