using System.Net;
using System.Net.Http.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Core.Rounds;
using CoaiServer;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

[Collection(ServerCollection.Name)]
public sealed class CatalogEndpointTests
{
    private const string Vendors = """
        [{ "id": "codex", "runtime": "codex", "models": ["gpt-5.6-luna"], "slots": ["a", "b"] }]
        """;

    [Fact]
    public async Task TheCatalogRefusesAnyoneWhoIsNotSignedIn()
    {
        using var server = new TeamServer();

        var anonymous = await server.CreateClient().GetAsync("/api/catalog");

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheCatalogRefusesAnotherCompany()
    {
        using var server = new TeamServer();

        var outsider = await server.ClientFor("someone@other-company.test").GetAsync("/api/catalog");

        outsider.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the whole point is that only this company spends its own subscriptions");
    }

    [Fact]
    public async Task TheCatalogNamesTheModelsTheOperatorAllowsAndCountsTheAccounts()
    {
        using var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        var catalog = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<CatalogDto>("/api/catalog");

        var vendor = catalog!.Vendors.Should().ContainSingle().Subject;
        vendor.Id.Should().Be("codex");
        vendor.Models.Should().ContainSingle().Which.Should().Be("gpt-5.6-luna");
        vendor.Slots.Total.Should().Be(2);
        // Nothing has ever been signed in on a throwaway data directory, so both accounts say so
        // rather than claiming to be ready — that is what tells the operator to run `login`.
        vendor.Slots.NeedsSignIn.Should().Be(2);
        vendor.Slots.Ready.Should().Be(0);
    }

    [Fact]
    public async Task HealthNeverImpliesThatAnAccountCanBeUsed()
    {
        using var server = new TeamServer();
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        var catalog = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<CatalogDto>("/api/catalog");

        // The plan round's most-repeated finding, asserted as a SEPARATION rather than as a value:
        // the probe runs the CLI with the server's own environment, not any slot's HOME, so it can
        // only answer "is the binary there". Whether an account is usable is the slot counts. This
        // holds whichever way CliFound lands, which is what makes it a test of the design rather
        // than of whether the developer happens to have codex installed.
        var vendor = catalog!.Vendors.Single();
        vendor.Slots.Ready.Should().Be(0, "no account has ever been signed in on a throwaway data directory");
        vendor.Slots.NeedsSignIn.Should().Be(vendor.Slots.Total);
    }
    [Fact]
    public async Task AnEmptyCatalogSaysWhichFileToCreate()
    {
        using var server = new TeamServer();

        var catalog = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<CatalogDto>("/api/catalog");

        catalog!.Vendors.Should().BeEmpty();
        catalog.Error.Should().Contain("vendors.json",
            "a fresh install is not broken, but the operator still needs to be told what is missing");
    }

    [Fact]
    public async Task ABadEditIsVisibleToTheOperatorRatherThanOnlyInTheLog()
    {
        using var server = new TeamServer();
        var path = Path.Combine(server.DataDir, "vendors.json");
        File.WriteAllText(path, Vendors);
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        (await client.GetFromJsonAsync<CatalogDto>("/api/catalog"))!.Vendors.Should().ContainSingle();

        File.WriteAllText(path, """[{ "id": "codex", "runtime": "typo", "models": ["m"], "slots": ["a"] }]""");

        var after = await client.GetFromJsonAsync<CatalogDto>("/api/catalog");
        after!.Vendors.Should().ContainSingle("the previous allowlist keeps serving");
        after.Error.Should().Contain("typo", "and the disagreement between disk and behaviour is on screen");
    }

    [Fact]
    public async Task TheAdminFlagFollowsTheConfiguredList()
    {
        using var server = new TeamServer();
        var client = server.ClientFor($"dev@{TeamServer.Domain}");
        var boss = server.ClientFor($"boss@{TeamServer.Domain}");

        (await client.GetFromJsonAsync<CatalogDto>("/api/catalog"))!.IsAdmin.Should().BeFalse();
        (await boss.GetFromJsonAsync<CatalogDto>("/api/catalog"))!.IsAdmin.Should().BeTrue();
    }

    // ---------- story 3: the catalog says which roles this server will run ----------

    private static TeamServer WithRoles(IDictionary<string, string?>? config = null)
    {
        var server = new TeamServer(config);
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        return server;
    }

    [Fact]
    public async Task TheCatalogNamesTheFiveThisProductShipsWhenNobodyConfiguredAnything()
    {
        using var server = WithRoles();

        var catalog = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<CatalogDto>("/api/catalog");

        catalog!.Roles.Should().BeEquivalentTo(RoleCatalog.Builtin.Roles.Select(r => r.Id));
        catalog.AllowAnyRole.Should().BeFalse();
    }

    [Fact]
    public async Task TheCatalogNamesARoleTheOperatorConfigured()
    {
        using var server = WithRoles(new Dictionary<string, string?> { ["Coai:ExtraRoles"] = "Requirements" });

        var catalog = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<CatalogDto>("/api/catalog");

        catalog!.Roles.Should().Contain("Requirements")
            .And.Contain(RoleCatalog.Builtin.Roles[0].Id, "the shipped ones never go away");
        catalog.AllowAnyRole.Should().BeFalse();
    }

    [Fact]
    public async Task TheCatalogSaysWhenThisServerWillRunAnything()
    {
        using var server = WithRoles(new Dictionary<string, string?> { ["Coai:AllowAnyRole"] = "true" });

        var catalog = await server.ClientFor($"dev@{TeamServer.Domain}")
            .GetFromJsonAsync<CatalogDto>("/api/catalog");

        catalog!.AllowAnyRole.Should().BeTrue();
        catalog.Roles.Should().NotBeEmpty(
            "a server that runs anything still runs these, and a client with no custom role needs the list");
    }

    [Fact]
    public async Task WhatTheCatalogSAYSIsWhatTheGateACCEPTS()
    {
        // The failure this test exists for: a DTO property filled from somewhere other than the
        // instance the gates refuse with — or from nowhere, which is likelier — leaves a client
        // excluding a role this server runs perfectly well, with every other test still green.
        // So the two are compared against each other rather than against a literal.
        // (codex, plan 3's plan round.)
        using var server = WithRoles(new Dictionary<string, string?> { ["Coai:ExtraRoles"] = "Requirements,Brief" });
        var client = server.ClientFor($"dev@{TeamServer.Domain}");

        var catalog = await client.GetFromJsonAsync<CatalogDto>("/api/catalog");

        foreach (var role in catalog!.Roles)
        {
            var submitted = await client.PostAsJsonAsync(
                "/api/reviews",
                new ReviewRequestDto("codex", "gpt-5.6-luna", "review this", role, 60));

            submitted.StatusCode.Should().Be(HttpStatusCode.Accepted, role);
        }
    }
}

/// <summary>The probe cache, tested directly — an HTTP test cannot count process launches.</summary>
public sealed class VendorHealthCacheTests
{
    [Fact]
    public async Task TheCliIsAskedOnceNotOncePerRequest()
    {
        var launcher = new CountingLauncher();
        var cache = new VendorHealthCache(launcher, TimeSpan.FromMinutes(5));

        for (var i = 0; i < 5; i++)
        {
            await cache.OfAsync("codex");
        }

        // The panel polls this catalog while it is open. Uncached, five polls of three vendors is
        // fifteen process launches on a 3.8 GB box whose memory is the binding constraint of the
        // whole deployment.
        cache.Probes.Should().Be(1);
        launcher.Runs.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task TheAnswerIsAskedAgainOnceItIsStale()
    {
        var cache = new VendorHealthCache(new CountingLauncher(), TimeSpan.Zero);

        await cache.OfAsync("codex");
        await cache.OfAsync("codex");

        cache.Probes.Should().Be(2, "an operator who just fixed an install must not be told to restart the server");
    }

    private sealed class CountingLauncher : IProcessLauncher
    {
        public int Runs { get; private set; }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Runs += 1;

            return Task.FromResult(new ProcessResult(0, "codex 1.2.3", string.Empty, false));
        }
    }
}
