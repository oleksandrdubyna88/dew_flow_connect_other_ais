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

    [Fact]
    public async Task AChatIsAcceptedOverTheWireWithNoRole()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = null, Kind = "chat" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task AChatCarryingAReviewRoleIsRefusedOverTheWire()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Kind = "chat" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("carries no review role");
    }

    [Fact]
    public async Task AClientThatSendsNoKindIsStillAcceptedWithNoRole()
    {
        // The row every installed copy depends on. `coai-mcp --ask-remote` and the extension's chat
        // both predate this field; refusing them would take every round and every conversation down
        // at once, on the day this server was deployed.
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = null });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task AKindTheServerDoesNotKnowIsRefusedByName()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Kind = "conversation" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("not a kind of job").And.Contain("chat");
    }

    [Fact]
    public async Task RepeatingASubmitWithOneKeyAnswersWithTheSameReview()
    {
        // The failure this is for: the POST arrives, the job is accepted, and the response never
        // comes back. The person presses send again, and without this the server is holding two
        // jobs for one question on an account where a slot is the scarcest thing there is.
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var body = Request() with { IdempotencyKey = "turn-1" };

        var first = await (await client.PostAsJsonAsync("/api/reviews", body))
            .Content.ReadFromJsonAsync<ReviewAcceptedDto>();
        var second = await client.PostAsJsonAsync("/api/reviews", body);

        second.StatusCode.Should().Be(HttpStatusCode.Accepted, "a retry is not an error");
        (await second.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!.Id.Should().Be(first!.Id);
    }

    [Fact]
    public async Task OneKeyUsedForTwoDifferentQuestionsIsAConflict()
    {
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        await client.PostAsJsonAsync("/api/reviews", Request() with { IdempotencyKey = "turn-1" });
        var second = await client.PostAsJsonAsync(
            "/api/reviews",
            Request() with { IdempotencyKey = "turn-1", Prompt = "a different question entirely" });

        // 409, not 400: the request is well formed and so is the key. What is wrong is that the two
        // disagree with something this server already accepted, and the fix is a NEW key.
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("turn-1").And.Contain("use a new key");
    }

    [Fact]
    public async Task AMalformedIdempotencyKeyIsRefusedBeforeAnythingIsQueued()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { IdempotencyKey = "../../etc/passwd" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("idempotency key");
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

    /// <summary>
    /// Hands the runner whole ATTEMPTS, because that is what a launcher returns.
    /// </summary>
    /// <remarks>
    /// It used to take bare outcomes and dress each one as an attempt itself — including a
    /// <c>ReviewerOutcome.Ok(null!, …)</c> for the success case, a value the real launcher cannot
    /// build because it holds the vendor's raw text and never a parsed review. That fake success
    /// was the only success in the suite, so the runner's success arm was covered here and
    /// unreachable in production for the whole life of the server.
    /// </remarks>
    private sealed class Fake(params ReviewAttempt[] attempts) : IReviewLauncher
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

            return Task.FromResult(attempts[Math.Min(_calls++, attempts.Length - 1)]);
        }
    }

    private (JobStore Jobs, JobRunner Runner, Fake Launcher) Build(params ReviewAttempt[] attempts)
    {
        var jobs = new JobStore();
        var launcher = new Fake(attempts);
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
        var (jobs, runner, launcher) = Build(new ReviewAttempt.Answered("the answer", 7, 11));
        jobs.Submit(Job());

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeTrue();

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Done);
        job.Answer.Should().Be("the answer", "parsing stays in the client, so the server returns it raw");
        job.TokensIn.Should().Be(7);
        launcher.Environments.Single()["HOME"].Should().Contain("codex");
    }

    [Fact]
    public async Task ARateLimitParksThatAccountAndTriesTheOtherOne()
    {
        // With a second account signed in and idle, failing the review outright told a caller "rate
        // limited" while an account that could have run it sat there — which defeats the whole point
        // of configuring more than one. (codex, code round.)
        var (jobs, runner, _) = Build(new ReviewAttempt.Failed(new ReviewerOutcome.RateLimited("You've hit your session limit · resets 9:30pm (UTC)")));
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Queued, "another account can still serve it");
        job.Reason.Should().Contain("rate-limited").And.Contain("trying");
        new SlotRegistry(_dir, new JsonFileStore()).Read("codex", "a").CooldownUntilUtc.Should()
            .NotBeNull("the account that refused must not take the next job straight away");
    }

    [Fact]
    public async Task WhenEveryAccountIsRateLimitedTheReviewFailsWithTheVendorsWords()
    {
        var (jobs, runner, _) = Build(new ReviewAttempt.Failed(new ReviewerOutcome.RateLimited("session limit reached")));
        jobs.Submit(Job());

        // Once for slot a, once for slot b. After the second there is nowhere left to rotate to.
        await runner.PumpAsync("codex", CancellationToken.None);
        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Failed);
        job.Failure.Should().Be(FailureKind.RateLimited);
        job.Reason.Should().Contain("session limit reached");
    }
    [Fact]
    public async Task AJobIsNeverRotatedBackOntoAnAccountThatAlreadyRefusedIt()
    {
        // Without this the job cycles a and b for the whole ten-minute queue window, re-asking
        // accounts it already knows are exhausted, and fails anyway — the only difference being how
        // long the caller waited to be told. (Two reviewers, second code round.)
        var (jobs, runner, launcher) = Build(new ReviewAttempt.Failed(new ReviewerOutcome.RateLimited("limit")));
        jobs.Submit(Job());

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await runner.PumpAsync("codex", CancellationToken.None);
        }

        launcher.SlotsUsed.Should().HaveCount(2, "two accounts, so two attempts and then it stops");
        launcher.SlotsUsed.Should().OnlyHaveUniqueItems();
        jobs.All().Single().Failure.Should().Be(FailureKind.RateLimited);
    }

    [Fact]
    public async Task AVendorThatCrashesEndsTheJobRatherThanLeavingItRunning()
    {
        var (jobs, runner, _) = Build(new ReviewAttempt.Failed(new ReviewerOutcome.NonZeroExit(3, "it exploded")));
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Failed);
        job.Failure.Should().Be(FailureKind.NonZeroExit);
        job.Reason.Should().Contain("exploded");
    }

    /// <summary>
    /// A CLI that ran and said nothing must not tell the person it never started.
    /// </summary>
    /// <remarks>
    /// Raised by codex on this change's plan round, and the right half of it: mapping the blank
    /// answer to <c>Unparseable</c> inside the launcher is worth nothing if the status a caller
    /// READS still says <c>not_started</c>. `not_started` sends somebody to look at accounts,
    /// executables and sign-ins; the CLI started, it produced an empty envelope, and those are two
    /// different mornings.
    /// </remarks>
    [Fact]
    public async Task AVendorThatRanAndSaidNothing_IsNotReportedAsNeverStarted()
    {
        var (jobs, runner, _) = Build(new ReviewAttempt.Failed(
            new ReviewerOutcome.Unparseable("the vendor exited cleanly without writing an answer", Usage.None)));
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.Status.Should().Be(JobStatus.Failed);
        job.Failure.Should().Be(FailureKind.UnparseableByVendor);
        job.Failure.Should().NotBe(FailureKind.NotStarted);
        job.Reason.Should().Contain("without writing an answer");
    }

    /// <summary>
    /// A vendor that answered unusably still BILLED for it.
    /// </summary>
    /// <remarks>
    /// `Unparseable` is the one failure that carries usage, and the transition discarded it — so the
    /// spending report said a persistently broken vendor cost the company nothing, which is exactly
    /// the vendor somebody would want to find in that report. Raised by CodeRabbit on PR 93.
    /// </remarks>
    [Fact]
    public async Task AVendorThatBilledForAnUnusableAnswer_StillHasItsTokensRecorded()
    {
        var (jobs, runner, _) = Build(new ReviewAttempt.Failed(
            new ReviewerOutcome.Unparseable("nothing usable came back", new Usage(4_211, 77, null))));
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var job = jobs.All().Single();
        job.TokensIn.Should().Be(4_211);
        job.TokensOut.Should().Be(77);
        File.ReadAllLines(Path.Combine(_dir, "usage.jsonl"))[0].Should().Contain("4211");
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
        var (_, runner, launcher) = Build(new ReviewAttempt.Answered("the answer", 7, 11));

        (await runner.PumpAsync("codex", CancellationToken.None)).Should().BeFalse();

        launcher.SlotsUsed.Should().BeEmpty();
    }

    [Fact]
    public async Task ASuccessWritesOneUsageLineCarryingTheCallersEmail()
    {
        var (jobs, runner, _) = Build(new ReviewAttempt.Answered("the answer", 7, 11));
        jobs.Submit(Job("someone@example.com"));

        await runner.PumpAsync("codex", CancellationToken.None);

        var lines = File.ReadAllLines(Path.Combine(_dir, "usage.jsonl"));
        lines.Should().ContainSingle();
        lines[0].Should().Contain("someone@example.com").And.Contain("codex");
    }

    [Fact]
    public async Task AConversationIsRecordedAsOneRatherThanAsAReview()
    {
        // The owner's "счиатть, отделять": a chat turn goes into the spending record like a review
        // turn and is DISTINGUISHED from it, because "what did the gate cost me" and "what did asking
        // cost me" are two questions and one total answers neither.
        var (jobs, runner, _) = Build(new ReviewAttempt.Answered("the answer", 7, 11));
        jobs.Submit(Job() with { Kind = JobKind.Chat, Role = "" });

        await runner.PumpAsync("codex", CancellationToken.None);

        var read = new UsageReader(_dir).Read(
            new UsageRange(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1)));

        read.Lines.Should().ContainSingle();
        read.Lines[0].Kind.Should().Be(JobKind.Chat);
        UsageTotals.ByKind(read.Lines).Should().ContainSingle()
            .Which.Kind.Should().Be("chat");
    }

    [Fact]
    public async Task AReviewIsStillRecordedAsAReview()
    {
        var (jobs, runner, _) = Build(new ReviewAttempt.Answered("the answer", 7, 11));
        jobs.Submit(Job());

        await runner.PumpAsync("codex", CancellationToken.None);

        var read = new UsageReader(_dir).Read(
            new UsageRange(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1)));

        read.Lines[0].Kind.Should().Be(JobKind.Review);
    }

    private sealed class Exploding : IReviewLauncher
    {
        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job,
            IReadOnlyDictionary<string, string?> environment, CancellationToken ct) =>
            throw new InvalidOperationException("the machinery broke");
    }
}
