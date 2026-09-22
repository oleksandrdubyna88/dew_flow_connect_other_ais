using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>Every test here sets process-global environment variables, so they run one at a time.</summary>
[CollectionDefinition("the-server", DisableParallelization = true)]
public sealed class TheServerCollection;

/// <summary>
/// The route, over real HTTP, against the real serializer.
/// </summary>
/// <remarks>
/// <b>What the unit tests cannot see.</b> `Ingest.Take` is pure and covered; the bearer header, the
/// 401, the batch cap and the AOT JSON binding are not part of it. The last one is why this file
/// exists at all: an AOT binding failure once made a released `coai-server` answer 500 to everything,
/// and `BugsJson` is new code of exactly that kind — a serializer context that compiles perfectly and
/// can still bind nothing at runtime.
/// </remarks>
[Collection("the-server")]
public sealed class TheRouteTests
{
    private static readonly object Good = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
            },
        },
    };

    /// <summary>The whole point of an HTTP test: the body really binds.</summary>
    [Fact]
    public async Task ARealRequestBindsAndIsAccepted()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var reply = await http.PostAsJsonAsync("/ingest", Good, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(
            HttpStatusCode.OK,
            "a serializer context that compiles and binds nothing is exactly what this test is for");

        var answer = await reply.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken);
        answer!.Items.Should().ContainSingle().Which.Took.Should().Be("accepted");

        using var corpus = server.Reading();
        corpus.Waiting(10).Should().ContainSingle("the pair must really be in quarantine");
    }

    /// <summary>No key is no answer, and the answer says nothing about which keys exist.</summary>
    [Fact]
    public async Task WithoutAKeyNothingIsTaken()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();

        var reply = await http.PostAsJsonAsync("/ingest", Good, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var corpus = server.Reading();
        corpus.Waiting(10).Should().BeEmpty();
    }

    /// <summary>A revoked key and one that never existed are indistinguishable.</summary>
    /// <remarks>
    /// Different answers would make the endpoint a way to discover which keys are real — ask with a
    /// guess and read the difference.
    /// </remarks>
    [Fact]
    public async Task ARevokedKeyAndAnUnknownKeyAnswerTheSame()
    {
        using var server = new BugsServer();
        var (key, id) = server.IssueKey();
        using (var corpus = server.Reading())
        {
            corpus.Revoke(id, Audit.By(AdminId.Cli, server.Clock)).Should().BeOfType<Revoked.Now>();
        }

        using var http = server.CreateClient();

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var revoked = await http.PostAsJsonAsync("/ingest", Good, TestContext.Current.CancellationToken);

        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "never-issued");
        var unknown = await http.PostAsJsonAsync("/ingest", Good, TestContext.Current.CancellationToken);

        revoked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        unknown.StatusCode.Should().Be(revoked.StatusCode, "the two must not be told apart");
    }

    /// <summary>A document with no `items` is a malformed request, not an empty batch.</summary>
    [Fact]
    public async Task ADocumentWithNoItemsIsRefused()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var reply = await http.PostAsJsonAsync("/ingest", new { }, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("items");
    }

    /// <summary>A batch past the cap is refused before anything is read.</summary>
    /// <remarks>
    /// With the body limit this is what actually bounds abuse: `submissions` is a lifetime COUNT and
    /// is not a rate limit, which the plan says after a reviewer pointed out that a lifetime count has
    /// no window and no reset. The rate limiter is what bounds a minute; `last_seen_month` is a month
    /// and bounds nothing.
    /// </remarks>
    [Fact]
    public async Task ABatchPastTheCapIsRefused()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var many = new
        {
            items = Enumerable.Range(0, Ingest.MostPerBatch + 1).Select(n => new
            {
                language = "CSharp",
                skeletonBefore = $"method_{n}() {{ }}",
                skeletonAfter = $"method_{n}() {{ var_1 = 0; }}",
            }).ToArray(),
        };

        var reply = await http.PostAsJsonAsync("/ingest", many, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var corpus = server.Reading();
        corpus.Waiting(500).Should().BeEmpty("nothing is stored from a batch that was never read");
    }

    /// <summary>Health says the server is up and nothing about the corpus.</summary>
    /// <remarks>
    /// Unauthenticated, so a count here would be an unauthenticated read of how much anybody has
    /// contributed.
    /// </remarks>
    [Fact]
    public async Task HealthSaysNothingAboutTheCorpus()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        using (var corpus = server.Reading())
        {
            corpus.Keep("CSharp", "a", "b", new KeyId("key"), UtcMonth.Of(DateTimeOffset.UnixEpoch));
        }

        var reply = await http.GetAsync("/health", TestContext.Current.CancellationToken);
        var body = await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("ok");
        body.Should().NotContain("1", "a count here is an unauthenticated read of how much exists");
        body.Should().NotContain(Corpus.IdOf("CSharp", "a", "b"), "nor is an entry id");
    }

    /// <summary>A leak is refused over HTTP too, with the word that caused it.</summary>
    [Fact]
    public async Task ALeakIsRefusedWithItsWord()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var leaky = new
        {
            items = new[]
            {
                new
                {
                    language = "CSharp",
                    skeletonBefore = "method_1() { ChargeAcmeCustomer(); }",
                    skeletonAfter = "method_1() { lock (var_1) { } }",
                },
            },
        };

        var reply = await http.PostAsJsonAsync("/ingest", leaky, TestContext.Current.CancellationToken);
        var answer = await reply.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK, "a refusal is an item's fate, not the batch's");
        answer!.Items[0].Took.Should().Be("refused");
        answer.Items[0].Why.Should().Contain("ChargeAcmeCustomer");
        using var corpus = server.Reading();
        corpus.Waiting(10).Should().BeEmpty();
    }

    /// <summary>A body past the cap does not reach the route.</summary>
    [Fact]
    public async Task ABodyPastTheCapIsRefused()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        using var huge = new StringContent(
            new string('x', Ingest.MostBytes + 1024), Encoding.UTF8, "application/json");

        var reply = await http.PostAsync("/ingest", huge, TestContext.Current.CancellationToken);

        reply.IsSuccessStatusCode.Should().BeFalse("the body limit is what stops a batch nobody reads");
    }

    // ------------------------------------------------------------------------------------------
    // The commented route — a door an older binary does not have.
    // ------------------------------------------------------------------------------------------

    /// <summary>The comment binds through the real AOT context and lands in the column.</summary>
    /// <remarks>
    /// `Ingest.Take` is unit-tested with a comment, and that proves nothing about the WIRE: a
    /// property the serializer context does not know is dropped in silence, which is the exact
    /// failure this whole route exists to prevent. Only a real request can see it.
    /// </remarks>
    [Fact]
    public async Task ACommentedRequestBindsAndTheCommentIsStored()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var reply = await http.PostAsJsonAsync(
            "/ingest/commented", Said, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.OK);
        var answer = await reply.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken);
        answer!.Items.Should().ContainSingle().Which.Took.Should().Be("accepted");

        using var corpus = server.Reading();
        corpus.Waiting(10).Should().ContainSingle()
            .Which.Comment.Should().Be("this one bit us in production");
    }

    /// <summary>
    /// And the plain route REFUSES a comment it is handed. THE mechanism, in one assertion.
    /// </summary>
    /// <remarks>
    /// Two routes that behaved identically would be one route with two names, and the only thing
    /// keeping a comment out of a server too old to store it would be an accident of deployment
    /// order. Here the difference is the code's, and it is the same difference an old binary's 404
    /// enforces from the other side — with the refusal per ITEM, so the batch is still a 200 and a
    /// pair without a comment beside it still lands.
    /// </remarks>
    [Fact]
    public async Task ThePlainRouteRefusesAPairThatCarriesAComment()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var reply = await http.PostAsJsonAsync("/ingest", Said, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(
            HttpStatusCode.OK, "a refusal is an item's fate, not the batch's");
        var answer = await reply.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken);
        answer!.Items.Should().ContainSingle().Which.Took.Should().Be("refused");
        answer.Items[0].Why.Should().Contain("/ingest/commented");

        using var corpus = server.Reading();
        corpus.Waiting(10).Should().BeEmpty("nothing is written, so the batch can be resent");
    }

    /// <summary>The new route is behind the same key as the old one.</summary>
    /// <remarks>
    /// A route added beside the gate rather than behind it would let anybody write into somebody
    /// else's corpus — and `WhoOf` would throw on a request that carried no key at all, turning the
    /// hole into a 500 rather than a 401.
    /// </remarks>
    [Fact]
    public async Task TheCommentedRouteWithoutAKeyTakesNothing()
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();

        var reply = await http.PostAsJsonAsync(
            "/ingest/commented", Said, TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var corpus = server.Reading();
        corpus.Waiting(10).Should().BeEmpty();
    }

    /// <summary>Both routes state which contract answered, on every answer.</summary>
    /// <remarks>
    /// The belt behind the route: a proxy that answers 200 for a path the server does not really
    /// have cannot also produce this number, so a client that checks it refuses to mark a batch
    /// somebody else answered.
    /// </remarks>
    [Theory]
    [InlineData("/ingest")]
    [InlineData("/ingest/commented")]
    public async Task EveryAnswerSaysWhichContractAnsweredIt(string route)
    {
        using var server = new BugsServer();
        using var http = server.CreateClient();
        var (key, _) = server.IssueKey();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var reply = await http.PostAsJsonAsync(route, Good, TestContext.Current.CancellationToken);

        var answer = await reply.Content.ReadFromJsonAsync<Answer>(TestContext.Current.CancellationToken);
        answer!.Contract.Should().Be(Contract.Comments);
    }

    /// <summary>The same pair as <see cref="Good"/>, with words a person typed about it.</summary>
    private static readonly object Said = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
                comment = "this one bit us in production",
            },
        },
    };

    /// <summary>The answer's shape, as a reader of it sees it.</summary>
    private sealed record Answer(IReadOnlyList<Item> Items, int Contract = 0);

    private sealed record Item(string EntryId, string Took, string Why);
}
