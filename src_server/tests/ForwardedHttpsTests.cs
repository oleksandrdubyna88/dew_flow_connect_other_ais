using System.Net;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The HTTPS gate, run with the setting a real deployment uses.
/// </summary>
/// <remarks>
/// <para><b>Why this file exists.</b> Every other test in this suite sets
/// <c>Coai__RequireForwardedHttps=false</c>, so 165 of them ran green over a middleware that refuses
/// every request on a correctly configured host. It was found by deploying: the server answered
/// <c>/api/health</c> and then <c>403 HTTPS required.</c> to everything else, behind an nginx that
/// was setting the header correctly.</para>
/// <para><b>The mechanism.</b> `UseForwardedHeaders` CONSUMES <c>X-Forwarded-Proto</c> when the
/// request comes from a trusted proxy — that is its job: it moves the value into
/// <c>Request.Scheme</c> and strips the header so nothing downstream can be fooled twice. The gate
/// then read the RAW header, found nothing, and refused. So the failure was inverted: configuring
/// <c>Coai:TrustedProxies</c> CORRECTLY is what broke it, and leaving the proxy untrusted is what
/// made it appear to work.</para>
/// </remarks>
// The env this harness sets is PROCESS-GLOBAL, so a class outside the collection runs in
// parallel and leaks `RequireForwardedHttps=true` into everybody else's server. Two session
// tests went 403 before this line was here.
[Collection(ServerCollection.Name)]
public sealed class ForwardedHttpsTests
{
    private static TeamServer Strict() => new(new Dictionary<string, string?>
    {
        // The production value, which no other test in this suite uses.
        ["Coai__RequireForwardedHttps"] = "true",
        // And a TRUSTED loopback proxy, which is what a host nginx is. This pair is the deployment.
        ["Coai__TrustedProxies"] = "127.0.0.1/32",
    });

    [Fact]
    public async Task ARequestForwardedByATrustedProxyOverHttpsIsServed()
    {
        // The one that was red: the proxy is trusted, so the header is consumed before the gate
        // reads it, and every route but health answered 403 on a correctly configured machine.
        using var server = Strict();
        // FROM the trusted proxy, not from nowhere: with a null remote address the middleware has
        // nothing to check and applies the header regardless, so the test would pass without ever
        // exercising the decision it is named after.
        using var client = server.ClientFrom(System.Net.IPAddress.Loopback);
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync("/api/client-config");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "a request the proxy forwarded over https must be served, and trusting that proxy is "
            + "what a deployment does");
    }

    [Fact]
    public async Task APlaintextRequestIsStillRefused()
    {
        // The gate must keep doing its job: no header at all means it did not come through the
        // proxy, and a missing header is treated exactly like a plaintext one.
        using var server = Strict();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/client-config");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AForwardedHttpRequestIsRefused()
    {
        using var server = Strict();
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");

        var response = await client.GetAsync("/api/client-config");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HealthIsExemptBecauseItsProbeHasNoProxyInFrontOfIt()
    {
        using var server = Strict();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnUNTRUSTEDProxysHeaderIsREFUSED()
    {
        // The first repair of the gate kept the raw header as a fallback, and this test asserted
        // that fallback. Two reviewers called it in the same round and they were right: a caller
        // who can reach this server over plaintext can send `X-Forwarded-Proto: https` itself, so
        // honouring it makes the gate something any caller can satisfy by asserting it.
        //
        // The header is now believed only when `UseForwardedHeaders` believed it — which is only
        // for a proxy `Coai:TrustedProxies` names. This proxy is not named, so its claim is worth
        // nothing.
        using var server = new TeamServer(new Dictionary<string, string?>
        {
            ["Coai__RequireForwardedHttps"] = "true",
            ["Coai__TrustedProxies"] = "10.255.255.0/32",
        });
        // A REAL remote address, because that is the whole question. `TestServer` leaves
        // `RemoteIpAddress` null by default and `UseForwardedHeaders` then has nothing to check the
        // proxy against, so it applies the header — which would make this test pass for the wrong
        // reason, or fail for one. Giving the connection an address that `TrustedProxies` does not
        // name is what actually exercises the decision.
        using var client = server.ClientFrom(System.Net.IPAddress.Parse("203.0.113.7"));
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.GetAsync("/api/client-config");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a header the middleware did not trust is a claim the caller made about itself");
    }
}
