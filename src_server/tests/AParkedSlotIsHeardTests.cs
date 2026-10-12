using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A parked account is heard — story 5.3b of <c>todo/PLAN_every_round_is_counted.md</c>, the Team server's half.
/// </summary>
/// <remarks>
/// <para><b>What was missing.</b> Since 5.3a the server parks an exhausted slot until the instant its vendor stated, and
/// nobody outside the box could see it: the catalog counted the parked accounts and said nothing of when they come back,
/// and a job queued behind them was told only that it was queued. <c>SlotSelector.Explain</c> already wrote the sentence
/// — <i>every account is rate-limited; the first comes back at …</i> — and no status path called it (5.3a's code round).</para>
/// <para><b>What must hold.</b> The catalog says, per vendor, the earliest instant a parked account comes back — and no
/// account's name and no email; and a QUEUED job that no account can take says why in its status, in the new optional
/// <c>waiting</c> field, while a job that is not queued behind anything says nothing there. Over real HTTP, with the
/// slots parked on disk by the real <see cref="SlotRegistry"/>: no vendor process is ever started (no account is ready).</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class AParkedSlotIsHeardTests
{
    private const string AgyQuotaLine =
        "error: Individual quota reached. Please upgrade your subscription to increase your limits. Resets in 88h49m9s.";

    private static TeamServer WithSlots(params string[] slots)
    {
        var server = new TeamServer();
        var names = string.Join(", ", slots.Select(slot => $"\"{slot}\""));
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"),
            $$"""[{ "id": "codex", "runtime": "codex", "models": ["gpt-5.6-luna"], "slots": [{{names}}] }]""");

        return server;
    }

    /// <summary>Signs the slot in, then parks it on the vendor's own sentence — as the runner does on a refusal.</summary>
    private static void Park(TeamServer server, string slot, string said, DateTimeOffset nowUtc)
    {
        var registry = new SlotRegistry(server.DataDir, new JsonFileStore());
        registry.MarkSignedIn(registry.Read("codex", slot));
        registry.MarkCoolingDown(registry.Read("codex", slot), said, nowUtc);
    }

    private static DateTimeOffset Instant(string utc) =>
        DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    [Fact]
    public async Task TheCatalog_SaysWhenTheFirstParkedAccountComesBack_NamingNoAccount()
    {
        using var server = WithSlots("alice-account", "bob-account");
        var now = DateTimeOffset.UtcNow;
        Park(server, "alice-account", AgyQuotaLine, now);
        Park(server, "bob-account", "error: Individual quota reached. Resets in 2h.", now);
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var body = await client.GetStringAsync("/api/catalog");
        var vendor = (await client.GetFromJsonAsync<CatalogDto>("/api/catalog"))!.Vendors.Single();

        vendor.Slots.CoolingDown.Should().Be(2);
        vendor.Slots.ResetsUtc.Should().NotBeEmpty("the catalog says when the vendor can serve again, not only that it cannot");
        Instant(vendor.Slots.ResetsUtc).Should().BeCloseTo(now + TimeSpan.FromHours(2), TimeSpan.FromMinutes(1),
            "the EARLIEST parked account decides when the vendor comes back — bob's two hours, not alice's 88");
        body.Should().NotContain("alice-account").And.NotContain("bob-account", "the catalog names no account");
        // No EMAIL — not "no @": on a machine without codex the health note says "npm install -g @openai/codex", which
        // is an install hint, not an account (CI's Linux and macOS legs on PR #773; this machine has codex, so the note
        // never appeared here).
        body.Should().NotMatchRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+", "and carries no email");
    }

    [Fact]
    public async Task TheCatalog_SaysNoReset_WhenNoAccountIsParked()
    {
        using var server = WithSlots("a");

        var vendor = (await server.ClientFor($"dev@{TeamServer.Domain}").GetFromJsonAsync<CatalogDto>("/api/catalog"))!.Vendors.Single();

        vendor.Slots.ResetsUtc.Should().BeEmpty("a signed-out account is not parked and comes back at no instant");
    }

    [Fact]
    public async Task AQueuedJobBehindAParkedAccount_HearsWhenItCanRun()
    {
        using var server = WithSlots("a");
        Park(server, "a", AgyQuotaLine, DateTimeOffset.UtcNow);
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var accepted = await client.PostAsJsonAsync("/api/reviews", new ReviewRequestDto("codex", "gpt-5.6-luna", "review this", "Architecture", 60));
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await accepted.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!.Id;
        var status = (await client.GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{id}"))!;

        status.Status.Should().Be("queued");
        status.Waiting.Should().Contain("rate-limited").And.Contain("comes back at",
            "a job queued behind a parked account is told when it can run, not only its place in the queue");
        status.Reason.Should().BeEmpty("the failure's own words stay the failure's — a queued job has failed at nothing");
    }

    [Fact]
    public async Task AQueuedJobBehindASignedOutAccount_HearsTheCure()
    {
        using var server = WithSlots("a");
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var accepted = await client.PostAsJsonAsync("/api/reviews", new ReviewRequestDto("codex", "gpt-5.6-luna", "review this", "Architecture", 60));
        var id = (await accepted.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!.Id;

        (await client.GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{id}"))!.Waiting.Should().Contain("signed out");
    }

    [Fact]
    public async Task AJobThatIsNotQueued_SaysNothingAboutWaiting()
    {
        using var server = WithSlots("a");
        Park(server, "a", AgyQuotaLine, DateTimeOffset.UtcNow);
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var accepted = await client.PostAsJsonAsync("/api/reviews", new ReviewRequestDto("codex", "gpt-5.6-luna", "review this", "Architecture", 60));
        var id = (await accepted.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!.Id;

        (await client.DeleteAsync($"/api/reviews/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var status = (await client.GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{id}"))!;

        status.Status.Should().Be("failed");
        status.Waiting.Should().BeEmpty("a cancelled job waits for nothing");
    }
}
