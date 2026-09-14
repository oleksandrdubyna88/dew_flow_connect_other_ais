using System.Net;
using System.Net.Http.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A document review is a review, and this server runs it — said out loud, rather than inherited.
/// </summary>
/// <remarks>
/// <para><b>It was already true, which is exactly why it needed a test.</b>
/// <see cref="AcceptedRoles.From"/> seeds itself from <c>RoleCatalog.Builtin.Roles</c>, and plan 4
/// put two document roles in that seed — so this server started accepting <c>DocumentReview</c> the
/// day it was next compiled, with nobody having written a line for it and nothing anywhere saying
/// so. Every test in this project derives its expectations from the same collection, so all of them
/// would have stayed green if the roles had never arrived, or if they were removed tomorrow.</para>
/// <para>These name the roles. A seed that loses them is a red test rather than a round that quietly
/// stops asking, and the end-to-end one below is the first thing in either repository to prove that
/// a document role travels the whole way instead of each half being tested against itself.</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class ADocumentRoleRunsHereTests
{
    private const string Vendors = """
        [{ "id": "codex", "runtime": "codex", "models": ["gpt-5.6-luna"], "slots": ["a"] }]
        """;

    private static TeamServer WithVendors()
    {
        var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        return server;
    }

    [Theory]
    [InlineData(RoleCatalog.DocumentRole)]
    [InlineData(RoleCatalog.DocumentSummaryRole)]
    public void TheDefaultServerKnowsBothDocumentRoles(string role)
    {
        var roles = AcceptedRoles.From([], allowAny: false);

        roles.Knows(role).Should().BeTrue();
        roles.Refusal(role).Should().BeNull();
        roles.Names.Should().Contain(role);
    }

    [Theory]
    [InlineData(RoleCatalog.DocumentRole)]
    [InlineData(RoleCatalog.DocumentSummaryRole)]
    public async Task TheCatalogNamesBothDocumentRoles(string role)
    {
        using var server = WithVendors();

        var catalog = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<CatalogDto>("/api/catalog");

        catalog!.Roles.Should().Contain(
            role,
            "coai-mcp reads this list to decide whether to send a document round here at all");
    }

    /// <summary>A document review, submitted over real HTTP exactly as the shim submits one.</summary>
    [Fact]
    public async Task ADocumentReviewIsAcceptedOverTheWire()
    {
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        // What `DocumentContext` composes: the purpose first, then the document. It arrives inside
        // the PROMPT like every other review — there is no document field on the wire, and this is
        // the test that says a new one was never needed.
        var prompt = "## What this document is for\n\nA release note.\n\n"
            + "## The document under review — notes.md\n\nWe shipped the thing.\n";

        var accepted = await client.PostAsJsonAsync(
            "/api/reviews",
            new ReviewRequestDto("codex", "gpt-5.6-luna", prompt, RoleCatalog.DocumentRole, 60));

        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await accepted.Content.ReadFromJsonAsync<ReviewAcceptedDto>();
        var polled = await client.GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{body!.Id}");
        polled!.Id.Should().Be(body.Id);
    }

    /// <summary>
    /// And it RUNS: claimed off the queue, launched, and answered.
    /// </summary>
    /// <remarks>
    /// Through <see cref="JobRunner"/> with a launcher that answers, because the real one would need
    /// a vendor CLI on the machine. What it proves is that nothing between the queue and the answer
    /// is role-specific — the server runs a CLI on <c>job.Prompt</c> and a document is simply what
    /// that prompt happens to contain.
    /// </remarks>
    [Fact]
    public async Task ADocumentReviewIsClaimedRunAndAnswered()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coai-document-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, "vendors.json"),
                """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""");
            var registry = new SlotRegistry(dir, new JsonFileStore());
            registry.MarkSignedIn(registry.Read("codex", "a"));

            var jobs = new JobStore();
            var runner = new JobRunner(
                jobs, new VendorCatalogHost(dir), registry,
                new Answering("""{"findings":[],"notes":"it reads well"}"""), new UsageLedger(dir));
            jobs.Submit(new JobRecord(
                JobId.New(), "dev@example.com", "codex", "m", RoleCatalog.DocumentRole,
                "## The document under review — notes.md\n\nWe shipped the thing.\n",
                JobStatus.Queued, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10),
                TimeSpan.FromSeconds(60)));

            (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

            var job = jobs.All().Single();
            job.Status.Should().Be(JobStatus.Done);
            job.Role.Should().Be(RoleCatalog.DocumentRole);
            job.Answer.Should().Contain("notes", "the summary comes back in `notes`, raw as always");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class Answering(string raw) : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job,
            IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
            Task.FromResult<ReviewAttempt>(new ReviewAttempt.Answered(raw, 3, 5));
    }
}
