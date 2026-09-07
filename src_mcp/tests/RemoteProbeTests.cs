using System.Net;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Answers a canned response and counts what was asked.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public int Requests { get; private set; }

    public List<HttpRequestMessage> Seen { get; } = [];

    public StubHandler(HttpStatusCode status, string body)
        : this(_ => new HttpResponseMessage(status) { Content = new StringContent(body) })
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests++;
        Seen.Add(request);

        return Task.FromResult(answer(request));
    }
}

/// <summary>
/// The health of a Team-server vendor, and the cache that keeps `providers` from hammering a server
/// that is already running other people's reviews.
/// </summary>
public sealed class RemoteProbeTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "coai-probe-" + Guid.NewGuid().ToString("N"));

    private const string Server = "https://coai.example.com";

    private static readonly VendorIdentity Vendor = new("codex", "remote", Server);

    private static string Catalog(int total, int ready, int cooling, int signedOut, string id = "codex") =>
        $$"""
        {"serverVersion":"0.5.1","isAdmin":false,"error":"","vendors":[
          {"id":"{{id}}","runtime":"codex","models":["gpt-5.6"],
           "health":{"enabled":true,"cliFound":true,"version":"1","auth":"own auth","note":""},
           "slots":{"total":{{total}},"ready":{{ready}},"coolingDown":{{cooling}},"needsSignIn":{{signedOut}} } } ] }
        """;

    private void SignIn(string token = "a-token") =>
        TeamServerAuth.WriteToken(TeamServerAuth.TokenPath(_dataDir, Server), token);

    public void Dispose()
    {
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutATokenItSaysSoAndDoesNotAskTheServer()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0));
        var probe = new RemoteProbe(new HttpClient(handler));

        var health = await probe.RunAsync(Vendor, enabled: true, _dataDir);

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain("Team servers section");
        handler.Requests.Should().Be(0, "whether this machine signed in is a question about a file");
    }

    [Fact]
    public async Task ADisabledVendorIsNotProbed()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0));

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: false, _dataDir);

        health.Note.Should().Be("disabled in settings");
        handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task AReadyVendorCarriesTheServersOwnSlotCount()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(total: 3, ready: 2, cooling: 1, signedOut: 0));

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.Auth.Should().Be("server token");
        health.Note.Should().Contain("2 of 3");
        health.Version.Should().Contain("0.5.1");
    }

    [Fact]
    public async Task TheRequestCarriesTheTokenAndTheContractVersion()
    {
        SignIn("the-token");
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0));

        await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        var request = handler.Seen[0];
        // The one canonical spelling, so a server whose URL was saved with a slash is not asked for
        // `…com//api/catalog` and then called unhealthy for answering 404.
        request.RequestUri!.ToString().Should().Be("https://coai.example.com/api/catalog");
        request.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer the-token");
        request.Headers.GetValues(RemoteAsk.ContractHeader).Should().ContainSingle().Which.Should().Be("1");
    }

    [Fact]
    public async Task AVendorTheServerDoesNotOfferNamesWhatItDoesOffer()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0, id: "claude"));

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.Auth.Should().Be("unavailable");
        // Naming the offer is the difference between "unavailable" and a person fixing a typo.
        health.Note.Should().Contain("does not offer").And.Contain("claude");
    }

    [Fact]
    public async Task AllAccountsSignedOutIsTheOperatorsProblemAndSaysSo()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(total: 2, ready: 0, cooling: 0, signedOut: 2));

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.Note.Should().Contain("signed out").And.Contain("operator");
        health.Note.Should().NotContain("by themselves", "signed-out accounts never come back on their own");
    }

    [Fact]
    public async Task AllAccountsCoolingDownSaysTheyComeBack()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(total: 2, ready: 0, cooling: 2, signedOut: 0));

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.Note.Should().Contain("rate-limited").And.Contain("by themselves");
    }

    [Fact]
    public async Task NoAccountsAtAllPointsAtTheOperator()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(total: 0, ready: 0, cooling: 0, signedOut: 0));

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.Note.Should().Contain("no accounts configured");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "sign in again")]
    [InlineData(HttpStatusCode.Forbidden, "company domain")]
    [InlineData(HttpStatusCode.UpgradeRequired, "older than")]
    [InlineData(HttpStatusCode.InternalServerError, "no handling for")]
    public async Task EachRefusalIsItsOwnSentence(HttpStatusCode status, string expected)
    {
        SignIn();
        var handler = new StubHandler(status, """{"error":"nope"}""");

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain(expected);
    }

    [Fact]
    public async Task AnAuthRefusalIsNotReportedAsAnAbsentServer()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.Unauthorized, "{}");

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        // A server that answered 401 IS there. Reporting it as absent sends somebody to restart a
        // server that is running perfectly.
        health.CliFound.Should().BeTrue();
    }

    [Fact]
    public async Task AnUnreachableServerIsAnOutageNotARefusal()
    {
        SignIn();
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.CliFound.Should().BeFalse();
        health.Note.Should().Contain("could not be reached");
    }

    [Fact]
    public async Task ABodyThatIsNotACatalogIsNamedRatherThanCrashedOn()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, "<html>sign in</html>");

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(Vendor, enabled: true, _dataDir);

        health.Note.Should().Contain("could not read");
    }

    [Fact]
    public async Task AGoodAnswerIsRememberedSoOpeningThePanelDoesNotHammerTheServer()
    {
        SignIn();
        var now = DateTime.UtcNow;
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0));
        var probe = new RemoteProbe(new HttpClient(handler), () => now);

        await probe.RunAsync(Vendor, enabled: true, _dataDir);
        await probe.RunAsync(Vendor, enabled: true, _dataDir);
        now = now.AddSeconds(59);
        await probe.RunAsync(Vendor, enabled: true, _dataDir);

        handler.Requests.Should().Be(1);

        now = now.AddSeconds(2);
        await probe.RunAsync(Vendor, enabled: true, _dataDir);
        handler.Requests.Should().Be(2, "a minute later the answer is worth asking for again");
    }

    [Fact]
    public async Task AFailureIsNotRetriedSOONERThanASuccessWouldBe()
    {
        // The finding this test exists for: the first draft held a good answer for 60 s and a bad
        // one for 15, so a server that was DOWN got asked four times as often as one that was up —
        // and the moment it came under load was the moment every client polled it hardest. Raised
        // twice on the plan round.
        SignIn();
        var now = DateTime.UtcNow;
        var handler = new StubHandler(_ => throw new HttpRequestException("down"));
        var probe = new RemoteProbe(new HttpClient(handler), () => now);

        await probe.RunAsync(Vendor, enabled: true, _dataDir);
        now = now.Add(RemoteProbe.Fresh) - TimeSpan.FromSeconds(1);
        await probe.RunAsync(Vendor, enabled: true, _dataDir);

        handler.Requests.Should().Be(1, "a failing server must never be asked more often than a healthy one");
    }

    [Fact]
    public async Task RepeatedFailuresBackOffAndThenStopGrowing()
    {
        SignIn();
        var now = DateTime.UtcNow;
        var handler = new StubHandler(_ => throw new HttpRequestException("down"));
        var probe = new RemoteProbe(new HttpClient(handler), () => now);

        // Each attempt is made exactly at the end of the previous wait, so the number of requests
        // over a fixed span is what the doubling actually controls.
        var waits = new List<TimeSpan>();
        var expected = RemoteProbe.FirstBackoff;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var before = handler.Requests;
            await probe.RunAsync(Vendor, enabled: true, _dataDir);
            handler.Requests.Should().Be(before + 1, "the wait had elapsed");
            waits.Add(expected);
            now = now.Add(expected);
            expected = expected * 2 > RemoteProbe.MaxBackoff ? RemoteProbe.MaxBackoff : expected * 2;
        }

        waits.Should().BeInAscendingOrder();
        waits[^1].Should().Be(RemoteProbe.MaxBackoff, "it caps rather than growing forever");
    }

    [Fact]
    public async Task OneGoodAnswerClearsTheBackoff()
    {
        SignIn();
        var now = DateTime.UtcNow;
        var up = false;
        var handler = new StubHandler(_ => up
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalog(1, 1, 0, 0)) }
            : throw new HttpRequestException("down"));
        var probe = new RemoteProbe(new HttpClient(handler), () => now);

        await probe.RunAsync(Vendor, enabled: true, _dataDir);
        now = now.Add(RemoteProbe.FirstBackoff);
        await probe.RunAsync(Vendor, enabled: true, _dataDir);
        now = now.Add(RemoteProbe.FirstBackoff * 2);

        up = true;
        (await probe.RunAsync(Vendor, enabled: true, _dataDir)).Auth.Should().Be("server token");

        // Back to the ordinary freshness, not to a wait earned while it was down.
        now = now.Add(RemoteProbe.Fresh).AddSeconds(1);
        up = false;
        var requests = handler.Requests;
        await probe.RunAsync(Vendor, enabled: true, _dataDir);
        handler.Requests.Should().Be(requests + 1);
    }

    [Fact]
    public async Task SigningInAgainIsNotHeldBehindTheOldTokensBackoff()
    {
        // Otherwise the cure for a 401 appears not to work: a person signs in again and the panel
        // keeps showing the rejection for up to ten minutes, so they sign in a third time.
        SignIn("stale-token");
        var now = DateTime.UtcNow;
        var handler = new StubHandler(request =>
            request.Headers.GetValues("Authorization").Single() == "Bearer fresh-token"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Catalog(1, 1, 0, 0)) }
                : new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
        var probe = new RemoteProbe(new HttpClient(handler), () => now);

        (await probe.RunAsync(Vendor, enabled: true, _dataDir)).Auth.Should().Be("unavailable");

        SignIn("fresh-token");
        (await probe.RunAsync(Vendor, enabled: true, _dataDir)).Auth
            .Should().Be("server token", "a new token is a new question, not the old one repeated");
    }

    [Fact]
    public async Task AVendorWithNoServerUrlSaysSoRatherThanAskingNobody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0));

        var health = await new RemoteProbe(new HttpClient(handler))
            .RunAsync(new VendorIdentity("codex", "remote", ""), enabled: true, _dataDir);

        health.Note.Should().Contain("no server URL");
        handler.Requests.Should().Be(0);
    }

    /// <summary>
    /// A remote row that does not record the name its SERVER knows it by says THAT, rather than
    /// reporting its own id as a vendor nobody has heard of.
    /// </summary>
    /// <remarks>
    /// Rows are <c>&lt;server&gt;-&lt;vendor&gt;</c> and <c>VendorOnServer</c> falls back to the row id
    /// when <c>remoteVendor</c> is empty — which is right for a hand-written row someone named
    /// <c>claude</c>, and misleading for every row the panel generated. Until 2026-09-07 the panel
    /// generated ALL of them without the field, so the sentence a person actually met was "the Team
    /// server does not offer a vendor called 'remsoftdev-claude'" — true, and pointing at a spelling
    /// mistake rather than at the missing field. The note may not go further than that: the id here
    /// MAY be one the panel generated, and nothing at the probe can tell that from a name somebody
    /// typed on purpose. Accepted finding, story 1.1 code round.
    /// </remarks>
    [Fact]
    public async Task ARowThatCannotNameItsServersVendorSaysSo_RatherThanBlamingATypo()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0, id: "claude"));
        var unnamed = new VendorIdentity("remsoftdev-claude", "remote", Server);

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(unnamed, enabled: true, _dataDir);

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain(
            "does not record which vendor",
            "the row id is not a name anybody typed, so reporting it as an unknown vendor sends people hunting for a typo");
        health.Note.Should().Contain("claude", "what the server does offer is still the useful half");
    }

    /// <summary>
    /// Two rows that ask the server the same question, and must not share one answer.
    /// </summary>
    /// <remarks>
    /// The cache is keyed by server, the name being asked about, and the token — and the name is
    /// <c>VendorOnServer</c>, which two DIFFERENT rows can produce: one that records <c>codex</c>,
    /// and one called <c>codex</c> that records nothing and falls back to its id. They deserve
    /// different sentences, and until the key said so the second row to ask was handed the first
    /// one's. Found by the automated reviewer on this change's pull request.
    /// </remarks>
    [Fact]
    public async Task ARecordedNameAndAFallbackDoNotShareOneCachedAnswer()
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0, id: "claude"));
        var probe = new RemoteProbe(new HttpClient(handler));

        var fallback = await probe.RunAsync(
            new VendorIdentity("codex", "remote", Server), enabled: true, _dataDir);
        var recorded = await probe.RunAsync(
            new VendorIdentity("remsoftdev-codex", "remote", Server, "codex"), enabled: true, _dataDir);

        fallback.Note.Should().Contain("does not record which vendor");
        recorded.Note.Should().NotContain(
            "does not record which vendor",
            "this row DOES record one — it was handed the other row's answer out of the cache");
    }

    /// <summary>
    /// Saying WHICH name was tried costs no extra request, for either shape of row.
    /// </summary>
    /// <remarks>
    /// The message assertions above would all still pass if the richer note were built by asking the
    /// catalog a second time, and `providers` is called on every panel open against a server that is
    /// running other people's reviews. Accepted finding, this story's plan round: a promise about
    /// request count needs an assertion about request count.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("cluade")]
    public async Task NeitherShapeOfRowCostsTheServerASecondRequest(string recorded)
    {
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0, id: "claude"));
        var probe = new RemoteProbe(new HttpClient(handler));
        var vendor = new VendorIdentity("remsoftdev-claude", "remote", Server, recorded);

        var first = await probe.RunAsync(vendor, enabled: true, _dataDir);
        var second = await probe.RunAsync(vendor, enabled: true, _dataDir);

        handler.Requests.Should().Be(1, "the second call must come from the cache, as it always did");
        second.Note.Should().Be(first.Note, "a cached answer is handed back whole, not re-derived");
    }

    [Fact]
    public async Task ARowThatDOESNameItsVendorStillGetsThePlainNotOfferedSentence()
    {
        // The complement, so the new sentence cannot leak onto rows it is not about: this row states
        // its server's own name for the vendor and that name is simply wrong. A typo IS the diagnosis
        // here, and the message must stay the one that says so.
        SignIn();
        var handler = new StubHandler(HttpStatusCode.OK, Catalog(1, 1, 0, 0, id: "claude"));
        var misnamed = new VendorIdentity("remsoftdev-claude", "remote", Server, "cluade");

        var health = await new RemoteProbe(new HttpClient(handler)).RunAsync(misnamed, enabled: true, _dataDir);

        // Whole-string, not Contain: the requirement is that this branch stays byte-for-byte what it
        // was, and `Contain` cannot fail on an appended sentence — which is precisely the leak this
        // test exists to catch. Accepted finding, this story's code round.
        health.Note.Should().Be(
            $"the Team server at {Server} does not offer a vendor called 'cluade' — it offers claude");
    }
}

/// <summary>The process probe's own remote arm — the one that must never shell out.</summary>
public sealed class VendorProbeRemoteArmTests
{
    [Fact]
    public async Task ARemoteVendorIsNeverAskedForItsVersion()
    {
        // The executable a remote vendor names is coai-mcp ITSELF, so without this arm the probe
        // would have run this binary against itself and reported whatever it printed as a vendor's
        // health.
        var launcher = new ThrowingLauncher();

        var health = await VendorProbe.RunAsync(
            launcher, new VendorIdentity("codex", "remote", "https://s"), enabled: true,
            executablePath: "", model: "", hasVaultKey: false);

        launcher.Started.Should().BeFalse();
        health.Auth.Should().Be("unavailable");
    }

    [Fact]
    public async Task TheProbeItIsGivenIsTheOneThatAnswers()
    {
        var launcher = new ThrowingLauncher();
        var expected = new VendorHealth(true, true, "server 1.0", "server token", "2 of 3 ready");

        var health = await VendorProbe.RunAsync(
            launcher, new VendorIdentity("codex", "remote", "https://s"), enabled: true,
            executablePath: "", model: "", hasVaultKey: false,
            remoteHealth: (_, _, _) => Task.FromResult(expected));

        health.Should().Be(expected);
        launcher.Started.Should().BeFalse();
    }

    private sealed class ThrowingLauncher : Runners.Processes.IProcessLauncher
    {
        public bool Started { get; private set; }

        public Task<Runners.Processes.ProcessResult> RunAsync(
            Runners.Processes.ProcessRequest request, CancellationToken ct = default)
        {
            Started = true;

            throw new InvalidOperationException("a remote vendor must never start a process");
        }
    }
}
