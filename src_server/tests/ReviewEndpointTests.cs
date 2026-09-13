using System.Net;
using System.Net.Http.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Reviewers;
using CoaiServer;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>
    /// A role spelled in another case is the same role, and it is RECORDED in the catalog's spelling.
    /// </summary>
    /// <remarks>
    /// <c>Enum.TryParse(role, ignoreCase: true)</c> did both jobs — accepting the spelling and
    /// canonicalising it — until the enum was retired. Losing either half is invisible until it is
    /// expensive: a refusal would stop every client that lower-cases its roles, and a passed-through
    /// spelling would make one role two rows in the usage view the day two clients disagree.
    /// (codex, on the plan round of the story that removed the enum.)
    /// </remarks>
    [Theory]
    [InlineData("architecture", "Architecture")]
    [InlineData("cOnVeNtIoNs", "Conventions")]
    [InlineData("SECURITYRELIABILITY", "SecurityReliability")]
    public async Task AShippedRoleInAnyCase_IsAccepted_AndRecordedInTheCatalogsSpelling(
        string sent, string recorded)
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = sent });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // The recorded spelling is asserted at the function rather than over the wire: the role a
        // job carries reaches no response a client can read back, only the ledger a finished run
        // writes — and running one here would mean running a vendor. It moved from
        // `ReviewEndpoints.CanonicalRole` to `AcceptedRoles` when which roles a server runs stopped
        // being a constant.
        AcceptedRoles.From([], allowAny: false).Canonical(sent).Should().Be(recorded);
    }

    [Fact]
    public void ARoleTheCatalogDoesNotKnow_KeepsTheSpellingItArrivedWith() =>
        // So the refusal can quote it back. Canonicalising is not the same as validating, and the
        // check that refuses an unknown role runs before this ever sees one.
        AcceptedRoles.From([], allowAny: false).Canonical("Requirements").Should().Be("Requirements");

    [Fact]
    public async Task ARoleThisServerDoesNotKnow_IsRefusedNamingTheOnesItDoes()
    {
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = "Requirements" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("Requirements").And.Contain("Architecture",
                "the refusal names the legal values, and they come from the shared catalog now");
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

    // ---------- story 2: the gates read what this server was CONFIGURED to accept ----------

    /// <summary>A server told to run one role of its own, beside the five it ships with.</summary>
    private static TeamServer WithExtraRole(string extra = "Requirements")
    {
        var server = new TeamServer(new Dictionary<string, string?> { ["Coai:ExtraRoles"] = extra });
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        return server;
    }

    [Fact]
    public async Task ARoleTheOperatorConfigured_IsAccepted()
    {
        // The whole point of plan 3. Until this, a team that shared a Team server was exactly the
        // team that could not share a review role somebody wrote.
        using var server = WithExtraRole();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = "Requirements" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task AConfiguredRoleIsAcceptedInAnyCase()
    {
        using var server = WithExtraRole();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = "requirements" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a client that lower-cases its roles is a client, not a mistake");
    }

    [Fact]
    public async Task AServerWithAnExtraRoleStillRefusesOneNobodyConfigured()
    {
        using var server = WithExtraRole();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = "Invented" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("Invented").And.Contain("Requirements",
                "the accepted set is what this server runs, not what it shipped with");
    }

    [Fact]
    public async Task AnIdThatIsNotAnIdIsRefusedByNamingTheRuleRatherThanTheRoles()
    {
        using var server = new TeamServer(new Dictionary<string, string?> { ["Coai:AllowAnyRole"] = "true" });
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = "My-Role" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("COAI_ROUNDS_", "the rule it broke")
            .And.NotContain("Architecture", "which is not what was wrong with it");
    }

    [Fact]
    public async Task AllowAnyRoleAcceptsARoleNobodyNamed()
    {
        using var server = new TeamServer(new Dictionary<string, string?> { ["Coai:AllowAnyRole"] = "true" });
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Role = "Invented" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task TheSameReviewInTwoCasingsIsOneJob()
    {
        // Canonicalisation happens ONCE, at the request boundary, BEFORE the idempotency
        // fingerprint — so two clients disagreeing about the case of a role do not become two jobs
        // that a later ledger view has to merge. (codex, the plan round, the sharpest finding.)
        // Two sends under ONE key. The fingerprint is what decides whether they are the same work:
        // canonicalised, they agree and the second is the first again; uncanonicalised, they differ
        // and the server answers 409 because one key is describing two different reviews.
        using var server = WithExtraRole();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var sent = Request() with { IdempotencyKey = "one-key" };

        var first = await client.PostAsJsonAsync("/api/reviews", sent with { Role = "Requirements" });
        var second = await client.PostAsJsonAsync("/api/reviews", sent with { Role = "requirements" });

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted, "the same review, spelled twice");

        var one = await first.Content.ReadFromJsonAsync<ReviewAcceptedDto>();
        var two = await second.Content.ReadFromJsonAsync<ReviewAcceptedDto>();
        two!.Id.Should().Be(one!.Id, "the fingerprint is computed from the canonical role");

        // And the STORED role is canonical too. Asserting only the id would pass a server that
        // canonicalised for the fingerprint and recorded the raw spelling — which is the ledger
        // split this whole decision exists to prevent, surviving the test written to prove it gone.
        // (codex, story 2's plan round.)
        var stored = server.Services.GetRequiredService<JobStore>()
            .Polled(one.Id, $"dev@{TeamServer.Domain}", DateTimeOffset.UtcNow);
        stored!.Role.Should().Be("Requirements", "the operator's spelling, whichever case arrived");
    }

    [Fact]
    public async Task AChatCarryingAnUnknownRoleIsToldItIsAChat_NotOfferedReviewRoles()
    {
        // The two refusals used to contradict each other one request apart: a chat carrying
        // 'Invented' was told "'Invented' is not a review role. Accepted: …", so the client picked a
        // name it had just been handed, sent it, and was told "a chat carries no review role".
        // Whether a job may carry a role AT ALL is a question about the kind, and it is answered
        // before which roles exist. (gemini, story 2's code round.)
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Kind = "chat", Role = "Invented" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error
            .Should().Contain("chat carries no review role")
            .And.NotContain("Architecture", "a list of roles is not the answer to a question about kinds");
    }

    [Fact]
    public async Task ANewClientSendingAnUnknownRoleIsStillRefused()
    {
        // The role check moved INTO JobKinds for a client that names its kind. It has to stay a
        // check: the (review, a role) row used to fall through to null, which is a signature that
        // says it validates and a method that does not.
        using var server = WithVendors();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Kind = "review", Role = "Invented" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ErrorDto>())!.Error.Should().Contain("Invented");
    }

    [Fact]
    public async Task ANewClientSendingAConfiguredRoleIsAccepted()
    {
        using var server = WithExtraRole();

        var response = await server.ClientFor($"dev@{TeamServer.Domain}")
            .PostAsJsonAsync("/api/reviews", Request() with { Kind = "review", Role = "Requirements" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task SayingNothingAndSayingWhitespaceAreOneJob()
    {
        // `Canonical("   ")` returned three spaces, so the same review from the same person was two
        // jobs depending on which way they said nothing — one fingerprint over "" and one over "   ".
        // (gemini, story 2's code round.)
        using var server = WithVendors();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var sent = Request() with { IdempotencyKey = "one-key", Kind = "chat" };

        var first = await client.PostAsJsonAsync("/api/reviews", sent with { Role = null });
        var second = await client.PostAsJsonAsync("/api/reviews", sent with { Role = "   " });

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await second.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!.Id
            .Should().Be((await first.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!.Id);
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
