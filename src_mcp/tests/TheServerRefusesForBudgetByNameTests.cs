using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The client half of "no start without budget" — story 4.3 of <c>todo/PLAN_every_round_is_counted.md</c>: the Team
/// server ends a job whose caller can no longer wait with its OWN name on the wire, <c>no_budget</c>, and the shim reads
/// that name — never the sentence beside it — into its own exit, <see cref="RemoteAsk.NoBudget"/>.
/// </summary>
/// <remarks>
/// The seam is the one <c>ARoundHasAnIdOnTheWireTests</c> uses: the real <c>--ask-remote</c> code (<c>AskRemote.RunAsync</c>
/// over <c>Program.Flags</c>, in this process) against a loopback stub that answers what a 4.3 server answers. The old
/// client's road is characterised beside it: a shim built before the name renders it inside its failure sentence and
/// exits <see cref="RemoteAsk.VendorFailed"/>, and nothing throws.
/// </remarks>
public sealed class TheServerRefusesForBudgetByNameTests : IDisposable
{
    private const string Reason = "not started: its caller could wait 7s more when an account came free, and a launch needs at least 10s to start — nothing was sent to the vendor";

    private readonly TempDir _dir = TempDir.For("coai-no-budget-name-");
    private readonly ApiEndpointStub _server = ApiEndpointStub.Start();

    public void Dispose()
    {
        _server.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public async Task TheShim_ReadsNoBudgetByName_AndExitsItsOwnCode()
    {
        _server.Answers = Ending(RemoteAsk.NoBudgetFailure, Reason);

        var (exit, said) = await ShimAsync(Built(TimeSpan.FromSeconds(60)));

        exit.Should().Be(RemoteAsk.NoBudget, said);
        said.Should().Contain("could no longer wait for a launch").And.Contain("Nothing was spent").And.Contain(Reason);
        _server.Requests.Should().NotContain(r => r.Method == "DELETE", "the job is already over; there is nothing to cancel");
    }

    [Fact]
    public async Task TheShim_ReadsTheName_NotTheSentence()
    {
        // The server's sentence under a different name is a vendor failure; the name under a different sentence is not.
        _server.Answers = Ending("timed_out", Reason);
        var (byReason, _) = await ShimAsync(Built(TimeSpan.FromSeconds(60)));

        _server.Answers = Ending(RemoteAsk.NoBudgetFailure, "whatever the server chose to say");
        var (byName, _) = await ShimAsync(Built(TimeSpan.FromSeconds(60)));

        byReason.Should().Be(RemoteAsk.VendorFailed, "a sentence is never the signal");
        byName.Should().Be(RemoteAsk.NoBudget, "the name is");
    }

    [Fact]
    public void AnOlderShim_RendersTheNameInItsSentence_AndNothingThrows()
    {
        // The road a shim built before story 4.3 takes with the new name: RemotePoll reads it as any failure, and the
        // sentence a person sees carries the name — the name an old client shows is one it never crashes on.
        var poll = RemoteAsk.ReadPoll($$"""{"id":"job-1","status":"failed","position":0,"answer":"","seconds":0,"tokensIn":0,"tokensOut":0,"failure":"{{RemoteAsk.NoBudgetFailure}}","reason":"{{Reason}}"}""");

        poll.Should().NotBeNull();
        poll!.State.Should().Be(RemoteState.Failed);
        poll.Failure.Should().Be(RemoteAsk.NoBudgetFailure);
        AskRemote.Decide(new AskRemote.PollAttempt(poll, null, false), 0, "https://srv").Verdict.Should().Be(AskRemote.PollVerdict.Finish);
        RemoteAsk.FailedMessage("codex", poll.Failure, poll.Reason).Should().Contain("(no_budget)").And.Contain(Reason);
    }

    [Fact]
    public void NeitherShimsSentence_IsReadAsARateLimit()
    {
        // The ladder reads the shim's stderr; a refusal it mistook for a throttle would be retried into the same refusal.
        RateLimit.Hit(new ProcessResult(RemoteAsk.NoBudget, string.Empty, RemoteAsk.NoBudgetMessage("codex", "https://srv", Reason), TimedOut: false))
            .Should().BeFalse();
        RateLimit.Hit(new ProcessResult(RemoteAsk.VendorFailed, string.Empty, RemoteAsk.FailedMessage("codex", RemoteAsk.NoBudgetFailure, Reason), TimedOut: false))
            .Should().BeFalse();
        RemoteAsk.NoBudget.Should().NotBe(RemoteAsk.VendorFailed).And.NotBe(RemoteAsk.TooSlow, "its own code, read by whoever reads codes");
    }

    // ---------- the harness ----------

    /// <summary>A 202 to the submit, then a terminal status naming <paramref name="failure"/>.</summary>
    private static Func<ApiEndpointStub.Seen, ApiEndpointStub.Answer> Ending(string failure, string reason) =>
        seen => seen.Method == "POST"
            ? new ApiEndpointStub.Answer(202, """{"id":"job-1","position":0}""")
            : new ApiEndpointStub.Answer(200, $$"""{"id":"job-1","status":"failed","position":0,"answer":"","seconds":0,"tokensIn":0,"tokensOut":0,"failure":"{{failure}}","reason":"{{reason}}"}""");

    private ReviewerInvocation Built(TimeSpan timeout)
    {
        var data = Directory.CreateDirectory(_dir.At("data")).FullName;
        var runtime = new RemoteRuntime("srv-codex", _server.Origin, "codex");
        TeamServerAuth.WriteToken(TeamServerAuth.TokenPath(data, runtime.ServerUrl), "a-token");
        var output = Directory.CreateDirectory(_dir.At("out-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        var built = runtime.Build("Architecture", "review this", output, Path.Combine(output, "schema.json"), output,
            new ReviewerSettings("srv-codex") { Model = "m", DataDir = data, Timeout = timeout });

        return runtime.ForLaunch(built);
    }

    private static async Task<(int Exit, string Said)> ShimAsync(ReviewerInvocation launch)
    {
        var said = new List<string>();
        var exit = await AskRemote.RunAsync(Program.Flags([.. launch.Request.Arguments]), said.Add, TextWriter.Null);

        return (exit, string.Join("\n", said));
    }
}
