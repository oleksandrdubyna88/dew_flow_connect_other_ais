using CoaiMcp;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A review queued behind a parked Team-server account hears when it can run — story 5.3b of
/// <c>todo/PLAN_every_round_is_counted.md</c>, the shim's half.
/// </summary>
/// <remarks>
/// <para>The server now says, in its status's optional <c>waiting</c> field, why a queued job cannot start —
/// <i>every account is rate-limited; the first comes back at …</i>. The shim read <c>position</c> and nothing else, so
/// the person saw "queued … 0 ahead of it" for as long as the account stayed parked, which reads like a hang.</para>
/// <para><b>What must hold.</b> The poll keeps the sentence; the queued progress line carries it after the queue
/// position; the line stays a PROGRESS line (<see cref="RemoteAsk.IsProgress"/>), so it is never picked as the reason a
/// reviewer stopped; and an older server, which sends no such field, gives the line it always gave.</para>
/// </remarks>
public sealed class AQueuedReviewHearsWhenItCanRunTests
{
    private const string Server = "https://coai.example.com";
    private const string Parked = "every account is rate-limited; the first comes back at 2026-10-14 04:49:09Z";

    private static AskRemote.Job Job() =>
        new(Server, "gemini", "m", "Architecture", "p", "o", "j", "t", TimeSpan.FromMinutes(5), 300);

    [Fact]
    public void ThePoll_KeepsTheServersWaitingSentence()
    {
        var poll = RemoteAsk.ReadPoll($$"""{"id":"1","status":"queued","position":0,"waiting":"{{Parked}}"}""")!;

        poll.Waiting.Should().Be(Parked);
        RemoteAsk.ReadPoll("""{"id":"1","status":"queued","position":2}""")!.Waiting.Should().BeEmpty("an older server sends no such field");
    }

    [Fact]
    public void TheQueuedLine_SaysWhenTheReviewCanRun_AndStaysAProgressLine()
    {
        var said = new List<string>();
        var poll = RemoteAsk.ReadPoll($$"""{"id":"1","status":"queued","position":0,"waiting":"{{Parked}}"}""")!;

        AskRemote.PollTracker.Start.After(new AskRemote.PollAttempt(poll, null, false), Job(), said.Add);

        var line = said.Should().ContainSingle().Subject;
        line.Should().Contain("queued on the Team server").And.Contain(Parked, "the person is told when the parked account comes back");
        RemoteAsk.IsProgress(line).Should().BeTrue("a progress note must never be read as the reason a reviewer stopped");
    }

    [Fact]
    public void AnOlderServersQueuedLine_IsUnchanged()
    {
        var said = new List<string>();
        var poll = RemoteAsk.ReadPoll("""{"id":"1","status":"queued","position":2}""")!;

        AskRemote.PollTracker.Start.After(new AskRemote.PollAttempt(poll, null, false), Job(), said.Add);

        said.Should().Equal($"gemini: {RemoteAsk.QueuedMessage(Server, 2)}");
    }
}
