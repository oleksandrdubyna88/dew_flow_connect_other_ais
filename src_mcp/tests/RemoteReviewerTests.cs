using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One server URL has one token path, whatever spelling either side wrote.
/// </summary>
/// <remarks>
/// The vector below is SHARED with the extension: the panel writes the token file and the shim reads
/// it, and if the two normalise differently a person signs in successfully and is told they are not
/// signed in one second later — then signs in again, and it happens again.
/// </remarks>
public sealed class TeamServerAuthTests
{
    [Theory]
    // The pair that motivated this: the panel saves a trailing slash, a vendor row usually has none.
    [InlineData("https://coai.example.com", "https://coai.example.com/")]
    [InlineData("https://coai.example.com", "https://coai.example.com///")]
    // A default port is not part of the identity; the same server written both ways is one server.
    [InlineData("https://coai.example.com", "https://coai.example.com:443")]
    [InlineData("http://coai.example.com", "http://coai.example.com:80")]
    // Scheme and host are case-insensitive by the URL spec, so two spellings are one server.
    [InlineData("https://coai.example.com", "HTTPS://COAI.EXAMPLE.COM/")]
    // A path is part of the identity — a server can live under one.
    [InlineData("https://host/team", "https://host/team/")]
    public void OneServerHasOneFingerprintHoweverItIsSpelled(string a, string b) =>
        TeamServerAuth.Fingerprint(a).Should().Be(TeamServerAuth.Fingerprint(b));

    [Theory]
    // A NON-default port is part of the identity: two servers on one host are two servers.
    [InlineData("https://host", "https://host:8443")]
    [InlineData("https://host/a", "https://host/b")]
    [InlineData("https://host", "http://host")]
    public void DifferentServersHaveDifferentFingerprints(string a, string b) =>
        TeamServerAuth.Fingerprint(a).Should().NotBe(TeamServerAuth.Fingerprint(b));

    [Fact]
    public void TheSameNormalisationBuildsTheREQUESTUrlToo() =>
        // Normalising only for the hash left the trailing slash in the request, so `{url}/api/…`
        // became `…com//api/…` — a 404 from a server the panel then called unhealthy, with a token
        // that matched perfectly. (codex, plan round.)
        TeamServerAuth.Endpoint("https://coai.example.com/", "/api/catalog")
            .Should().Be("https://coai.example.com/api/catalog");

    [Fact]
    public void AFingerprintIsShortEnoughToReadAndLongEnoughNotToCollide()
    {
        var fingerprint = TeamServerAuth.Fingerprint("https://coai.example.com");

        fingerprint.Should().HaveLength(TeamServerAuth.HashLength);
        fingerprint.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void SomethingThatIsNotAUrlDoesNotThrow() =>
        // This runs while building a command line. A bad URL should fail at the request with a
        // sentence, not here with a stack trace.
        TeamServerAuth.Normalise("not a url at all").Should().Be("not a url at all");

    [Fact]
    public void AnAbsentTokenIsEmptyRatherThanAnException() =>
        // "Not signed in" is an ordinary state with its own exit code, not a fault.
        TeamServerAuth.ReadToken(Path.Combine(Path.GetTempPath(), "coai-no-such-" + Guid.NewGuid()))
            .Should().BeEmpty();

    [Fact]
    public void ATokenSurvivesAWriteAndARead()
    {
        var dir = Path.Combine(Path.GetTempPath(), "coai-token-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = TeamServerAuth.TokenPath(dir, "https://coai.example.com/");
            TeamServerAuth.WriteToken(path, "  a-server-token  ");

            TeamServerAuth.ReadToken(path).Should().Be("a-server-token");
            // The same server, spelled without the slash, finds the same file.
            TeamServerAuth.ReadToken(TeamServerAuth.TokenPath(dir, "https://coai.example.com"))
                .Should().Be("a-server-token");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

/// <summary>Every exit the shim can take, and the sentence that belongs to it.</summary>
public sealed class RemoteAskTests
{
    [Fact]
    public void AnAcceptedReviewYieldsItsId() =>
        RemoteAsk.AcceptedId("""{"id":"123-abc","position":2}""").Should().Be("123-abc");

    [Fact]
    public void ABodyThatIsNotAnAcceptanceYieldsNothingRatherThanThrowing()
    {
        // Something in front of the server can answer 200 with a login page.
        RemoteAsk.AcceptedId("<html>sign in</html>").Should().BeEmpty();
        RemoteAsk.AcceptedId("").Should().BeEmpty();
    }

    [Theory]
    [InlineData("queued", RemoteState.Queued)]
    [InlineData("running", RemoteState.Running)]
    [InlineData("done", RemoteState.Done)]
    [InlineData("failed", RemoteState.Failed)]
    public void EachStatusIsRead(string status, RemoteState expected) =>
        RemoteAsk.ReadPoll($$"""{"id":"1","status":"{{status}}","position":0}""")!.State.Should().Be(expected);

    [Fact]
    public void ADoneAnswerCarriesTheVendorsRawTextAndItsTokens()
    {
        var poll = RemoteAsk.ReadPoll(
            """{"id":"1","status":"done","position":0,"answer":"the raw answer","tokensIn":7,"tokensOut":11}""")!;

        poll.Answer.Should().Be("the raw answer", "parsing belongs to the round, not to this shim");
        poll.TokensIn.Should().Be(7);
        poll.TokensOut.Should().Be(11);
    }

    [Fact]
    public void ARubbishBodyIsNullRatherThanAnException() =>
        RemoteAsk.ReadPoll("not json").Should().BeNull();

    [Theory]
    [InlineData(401, RemoteAsk.NotSignedIn)]
    [InlineData(403, RemoteAsk.NotSignedIn)]
    [InlineData(426, RemoteAsk.TooOld)]
    [InlineData(202, RemoteAsk.Ok)]
    public void EachHandledStatusHasItsOwnExit(int status, int exit) =>
        RemoteAsk.ExitForStatus(status).Should().Be(exit);

    [Theory]
    [InlineData(500)]
    [InlineData(404)]
    [InlineData(429)]
    public void AStatusWithNoBranchIsNullSoTheCallerCanSaySo(int status) =>
        // Null rather than a guess: the caller then produces "answered N, which this client has no
        // handling for", which is the difference between a crash and a sentence. (codex, plan round.)
        RemoteAsk.ExitForStatus(status).Should().BeNull();

    [Fact]
    public void TheNotSignedInSentenceSaysWhereToGo() =>
        RemoteAsk.NotSignedInMessage("https://coai.example.com")
            .Should().Contain("Team servers section").And.Contain("coai.example.com");

    [Fact]
    public void BeingRejectedAndBeingForbiddenAreDifferentSentences()
    {
        // Signing in again fixes one and cannot fix the other. Telling somebody outside the company
        // domain to try again would waste their afternoon.
        RemoteAsk.RejectedMessage("https://s").Should().Contain("sign in again");
        RemoteAsk.ForbiddenMessage("https://s").Should().Contain("company domain").And.NotContain("sign in again");
    }

    [Fact]
    public void GivingUpSaysItCancelledAndWhatToDo()
    {
        var message = RemoteAsk.TooSlowMessage("https://s", TimeSpan.FromSeconds(90), 4);

        message.Should().Contain("4 in the queue");
        message.Should().Contain("cancelled", "otherwise a person cannot tell whether it is still costing them");
        message.Should().Contain("COAI_REVIEWER_TIMEOUT_MINUTES");
    }

    [Fact]
    public void AFailedReviewCarriesTheVendorsOwnReason() =>
        RemoteAsk.FailedMessage("codex", "rate_limited", "session limit · resets 9:30pm")
            .Should().Contain("codex").And.Contain("rate_limited").And.Contain("9:30pm");

    [Fact]
    public void AVeryLongServerSentenceIsCutRatherThanPrintedWhole() =>
        RemoteAsk.UnexpectedMessage("https://s", 500, new string('x', 5000)).Length.Should().BeLessThan(400);

    [Fact]
    public void TheUsageLineIsWhatTheRuntimeReadsBack()
    {
        var line = RemoteAsk.UsageLine(12, 34);

        line.Should().Contain("\"tokensIn\":12").And.Contain("\"tokensOut\":34");
    }

    [Fact]
    public void TheRequestNamesTheVendorTheModelAndTheRole()
    {
        var body = RemoteAsk.RequestBody("codex", "gpt-5.6-luna", "Architecture", "review this", 600);

        body.Should().Contain("\"vendor\":\"codex\"")
            .And.Contain("\"model\":\"gpt-5.6-luna\"")
            .And.Contain("\"role\":\"Architecture\"")
            .And.Contain("\"timeoutSeconds\":600");
    }
}

/// <summary>The adapter, and the claim file that makes a killed shim still cancellable.</summary>
public sealed class RemoteRuntimeTests
{
    private static ReviewerInvocation Build(string dataDir)
    {
        var output = Path.Combine(Path.GetTempPath(), "coai-remote-" + Guid.NewGuid().ToString("N"));

        return new RemoteRuntime("codex", "https://coai.example.com/").Build(
            ReviewRole.Architecture,
            "review this",
            output,
            Path.Combine(output, "schema.json"),
            output,
            new ReviewerSettings("codex") { Model = "m", DataDir = dataDir, Timeout = TimeSpan.FromMinutes(10) });
    }

    [Fact]
    public void TheCommandLineNamesTheServerTheVendorAndTheTokenPATH()
    {
        var args = Build("/data").Request.Arguments;

        args.Should().Contain("--ask-remote");
        args.Should().Contain("https://coai.example.com", "the trailing slash is normalised away once, here");
        args.Should().Contain("--token-file");
        // A PATH, never the token itself. It is not in argv, not in settings.json, not in a log line.
        args.Should().NotContain(a => a.Contains("Bearer"));
    }

    [Fact]
    public void ThereIsNoSharedResourceBecauseTheServerHasItsOwnQueue() =>
        // A client-side per-resource semaphore would serialise reviews the server can run at once.
        Build("/data").SharedResource.Should().BeEmpty();

    [Fact]
    public void TheTwoClocksAreSeparateFlags()
    {
        var args = Build("/data").Request.Arguments.ToList();

        // Sending the shim's own deadline as the VENDOR's budget asked the server for an
        // eight-second review and got a 400 naming its range. Found by running it for real.
        args.Should().Contain("--timeout-seconds");
        args.Should().Contain("--vendor-timeout-seconds");
        var shim = int.Parse(args[args.IndexOf("--timeout-seconds") + 1]);
        var vendor = int.Parse(args[args.IndexOf("--vendor-timeout-seconds") + 1]);
        shim.Should().BeLessThan(vendor, "reaching the shim's deadline must produce a sentence, not a kill");
    }

    [Fact]
    public void AClaimSurvivesSoAKilledShimCanStillBeCancelled()
    {
        var file = Path.Combine(Path.GetTempPath(), "coai-claim-" + Guid.NewGuid().ToString("N") + ".job");
        try
        {
            RemoteRuntime.Claim(file, "https://coai.example.com/", "123-abc");

            // The parent reads this after killing the child, because a killed process runs no
            // cleanup and this file is the only thing left that can stop the job.
            var (server, id) = RemoteRuntime.ReadClaim(file);
            server.Should().Be("https://coai.example.com");
            id.Should().Be("123-abc");
        }
        finally
        {
            RemoteRuntime.Forget(file);
        }
    }

    [Fact]
    public void NoClaimIsEmptyRatherThanAnException() =>
        RemoteRuntime.ReadClaim(Path.Combine(Path.GetTempPath(), "coai-none-" + Guid.NewGuid()))
            .Should().Be((string.Empty, string.Empty));

    [Fact]
    public void ForgettingAClaimThatIsNotThereIsNotAnError()
    {
        var forget = () => RemoteRuntime.Forget(Path.Combine(Path.GetTempPath(), "coai-none-" + Guid.NewGuid()));

        forget.Should().NotThrow();
    }
}

/// <summary>The registry: every place that had to learn the name.</summary>
public sealed class RemoteRegistryTests
{
    [Fact]
    public void TheRuntimeNameExists() =>
        ReviewerRuntimeSelector.RuntimeNames.Should().Contain("remote");

    [Fact]
    public void ARemoteVendorGetsTheRemoteAdapterAndNotTheCodexOne()
    {
        // A remote row HAS a base URL — the Team server's — so without an arm before the base-URL
        // one, every Team server vendor resolved to a custom codex endpoint. This file's own
        // remarks predicted that split before the name existed.
        var vendor = new VendorIdentity("my-team", "remote", "https://coai.example.com");

        RuntimeResolution.NameOf(vendor).Should().Be("remote");
        RuntimeResolution.For(vendor).Should().BeOfType<RemoteRuntime>();
    }

    [Fact]
    public void ANameAloneCannotBuildARemoteVendorAndThatIsDeliberate()
    {
        // The guard against somebody adding a `remote` arm to `Named` to make it match the runtime
        // name set. A Team server vendor needs the server's URL, which a name does not carry, so an
        // arm there could only pass an empty one — an adapter whose command line no shim can use.
        // `For` holds the whole identity and builds it before this lookup is reached.
        ReviewerRuntimeSelector.Named("remote", "my-team").Should().BeNull();
        RuntimeResolution.For(new VendorIdentity("my-team", "remote", "https://s")).Should().BeOfType<RemoteRuntime>();
    }

    [Fact]
    public void ACodexVendorWithABaseUrlIsStillACustomCodex() =>
        RuntimeResolution.NameOf(new VendorIdentity("x", "", "https://api.example.com")).Should().Be("codex");

    [Fact]
    public void AuthForARemoteVendorIsAboutTheSERVERTokenNotAKey()
    {
        var vendor = new VendorIdentity("my-team", "remote", "https://coai.example.com/");

        // Even holding a vault key, a remote vendor's authentication is the session this machine
        // holds — saying "vault key" would send somebody to configure the wrong thing.
        RuntimeResolution.AuthOf(vendor, hasVaultKey: true, hasServerToken: true).Auth.Should().Be("server token");
        RuntimeResolution.AuthOf(vendor, hasVaultKey: true, hasServerToken: false).Auth.Should().Be("unavailable");
        RuntimeResolution.AuthOf(vendor, hasVaultKey: false, hasServerToken: false).Note
            .Should().Contain("Team servers section");
    }

    [Fact]
    public void EveryOtherVendorsAuthIsUnchanged()
    {
        // The remote arm is checked first, so this is the assertion that it did not swallow anybody.
        RuntimeResolution.AuthOf(new VendorIdentity("codex", "codex", ""), hasVaultKey: false).Auth
            .Should().Be("own auth");
        RuntimeResolution.AuthOf(new VendorIdentity("x", "", "https://api.example.com"), hasVaultKey: true).Auth
            .Should().Be("vault key");
    }
}
