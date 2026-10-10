using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The two admin routes — the roster and the company spending — answer the SAME caller the same way,
/// whether that caller presents an identity-provider token or a session minted from one.
/// </summary>
/// <remarks>
/// Both routes sit behind the one <see cref="CallerFilter"/> and the one inline admin check, so this
/// is a pin rather than a repair: the risk consultation on story 2.1 (2026-10-10) asked for the
/// agreement to be observed, because a disagreement here is an admin who can see who spent what but
/// not who is signed in, or the reverse — and nothing else exercises the two together.
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class PeopleAndUsageAgreeTests
{
    private const string PeopleRoute = "/api/people";

    private const string CompanyRoute = "/api/usage?scope=company";

    private static string Boss => $"boss@{TeamServer.Domain}";

    private static string Dev => $"dev@{TeamServer.Domain}";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> SessionTokenFor(TeamServer server, string email)
    {
        using var client = server.ClientFor(email, "Somebody");
        var response = await client.PostAsync("/api/session", content: null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return (await response.Content.ReadFromJsonAsync<SessionDto>(Ct))!.Token;
    }

    private static async Task<(HttpStatusCode People, HttpStatusCode Company)> BothAsync(HttpClient client) =>
        ((await client.GetAsync(PeopleRoute, Ct)).StatusCode, (await client.GetAsync(CompanyRoute, Ct)).StatusCode);

    [Fact]
    public async Task AMixedCaseConfiguredAdmin_IsAnAdminOnBothRoutes_ByRawTokenAndBySession()
    {
        // Configured as `Boss@Example.COM`, calling as `boss@example.com`: the admin list is matched
        // case-insensitively, and the session carries the email the token had.
        using var server = new TeamServer(new Dictionary<string, string?> { ["Coai__Admins"] = $"Boss@{TeamServer.Domain.ToUpperInvariant()}" });
        using var raw = server.ClientFor(Boss);
        using var session = server.WithToken(await SessionTokenFor(server, Boss));

        (await BothAsync(raw)).Should().Be((HttpStatusCode.OK, HttpStatusCode.OK));
        (await BothAsync(session)).Should().Be((HttpStatusCode.OK, HttpStatusCode.OK));
    }

    [Fact]
    public async Task ANonAdmin_IsRefusedOnBothRoutes_ByRawTokenAndBySession()
    {
        using var server = new TeamServer();
        using var raw = server.ClientFor(Dev);
        using var session = server.WithToken(await SessionTokenFor(server, Dev));

        (await BothAsync(raw)).Should().Be((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden));
        (await BothAsync(session)).Should().Be((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden));
    }

    [Fact]
    public async Task AnAnonymousCaller_IsUnauthorizedOnBothRoutes()
    {
        using var server = new TeamServer();
        using var anonymous = server.CreateClient();

        (await BothAsync(anonymous)).Should().Be((HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized));
    }

    /// <summary>
    /// Admin status is read from the configuration on every request, never stored in the session: a
    /// session minted while its owner was an admin stops opening the admin routes the moment a
    /// restart's configuration no longer names them.
    /// </summary>
    [Fact]
    public async Task AnAdminRemovedByARestart_IsRefusedOnBothRoutes_WithTheRetainedSession()
    {
        var data = Path.Combine(Path.GetTempPath(), "coai-server-tests", Guid.NewGuid().ToString("N"));
        string retained;
        using (var before = new TeamServer(new Dictionary<string, string?> { ["Coai__DataDir"] = data }))
        {
            retained = await SessionTokenFor(before, Boss);
            (await BothAsync(before.WithToken(retained))).Should().Be((HttpStatusCode.OK, HttpStatusCode.OK), "an admin, before");
        }

        using var after = new TeamServer(new Dictionary<string, string?>
        {
            ["Coai__DataDir"] = data,
            ["Coai__Admins"] = $"someone-else@{TeamServer.Domain}",
        });
        using var session = after.WithToken(retained);
        using var raw = after.ClientFor(Boss);

        (await BothAsync(session)).Should().Be((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), "the session is still valid, the admin is not");
        (await BothAsync(raw)).Should().Be((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden));
    }
}
