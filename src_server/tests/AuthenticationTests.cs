using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The whole authorisation model is "the email comes from a verified token". Every test here
/// presents something that must NOT be accepted.
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class AuthenticationTests
{
    [Fact]
    public async Task NoToken_Is401()
    {
        using var server = new TeamServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ACallerOutsideTheCompany_Is403()
    {
        using var server = new TeamServer();
        using var client = server.ClientFor("eve@evil.example");

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the domain is the boundary, and a valid token from outside it is still outside it");
    }

    [Fact]
    public async Task AnUnsignedAlgNoneToken_IsRefused()
    {
        using var server = new TeamServer();
        using var client = server.WithToken(Tokens.ForgedNoneAlg($"attacker@{TeamServer.Domain}"));

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenSignedWithAnotherKey_IsRefused()
    {
        using var server = new TeamServer();
        using var client = server.WithToken(
            Tokens.For($"attacker@{TeamServer.Domain}", "a-completely-different-key-32bytes!!"));

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ATokenWithNoEmail_IsRefused()
    {
        using var server = new TeamServer();
        using var client = server.WithToken(
            Tokens.WithClaims(TeamServer.LocalSigningKey, [new Claim("sub", "no-email-here")]));

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnExpiredToken_IsRefused()
    {
        using var server = new TeamServer();
        using var client = server.WithToken(Tokens.WithClaims(
            TeamServer.LocalSigningKey,
            [new Claim("email", $"late@{TeamServer.Domain}")],
            expires: DateTime.UtcNow.AddMinutes(-10)));

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Google sets <c>email_verified: false</c> in some tenants, and an unverified address is
    /// somebody else's until it is verified.
    /// </summary>
    [Fact]
    public async Task AnUnverifiedEmail_IsRefused()
    {
        using var server = new TeamServer();
        using var client = server.WithToken(Tokens.WithClaims(
            TeamServer.LocalSigningKey,
            [new Claim("email", $"unverified@{TeamServer.Domain}"), new Claim("email_verified", "false")]));

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WhoAmI_EchoesTheToken_AndKnowsAnAdmin()
    {
        using var server = new TeamServer();
        using var ordinary = server.ClientFor($"dev@{TeamServer.Domain}", "A Developer");
        using var boss = server.ClientFor($"boss@{TeamServer.Domain}");

        var them = await ordinary.GetFromJsonAsync<WhoAmIDto>(
            "/api/whoami", TestContext.Current.CancellationToken);
        var admin = await boss.GetFromJsonAsync<WhoAmIDto>(
            "/api/whoami", TestContext.Current.CancellationToken);

        them!.Email.Should().Be($"dev@{TeamServer.Domain}");
        them.Name.Should().Be("A Developer");
        them.IsAdmin.Should().BeFalse();
        admin!.IsAdmin.Should().BeTrue("Coai:Admins names this address");
    }

    /// <summary>
    /// The version is judged BEFORE the token, so an old client is told to update rather than
    /// handed a 401 about a token that was never the problem.
    /// </summary>
    [Fact]
    public async Task AClientTooOld_Is426_BeforeAnyTokenIsLookedAt()
    {
        using var server = new TeamServer(new Dictionary<string, string?>
        {
            ["Coai__MinimumClientContract"] = "2",
        });
        using var client = server.CreateClient(); // deliberately NO token
        client.DefaultRequestHeaders.Add(ContractVersion.Header, "1");

        var response = await client.GetAsync("/api/whoami", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UpgradeRequired,
            "401 here would send a person hunting a credential that was never the problem");
    }

    [Fact]
    public async Task EveryResponseCarriesTheServersOwnContractVersion()
    {
        using var server = new TeamServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/health", TestContext.Current.CancellationToken);

        response.Headers.GetValues(ContractVersion.Header).Should()
            .ContainSingle().Which.Should().Be(ContractVersion.Current.ToString(),
                "a client learns the server's version from a call it was already making");
    }

    [Fact]
    public async Task HealthAndClientConfig_NeedNoToken()
    {
        using var server = new TeamServer();
        using var client = server.CreateClient();

        (await client.GetAsync("/api/health", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/client-config", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK,
                "the caller has no token yet — that is what it is for");
    }

    [Fact]
    public async Task AnythingThatIsNotTheApi_DoesNotExist()
    {
        using var server = new TeamServer();
        using var client = server.ClientFor($"dev@{TeamServer.Domain}");

        (await client.GetAsync("/", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
