using System.Net;
using System.Net.Http.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The review surface as a client meets it: submit, watch, cancel — over real HTTP.
/// </summary>
/// <remarks>
/// The plan round asked for exactly this and was right to: every other test here drives the pieces
/// directly, so a route that was never registered, a filter that was never attached or a DI mistake
/// would ship with all of them green. These go through the wire.
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class ReviewEndpointTests
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

    private static ReviewRequestDto Request(string model = "gpt-5.6-luna") =>
        new("codex", model, "review this", "Architecture", 60);

    [Fact]
    public async Task SubmittingIsRefusedWithoutASignIn()
    {
        using var server = WithVendors();

        var response = await server.CreateClient().PostAsJsonAsync("/api/reviews", Request());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AModelTheCompanyDoesNotPayForIsRefusedByName()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request("gpt-9-expensive"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorDto>();
        error!.Error.Should().Contain("gpt-9-expensive").And.Contain("gpt-5.6-luna",
            "a caller should not have to guess which of the two names was wrong");
    }

    [Fact]
    public async Task AnUnknownVendorIsRefusedByName()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Vendor = "nope" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error.Should().Contain("codex");
    }

    [Fact]
    public async Task AnAcceptedReviewComesBackWithAnIdAndCanBePolled()
    {
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var accepted = await client.PostAsJsonAsync("/api/reviews", Request());

        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await accepted.Content.ReadFromJsonAsync<ReviewAcceptedDto>();
        body!.Id.Should().NotBeEmpty();

        var polled = await client.GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{body.Id}");
        polled!.Id.Should().Be(body.Id);
        polled.Status.Should().BeOneOf("queued", "running", "failed");
    }

    [Fact]
    public async Task AnotherPersonsReviewIsForbiddenRatherThanHidden()
    {
        using var server = WithVendors();
        var mine = await (await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request())).Content.ReadFromJsonAsync<ReviewAcceptedDto>();

        var theirs = await server.ClientFor($"other@{TeamServer.Domain}").GetAsync($"/api/reviews/{mine!.Id}");

        // 403, not 404. Every caller here is an authenticated member of one company and an id is an
        // unguessable <epoch>-<guid>, so confirming existence to a colleague leaks nothing worth
        // having — while hiding it left somebody chasing a missing review unable to tell "I mistyped
        // the id" from "the server dropped it". (Two reviewers, plan round.)
        theirs.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await theirs.Content.ReadFromJsonAsync<ErrorDto>())!.Error.Should().Contain("somebody else");
    }

    [Fact]
    public async Task AnIdFromAnEarlierRunSaysLostAndAnUnknownOneSaysUnknown()
    {
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var lost = await client.GetAsync($"/api/reviews/{JobId.New(JobId.Epoch - 5000)}");
        var unknown = await client.GetAsync($"/api/reviews/{JobId.New()}");

        lost.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await lost.Content.ReadFromJsonAsync<ErrorDto>())!.Error.Should().Contain("restarted");

        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await unknown.Content.ReadFromJsonAsync<ErrorDto>())!.Error.Should().Contain("no review with that id");
    }

    [Fact]
    public async Task CancellingAReviewAnswersNoContentAndThenTheJobIsFailed()
    {
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var accepted = await (await client.PostAsJsonAsync("/api/reviews", Request()))
            .Content.ReadFromJsonAsync<ReviewAcceptedDto>();

        var cancelled = await client.DeleteAsync($"/api/reviews/{accepted!.Id}");

        cancelled.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await client.GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{accepted.Id}");
        after!.Status.Should().Be("failed");
        after.Failure.Should().Be("cancelled");
    }

    [Fact]
    public async Task CancellingTwiceIsNotFound()
    {
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var accepted = await (await client.PostAsJsonAsync("/api/reviews", Request()))
            .Content.ReadFromJsonAsync<ReviewAcceptedDto>();
        await client.DeleteAsync($"/api/reviews/{accepted!.Id}");

        (await client.DeleteAsync($"/api/reviews/{accepted.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PollingAFinishedReviewReturnsAtOnceRatherThanWaiting()
    {
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var accepted = await (await client.PostAsJsonAsync("/api/reviews", Request()))
            .Content.ReadFromJsonAsync<ReviewAcceptedDto>();
        await client.DeleteAsync($"/api/reviews/{accepted!.Id}");

        var started = DateTimeOffset.UtcNow;
        await client.GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{accepted.Id}?wait=25");

        // Waiting for a "change" that has already happened is how a client reconnecting after a blip
        // sits for twenty-five seconds staring at an answer the server already has. (Plan round.)
        (DateTimeOffset.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}

/// <summary>The runner's rules, driven against a vendor that answers however the test says.</summary>
public sealed class JobRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-runner-" + Guid.NewGuid().ToString("N"));

    public JobRunnerTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            Path.Combine(_dir, "vendors.json"),
            """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a", "b"] }]""");

        // Both accounts signed in. Without this the runner correctly refuses to start anything —
        // an account nobody has ever signed in is NeedsSignIn, and SlotSelector will not pick it.
        // That is the production behaviour; it just has to be arranged for in a test.
        var registry = new SlotRegistry(_dir, new JsonFileStore());
        foreach (var name in new[] { "a", "b" })
        {
            registry.MarkSignedIn(registry.Read("codex", name));
        }
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Fake(params ReviewerOutcome[] outcomes) : IReviewLauncher
    {
        private int _calls;

        public List<string> SlotsUsed { get; } = [];

        public List<IReadOnlyDictionary<string, string?>> Environments { get; } = [];

        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job,
            IReadOnlyDictionary<string, string?> environment, CancellationToken ct)
        {
            SlotsUsed.Add(slot.Name);
            Environments.Add(environment);
            var outcome = outcomes[Math.Min(_calls++, outcomes.Length - 1)];

            return Task.FromResult(new ReviewAttempt(outcome, outcome is ReviewerOutcome.Ok ? "the answer" : "", 7, 11));
        }
    }

    private (JobStore Jobs, JobRunner Runner, Fake Launcher) Build(params ReviewerOutcome[] outcomes)
    {
        var jobs = new JobStore();
        var launcher = new Fake(outcomes);
        var catalog = new VendorCatalogHost(_dir);
        var slots = new SlotRegistry(_dir, new JsonFileStore());

        return (jobs, new JobRunner(jobs, catalog, slots, launcher, new UsageLedger(_dir)), launcher);
    }

    private static JobRecord Job(string email = "dev@example.com") =>
        new(JobId.New(), email, "codex", "m", "Architecture", "p", JobStatus.Queued,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(10), TimeSpan.FromSeconds(60));

    [Fact]
    public async Task AGoodRunComesBackWithTheVendorsRawAnswer()
    {
        var (jobs, runner, launcher) = Build(new ReviewerOutcome.Ok(null!, false, Usage.None));
        jobs.Submit(Job());

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Done);
        job.Answer.Should().Be("the answer", "parsing stays in the client, so the server returns it raw");
        job.TokensIn.Should().Be(7);
        launcher.Environments.Single()["HOME"].Should().Contain("codex");
    }

    [Fact]
    public async Task ARateLimitParksTheAccountAndTheJobSaysWhy()
    {
        var (jobs, runner, _) = Build(
            new ReviewerOutcome.RateLimited("You've hit your session limit · resets 9:30pm (UTC)"));
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.Failure.Should().Be(FailureKind.RateLimited);
        job.Reason.Should().Contain("session limit");
        new SlotRegistry(_dir, new JsonFileStore()).Read("codex", job.Slot).CooldownUntilUtc.Should()
            .NotBeNull("the account that refused must not take the next job straight away");
    }

    [Fact]
    public async Task AVendorThatCrashesEndsTheJobRatherThanLeavingItRunning()
    {
        var (jobs, runner, _) = Build(new ReviewerOutcome.NonZeroExit(3, "it exploded"));
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Failed);
        job.Failure.Should().Be(FailureKind.NonZeroExit);
        job.Reason.Should().Contain("exploded");
    }

    [Fact]
    public async Task AnExceptionFromTheInfrastructureStillEndsTheJob()
    {
        // The plan round's point: the six outcomes describe what the VENDOR does, and nothing covered
        // what the machinery around it does. A job stuck Running is a caller polling for ever.
        var jobs = new JobStore();
        var runner = new JobRunner(
            jobs, new VendorCatalogHost(_dir), new SlotRegistry(_dir, new JsonFileStore()), new Exploding());
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.IsTerminal.Should().BeTrue();
        job.Failure.Should().Be(FailureKind.NotStarted);
    }

    [Fact]
    public async Task NothingRunsWhenThereIsNothingQueued()
    {
        var (_, runner, launcher) = Build(new ReviewerOutcome.Ok(null!, false, Usage.None));

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeFalse();

        launcher.SlotsUsed.Should().BeEmpty();
    }

    [Fact]
    public async Task ASuccessWritesOneUsageLineCarryingTheCallersEmail()
    {
        var (jobs, runner, _) = Build(new ReviewerOutcome.Ok(null!, false, Usage.None));
        jobs.Submit(Job("someone@example.com"));

        await runner.PumpAsync("codex", CancellationToken.None);

        var lines = File.ReadAllLines(Path.Combine(_dir, "usage.jsonl"));
        lines.Should().ContainSingle();
        lines[0].Should().Contain("someone@example.com").And.Contain("codex");
    }

    private sealed class Exploding : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job,
            IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
            throw new InvalidOperationException("the machinery broke");
    }
}
