using CoaiMcp;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What one poll means for the loop — every branch, without a server.
/// </summary>
/// <remarks>
/// <para>The loop these decide for is covered end to end by <see cref="RemoteShimScenarioTests"/>,
/// which runs the real binary in its own process. That is the right test for the wiring and the wrong
/// one for the branches: a separate process cannot be steered into "three dropped polls in a row" on
/// demand, and its lines are invisible to coverage anyway.</para>
/// <para>So the decision was extracted as a pure function — which the repository's complexity rule
/// wanted regardless — and this is it, exercised directly.</para>
/// </remarks>
public sealed class RemotePollDecisionTests
{
    private const string Server = "https://coai.example.com";

    private static RemotePoll Poll(RemoteState state, string raw = "queued", int position = 0) =>
        new(state, raw, position, "", "", "", 0, 0);

    private static AskRemote.PollAttempt Answered(RemotePoll poll) => new(poll, null, false);

    [Fact]
    public void AQueuedReviewIsWaitedFor() =>
        AskRemote.Decide(Answered(Poll(RemoteState.Queued)), 0, Server)
            .Verdict.Should().Be(AskRemote.PollVerdict.KeepWaiting);

    [Fact]
    public void ARunningReviewIsWaitedForToo() =>
        AskRemote.Decide(Answered(Poll(RemoteState.Running)), 0, Server)
            .Verdict.Should().Be(AskRemote.PollVerdict.KeepWaiting);

    [Theory]
    [InlineData(RemoteState.Done)]
    [InlineData(RemoteState.Failed)]
    public void ATerminalStateEndsTheLoopWithItsAnswer(RemoteState state)
    {
        var step = AskRemote.Decide(Answered(Poll(state)), 0, Server);

        step.Verdict.Should().Be(AskRemote.PollVerdict.Finish);
        step.Answer.Should().NotBeNull("the caller writes it out and reads its usage");
    }

    [Fact]
    public void AStatusThisClientCannotReadSTOPSTheReviewAndNamesIt()
    {
        // The whole point of RemoteState.Unknown: this used to be read as "still queued", so a
        // terminal status from a newer server was waited out to the deadline and then reported as
        // slowness. Raised on the code round.
        var step = AskRemote.Decide(Answered(Poll(RemoteState.Unknown, raw: "reticulating")), 0, Server);

        step.Verdict.Should().Be(AskRemote.PollVerdict.Stop);
        step.Exit.Should().Be(RemoteAsk.TooOld, "the cure is an update, not a longer wait");
        step.Note.Should().Contain("reticulating", "naming what the server said is what makes it actionable");
    }

    [Fact]
    public void ARefusalTheServerMEANTIsNotRetried()
    {
        // A bad token or a contract too old. Polling again only repeats it.
        var step = AskRemote.Decide(new AskRemote.PollAttempt(null, RemoteAsk.NotSignedIn, false), 0, Server);

        step.Verdict.Should().Be(AskRemote.PollVerdict.Stop);
        step.Exit.Should().Be(RemoteAsk.NotSignedIn);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ADroppedPollIsABlipAndTheReviewCarriesOn(int failuresSoFar) =>
        // A long poll held open across a proxy or a laptop's wifi drops for reasons that have nothing
        // to do with the review. Aborting on the first one abandoned reviews that were running
        // perfectly. Raised on the code round.
        AskRemote.Decide(new AskRemote.PollAttempt(null, null, Transient: true), failuresSoFar, Server)
            .Verdict.Should().Be(AskRemote.PollVerdict.KeepWaiting);

    [Fact]
    public void ThreeDroppedPollsInARowIsAnOutage()
    {
        var step = AskRemote.Decide(new AskRemote.PollAttempt(null, null, Transient: true), 3, Server);

        step.Verdict.Should().Be(AskRemote.PollVerdict.Stop);
        step.Exit.Should().Be(RemoteAsk.Unreachable);
        step.Note.Should().Contain("3 polls in a row");
    }

    [Fact]
    public void ARefusalOUTRANKSTheFailureCount() =>
        // Both true at once: the server's own answer is the one worth reporting, because it says what
        // is wrong, and "could not be reached" would not.
        AskRemote.Decide(new AskRemote.PollAttempt(null, RemoteAsk.NotSignedIn, true), 9, Server)
            .Exit.Should().Be(RemoteAsk.NotSignedIn);

    [Theory]
    // Never longer than what is LEFT: asking for 25 with 6 to go meant a 6-second deadline took 26
    // seconds to notice. Found by running it against a real server.
    [InlineData(6, 6)]
    [InlineData(1, 1)]
    // Never longer than the server holds a poll open for, either.
    [InlineData(600, RemoteAsk.LongPollSeconds)]
    // And never zero or negative, which is not a wait the server would accept.
    [InlineData(0, 1)]
    public void ThePollAsksForTheSMALLEROfWhatIsLeftAndWhatTheServerAllows(int remaining, int expected) =>
        AskRemote.WaitSecondsFor(TimeSpan.FromSeconds(remaining)).Should().Be(expected);

    [Theory]
    [InlineData("", "codex", "p", "o", "t")]
    [InlineData("https://s", "", "p", "o", "t")]
    [InlineData("https://s", "codex", "", "o", "t")]
    [InlineData("https://s", "codex", "p", "", "t")]
    public void ACommandLineNobodyCouldHaveMeantIsBadUsage(
        string server, string vendor, string prompt, string output, string token) =>
        AskRemote.Refusal(server, vendor, prompt, output, token)!.Value.Exit.Should().Be(RemoteAsk.BadUsage);

    [Fact]
    public void NoTokenIsItsOwnRefusalWithItsOwnExit()
    {
        // Not a failure of the server or the network — an ordinary state, so the panel can offer the
        // right button rather than a generic error.
        var refusal = AskRemote.Refusal("https://s", "codex", "p", "o", token: "")!.Value;

        refusal.Exit.Should().Be(RemoteAsk.NotSignedIn);
        refusal.Message.Should().Contain("Team servers section");
    }

    [Fact]
    public void AGoodCommandLineIsNotRefused() =>
        AskRemote.Refusal("https://s", "codex", "p", "o", "a-token").Should().BeNull();

    [Fact]
    public void ABadCommandLineOUTRANKSAMissingToken() =>
        // Telling somebody to sign in when they mistyped the arguments would send them somewhere
        // that cannot help.
        AskRemote.Refusal("", "codex", "p", "o", token: "")!.Value.Exit.Should().Be(RemoteAsk.BadUsage);
}

/// <summary>What the loop remembers between polls.</summary>
public sealed class RemotePollTrackerTests
{
    private static readonly AskRemote.Job AJob = new(
        "https://coai.example.com", "codex", "m", "Architecture",
        "p", "o", "j", "t", TimeSpan.FromMinutes(5), 600);

    private static RemotePoll Poll(RemoteState state, int position) =>
        new(state, state.ToString().ToLowerInvariant(), position, "", "", "", 0, 0);

    [Fact]
    public void APositionIsRememberedSoGivingUpCanReportIt()
    {
        var said = new List<string>();

        var tracker = AskRemote.PollTracker.Start.After(
            new AskRemote.PollAttempt(Poll(RemoteState.Queued, 4), null, false), AJob, said.Add);

        tracker.Position.Should().Be(4);
    }

    [Fact]
    public void TheQueuePositionIsSaidWHILEItStillMatters()
    {
        // It used to be mentioned for the first time in the sentence announcing that the review had
        // been cancelled, which is the worst possible moment to learn it. Raised on the code round.
        var said = new List<string>();

        AskRemote.PollTracker.Start.After(
            new AskRemote.PollAttempt(Poll(RemoteState.Queued, 4), null, false), AJob, said.Add);

        said.Should().ContainSingle().Which.Should().Contain("queued").And.Contain("4 ahead");
    }

    [Fact]
    public void TheSameNewsIsNotRepeatedEverySecond()
    {
        var said = new List<string>();
        var attempt = new AskRemote.PollAttempt(Poll(RemoteState.Queued, 4), null, false);

        var tracker = AskRemote.PollTracker.Start.After(attempt, AJob, said.Add);
        tracker = tracker.After(attempt, AJob, said.Add);
        tracker.After(attempt, AJob, said.Add);

        said.Should().HaveCount(1, "a line every second is the same silence with more scrolling");
    }

    [Fact]
    public void MOVINGToRunningIsWorthSaying()
    {
        var said = new List<string>();

        var tracker = AskRemote.PollTracker.Start.After(
            new AskRemote.PollAttempt(Poll(RemoteState.Queued, 2), null, false), AJob, said.Add);
        tracker.After(new AskRemote.PollAttempt(Poll(RemoteState.Running, 0), null, false), AJob, said.Add);

        said.Should().HaveCount(2);
        said[1].Should().Contain("running");
    }

    [Fact]
    public void ADroppedPollIsCountedAndAnAnswerResetsTheCount()
    {
        var said = new List<string>();
        var dropped = new AskRemote.PollAttempt(null, null, Transient: true);

        var tracker = AskRemote.PollTracker.Start.After(dropped, AJob, said.Add).After(dropped, AJob, said.Add);
        tracker.Failures.Should().Be(2);

        tracker = tracker.After(new AskRemote.PollAttempt(Poll(RemoteState.Queued, 1), null, false), AJob, said.Add);
        tracker.Failures.Should().Be(0, "the server answered, so whatever went wrong is over");
    }
}
