using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The gate in front of <c>/admin/*</c>: what it refuses, what it never discloses, and whose limit
/// it counts against.
/// </summary>
/// <remarks>
/// <para><b>The disclosure rule is the point of this file.</b> A caller must not be able to tell "no
/// administrators are configured here" from "you are not one of them": the first answer would make
/// any of these routes an oracle for whether administration is enabled on this deployment,
/// answerable by anybody. So the two answers are compared BYTE FOR BYTE across two differently
/// configured servers, which is the only way to assert an indistinguishability rather than assume
/// it.</para>
/// <para><b>Gating is a PREFIX match, and that is tested as behaviour.</b> A path under `/admin` that
/// no route serves is refused before routing, so the next endpoint added to this surface is
/// protected by default rather than by somebody remembering to protect it.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheAdminGateTests
{
    private const string AdminKey = "an-administrators-key";

    /// <summary>With no credential, every admin route the server serves refuses.</summary>
    /// <remarks>
    /// The routes come from the host's own endpoint table rather than a list retyped here, with
    /// their real methods — see <see cref="BugsServer.AdminRoutes"/>. Written by hand it named the
    /// three GET routes and quietly left both POSTs out, the issuance among them.
    /// </remarks>
    [Fact]
    public async Task WithoutACredentialEveryAdminRouteRefuses()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.CreateClient();

        foreach (var (method, path) in server.AdminRoutes())
        {
            using var asking = new HttpRequestMessage(new HttpMethod(method), path);
            var reply = await http.SendAsync(asking, TestContext.Current.CancellationToken);

            reply.StatusCode.Should().Be(
                HttpStatusCode.Unauthorized, $"{method} {path} is not public, least of all the issuance");
        }

        using var corpus = server.Reading();
        corpus.KeysTotal().Should().Be(0, "and a refused issuance issues nothing");
        corpus.AuditCount().Should().Be(0, "a refused request does nothing and records nothing");
    }

    /// <summary>
    /// A server with no administrators and a server with the wrong credential answer the same bytes.
    /// </summary>
    /// <remarks>
    /// <para>Two DIFFERENTLY CONFIGURED servers, because that is what the rule is about: the same
    /// request must not reveal which deployment it reached. Status, body and any
    /// `WWW-Authenticate` hint are all compared, since any one of them differing is the oracle.</para>
    /// <para>They run one after the other rather than side by side. The harness configures the
    /// server through PROCESS environment variables, so two live instances would share one data
    /// directory and one serve lock — the second host refuses to start, and the failure reads
    /// "the entry point exited without ever building an IHost", which says nothing about admin
    /// keys. So the first server's answers are captured, it is disposed, and the second is asked
    /// the same questions.</para>
    /// </remarks>
    [Fact]
    public async Task AnAbsentVariableAndAWrongCredentialAreIndistinguishable()
    {
        var configured = await Refusals(adminKeys: AdminKey);
        var none = await Refusals(adminKeys: null);

        none.Should().HaveSameCount(
            configured, "both servers must have been asked the same questions");
        foreach (var (route, answer) in configured)
        {
            none[route].Should().Be(
                answer,
                $"{route} must answer the same thing whether administration is configured here or "
                + "not; a difference makes this endpoint an oracle anybody can ask");
        }

        configured.Values.Should().AllSatisfy(answer => answer.Should().StartWith("401 "));
    }

    /// <summary>What every admin route answers a wrong credential, as one comparable string each.</summary>
    /// <remarks>
    /// The server is started, asked, and disposed inside this method — see the caller's remarks for
    /// why two cannot be alive at once.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<string, string>> Refusals(string? adminKeys)
    {
        using var server = new BugsServer(adminKeys: adminKeys);
        using var http = server.Bearing("not-an-administrators-key");
        var answers = new Dictionary<string, string>();
        foreach (var (method, path) in server.AdminRoutes())
        {
            using var asking = new HttpRequestMessage(new HttpMethod(method), path);
            var reply = await http.SendAsync(asking, TestContext.Current.CancellationToken);
            var body = await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            answers[$"{method} {path}"] = $"{(int)reply.StatusCode} {reply.Headers.WwwAuthenticate} {body}";
        }

        return answers;
    }

    /// <summary>
    /// The right credential on a server that has administrators is the ONLY thing that gets through.
    /// </summary>
    /// <remarks>
    /// The other half of the test above: without it, a gate that refused everything would satisfy
    /// "the two are indistinguishable" perfectly.
    /// </remarks>
    [Fact]
    public async Task TheConfiguredCredentialIsAcceptedOnTheSameServer()
    {
        using var server = new BugsServer(adminKeys: $"# alice\n{AdminKey}");
        using var right = server.Bearing(AdminKey);
        using var wrong = server.Bearing("not-an-administrators-key");

        (await right.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await wrong.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>A path under `/admin` that no route serves is still refused without a credential.</summary>
    /// <remarks>
    /// This is what "gated by default" means, and it is observable: the refusal happens before
    /// routing, so an endpoint added to this surface tomorrow cannot be reached by a stranger even
    /// on the day somebody forgets to protect it. With a credential the same path is a 404, which
    /// proves the 401 came from the gate and not from the absence of a route.
    /// </remarks>
    [Fact]
    public async Task AnUnservedAdminPathIsRefusedBeforeRouting()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var stranger = server.CreateClient();
        using var administrator = server.Bearing(AdminKey);

        (await stranger.GetAsync("/admin/a-route-added-tomorrow", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the prefix is what gates, not the route list");
        (await administrator.GetAsync("/admin/a-route-added-tomorrow", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "so the 401 above was the gate's, not routing's");
    }

    /// <summary>Every refusal this server writes spells its one field the same way.</summary>
    /// <remarks>
    /// <para>There are two routes to a <see cref="Problem"/> body and they used to disagree. A
    /// handler's <c>TypedResults.BadRequest</c> goes through the host's JSON options, which apply a
    /// camel-case naming policy; a gate writes its body with the source-generated
    /// <c>JsonTypeInfo</c> directly, which applies the CONTEXT's options — and the context had
    /// none. So the same field arrived as `why` from a route and `Why` from a gate, and a client
    /// reading errors needed both spellings to read one server.</para>
    /// <para>The fix is on the context, so it cannot drift again: the naming policy is declared
    /// where the shapes are generated rather than at each call site.</para>
    /// </remarks>
    [Fact]
    public async Task EveryRefusalSpellsItsFieldTheSameWay()
    {
        using var server = new BugsServer(adminKeys: AdminKey, adminRatePerMinute: 1);
        using var stranger = server.CreateClient();
        using var administrator = server.Bearing(AdminKey);

        var fromTheGate = await Text(stranger.GetAsync("/admin/keys", TestContext.Current.CancellationToken));
        var fromARoute = await Text(
            administrator.GetAsync("/admin/keys?limit=0", TestContext.Current.CancellationToken));
        var fromTheLimit = await Text(
            administrator.GetAsync("/admin/keys", TestContext.Current.CancellationToken));

        foreach (var body in new[] { fromTheGate, fromARoute, fromTheLimit })
        {
            body.Should().StartWith("{\"why\":", $"one spelling for one field; this one reads {body}");
        }
    }

    private static async Task<string> Text(Task<HttpResponseMessage> sending)
    {
        using var reply = await sending;

        return await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A server with no administrators says so on the host, because that is the only place it can.
    /// </summary>
    /// <remarks>
    /// The disclosure rule above is what makes this line necessary rather than merely helpful: an
    /// absent variable and a wrong credential answer identically ON PURPOSE, so an operator has no
    /// way from outside to tell a missing secret from their own typo. The startup log is the one
    /// channel only they can read, and `AdminKeys` and `AdminGate` both promise this line exists —
    /// a promise in a docblock that nothing asserts is a promise that stops being true.
    /// </remarks>
    [Fact]
    public async Task AServerWithNoAdministratorsSaysSoOnStartup()
    {
        using var server = new BugsServer(adminKeys: null);
        using var http = server.CreateClient();
        (await http.GetAsync("/health", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK, "the host must have started for it to have said anything");

        server.Said.Should().Contain(
            line => line.Contains("no administrators are configured", StringComparison.Ordinal)
                && line.Contains(AdminKeys.Variable, StringComparison.Ordinal),
            "it must name the variable to look at, or the operator is left guessing");
    }

    /// <summary>And a server that HAS them reports how many, beside the limit they run under.</summary>
    [Fact]
    public async Task AServerWithAdministratorsReportsTheCountAndTheirLimit()
    {
        using var server = new BugsServer(adminKeys: $"{AdminKey}\nanother-key", adminRatePerMinute: 30);
        using var http = server.CreateClient();
        (await http.GetAsync("/health", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        server.Said.Should().Contain(
            line => line.Contains("2 administrators at 30 a minute", StringComparison.Ordinal),
            "an operator confirming a deploy wants to see what it read");
        server.Said.Should().NotContain(
            line => line.Contains("no administrators are configured", StringComparison.Ordinal));
        server.Said.Should().NotContain(
            line => line.Contains(AdminKey, StringComparison.Ordinal),
            "and a log line is not a place to print a credential");
    }

    /// <summary>
    /// The gate matches SEGMENTS, so a path that merely starts with those letters is not gated.
    /// </summary>
    /// <remarks>
    /// A reviewer read <c>StartsWithSegments("/admin")</c> as <c>String.StartsWith</c> and reported
    /// that `/administrator` would be caught by it. It would not — matching on segment boundaries is
    /// the whole difference between the two APIs and the reason this code uses that one — but the
    /// claim deserves a pin rather than an argument, because the two spellings are one character
    /// apart and the wrong one compiles. Gated by accident is not harmless: it would answer 401 to a
    /// public route somebody adds later under a name that happens to share the prefix.
    /// </remarks>
    [Fact]
    public async Task APathThatMerelyStartsWithThoseLettersIsNotGated()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var stranger = server.CreateClient();

        var reply = await stranger.GetAsync("/administrator", TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(
            HttpStatusCode.NotFound,
            "`/administrator` is one segment, not `/admin` plus something — a 401 here would mean "
            + "the gate is matching characters and would swallow a public route added under a name "
            + "that shares the prefix");
    }

    /// <summary>`/health` is public, and the admin gate does not touch it.</summary>
    [Fact]
    public async Task HealthIsNotBehindTheAdminGate()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.CreateClient();

        (await http.GetAsync("/health", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK, "a probe cannot hold an administrator's key");
    }

    /// <summary>
    /// The two limits are separate settings: exhausting the contributor's does not touch an
    /// administrator.
    /// </summary>
    /// <remarks>
    /// This is why the admin limit exists at all. Sharing the contributor number — flood control for
    /// a public endpoint — would rate-limit the Users tab after ten pages, which is the arithmetic
    /// the plan round did: a 250-row listing at 10 a minute is 25 minutes of paging.
    /// </remarks>
    [Fact]
    public async Task AContributorAtItsLimitDoesNotLimitAnAdministrator()
    {
        using var server = new BugsServer(ratePerMinute: 1, adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);
        var issued = await TheAdminRoutesTests.Issue(http, "a contributor");
        using var contributing = server.Bearing(issued.Key);

        var first = await contributing.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);
        var second = await contributing.PostAsJsonAsync("/ingest", OtherPair, TestContext.Current.CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "one a minute means one");

        for (var at = 0; at < 5; at++)
        {
            (await http.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.OK, "the administrator has its own window and its own limit");
        }
    }

    /// <summary>And the other way: an administrator at its limit does not stop contributors.</summary>
    [Fact]
    public async Task AnAdministratorAtItsLimitDoesNotLimitAContributor()
    {
        using var server = new BugsServer(adminKeys: AdminKey, adminRatePerMinute: 1);
        using var http = server.Bearing(AdminKey);
        var issued = await TheAdminRoutesTests.Issue(http, "a contributor");

        var refused = await http.GetAsync("/admin/keys", TestContext.Current.CancellationToken);

        refused.StatusCode.Should().Be(
            HttpStatusCode.TooManyRequests, "the issuance was the one request this administrator had");
        refused.Headers.RetryAfter.Should().NotBeNull("a 429 with no number is a client retrying for ever");
        (await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("1 requests a minute per administrator", "the number named is the ADMIN one");

        using var contributing = server.Bearing(issued.Key);
        (await contributing.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK, "a contributor is unaffected by the admin limit");
    }

    /// <summary>Two administrators have two windows, so one cannot lock the other out.</summary>
    [Fact]
    public async Task TwoAdministratorsDoNotShareAWindow()
    {
        using var server = new BugsServer(adminKeys: $"{AdminKey}\nanother-key", adminRatePerMinute: 1);
        using var alice = server.Bearing(AdminKey);
        using var bob = server.Bearing("another-key");

        (await alice.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await alice.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await bob.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK, "bob has spent nothing");
    }

    /// <summary>A refused credential never reaches the limiter, so it gets no window.</summary>
    /// <remarks>
    /// The order is the contract — 401, then 429 — and it is what stops a stranger's guesses from
    /// filling the limiter's dictionary with subjects that were never authenticated.
    /// </remarks>
    [Fact]
    public async Task AGuessedCredentialGetsNoWindow()
    {
        using var server = new BugsServer(adminKeys: AdminKey, adminRatePerMinute: 1);
        using var guessing = server.Bearing("a-guess");
        using var administrator = server.Bearing(AdminKey);

        for (var at = 0; at < 20; at++)
        {
            (await guessing.GetAsync("/admin/keys", TestContext.Current.CancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "still 401, never 429");
        }

        var active = await TheAdminRoutesTests.Get<TheAdminRoutesTests.ActiveNow>(
            administrator, "/admin/active");

        active.Items.Should().ContainSingle(
            "only the administrator that authenticated holds a window; twenty guesses hold none");
    }

    private static readonly object OnePair = new
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

    private static readonly object OtherPair = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_2(var_1) { }",
                skeletonAfter = "method_2(var_1) { lock (var_3) { } }",
            },
        },
    };
}
