using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Catalog;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round has an identity across the wire, and every launch of it says what it is and how long its caller can still
/// wait — story 4.1 of <c>todo/PLAN_every_round_is_counted.md</c>, the client half.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> One <c>POST /api/reviews</c> was one role for one vendor and carried nothing that tied
/// launches together: the server could not count a round, and a retry, a repair or a follow-up looked like three rounds
/// (symptom 3 of the plan). And the shim's own waiting deadline was fixed when the invocation was BUILT: a repair with
/// forty seconds left still told the Team server it could wait six hundred (the risk consultation for epic 4).</para>
/// <para><b>What must hold.</b> The body carries <c>round</c>, <c>attempt</c> and <c>remainingSeconds</c>, each left out
/// when empty and never a 0, under contract 2 still; every launch of one round — first, retry, repair, and a follow-up —
/// carries the one key; the number advertised is the seconds left on the EFFECTIVE deadline of the launch being
/// submitted, and the shim's own wait follows it; and a launch with no budget left is never submitted.</para>
/// <para>The seam: the real <see cref="BoundedScheduler"/>, <see cref="ReviewerExecutor"/> and <see cref="RemoteRuntime"/>
/// over a recording <see cref="IProcessLauncher"/> for what the parent decides; the real <c>--ask-remote</c> code
/// (<c>AskRemote.RunAsync</c> over <c>Program.Flags</c>, in this process) against a loopback stub for what reaches the
/// wire. The process boundary itself is <c>RemoteShimScenarioTests</c>'s, which gains one case for these fields.</para>
/// </remarks>
public sealed class ARoundHasAnIdOnTheWireTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly TempDir _dir = TempDir.For("coai-round-id-");
    private readonly ApiEndpointStub _server = ApiEndpointStub.Start();

    public void Dispose()
    {
        _server.Dispose();
        _dir.Dispose();
    }

    private static string Flag(ProcessRequest request, string flag)
    {
        var argv = request.Arguments;
        for (var i = 0; i < argv.Count - 1; i++)
        {
            if (argv[i] == flag)
            {
                return argv[i + 1];
            }
        }

        return "<absent>";
    }

    private static string Flag(ReviewerInvocation invocation, string flag) => Flag(invocation.Request, flag);

    /// <summary>A launch built by the real adapter for the stub server, signed in, with the given reviewer timeout.</summary>
    private ReviewerInvocation Built(TimeSpan timeout, string role = "Architecture")
    {
        var data = Directory.CreateDirectory(_dir.At("data")).FullName;
        var runtime = new RemoteRuntime("srv-codex", _server.Origin, "codex");
        TeamServerAuth.WriteToken(TeamServerAuth.TokenPath(data, runtime.ServerUrl), "a-token");
        var output = Directory.CreateDirectory(_dir.At("out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

        return runtime.Build(role, "review this", output, Path.Combine(output, "schema.json"), output,
            new ReviewerSettings("srv-codex") { Model = "m", DataDir = data, Timeout = timeout });
    }

    /// <summary>The adapter that built the launch, through the interface — <c>ForLaunch</c> is the interface's seam.</summary>
    private static IReviewerRuntime AdapterOf(ReviewerInvocation invocation) => invocation.Adapter!;

    /// <summary>The 202 and the done poll every submitted review gets from the stub.</summary>
    private static ApiEndpointStub.Answer Accepting(ApiEndpointStub.Seen seen) =>
        seen.Method == "POST"
            ? new ApiEndpointStub.Answer(202, """{"id":"job-1","position":0}""")
            : new ApiEndpointStub.Answer(200, """{"id":"job-1","status":"done","position":0,"answer":"{\"findings\":[]}","tokensIn":1,"tokensOut":2}""");

    /// <summary>The REAL shim code over the launch's argv — <c>Program.Flags</c> and <c>AskRemote.RunAsync</c> — in this process.</summary>
    private static async Task<(int Exit, string Said, TimeSpan Took)> ShimAsync(ReviewerInvocation launch)
    {
        var said = new List<string>();
        var clock = Stopwatch.StartNew();
        var exit = await AskRemote.RunAsync(Program.Flags([.. launch.Request.Arguments]), said.Add, TextWriter.Null);

        return (exit, string.Join("\n", said), clock.Elapsed);
    }

    private IReadOnlyList<JsonElement> Posted() =>
        [.. _server.Requests.Where(r => r.Method == "POST").Select(r => JsonDocument.Parse(r.Body).RootElement.Clone())];

    private static int Number(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) ? value.GetInt32() : -1;

    private static string Text(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : "<absent>";

    // ---------- the body ----------

    [Fact]
    public void TheBody_CarriesRoundAttemptAndRemainingSeconds_EachLeftOutWhenEmpty_AndTheContractStaysTwo()
    {
        var full = RemoteAsk.RequestBody("codex", "m", "Architecture", "p", 600, round: Key, attempt: LaunchAttempts.Retry, remainingSeconds: 42);
        full.Should().Contain($"\"round\":\"{Key}\"").And.Contain("\"attempt\":\"retry\"").And.Contain("\"remainingSeconds\":42");

        RemoteAsk.RequestBody("codex", "m", "Architecture", "p", 600).Should().Be(
            """{"vendor":"codex","model":"m","prompt":"p","role":"Architecture","timeoutSeconds":600}""",
            "a launch that knows none of them sends exactly what contract 2 sent");
        RemoteAsk.RequestBody("codex", "m", "Architecture", "p", 600, remainingSeconds: 0).Should().NotContain("remainingSeconds",
            "a budget of nought is never serialised — a server that dropped a 0 would switch its refusal off for the launch it exists for");
        RemoteAsk.RequestBody("codex", "m", "Architecture", "p", 600, remainingSeconds: -5).Should().NotContain("remainingSeconds");
        RemoteAsk.ContractVersion.Should().Be(2, "the fields are additive and optional; a version the old server does not serve would refuse every review");
    }

    [Fact]
    public void RemainingSeconds_IsTheWholeSecondsLeft_AndNeverBelowNought()
    {
        RemoteAsk.RemainingSeconds(TimeSpan.FromSeconds(29.9)).Should().Be(29, "whole seconds, rounded down — never a second the caller does not have");
        RemoteAsk.RemainingSeconds(TimeSpan.Zero).Should().Be(0);
        RemoteAsk.RemainingSeconds(TimeSpan.FromSeconds(-3)).Should().Be(0);
    }

    // ---------- every launch of one round ----------

    [Fact]
    public async Task ARetryARepairAndAFollowUp_ShareTheRoundsId()
    {
        // The round's work: the first launch and its repair, both stamped with the round's key as the engine stamps them.
        var first = Built(TimeSpan.FromMinutes(10)) with { Round = Key };
        var repair = Built(TimeSpan.FromMinutes(10)) with { Round = Key };
        // A rate limit (→ the ladder's retry), then an answer that will not parse (→ the repair), then a clean one.
        var launcher = new Recording(
            _ => new ProcessResult(1, string.Empty, "429 Too Many Requests", TimedOut: false),
            request => Answering(request, """{"note":"prose, not findings"}"""),
            request => Answering(request, """{"findings": []}"""));

        var results = await new BoundedScheduler(retryLadder: [TimeSpan.FromMilliseconds(1)])
            .RunAllAsync([new ReviewerWork(first, repair)], new ReviewerExecutor(launcher), TestContext.Current.CancellationToken);

        results.Single().Outcome.Should().BeOfType<ReviewerOutcome.Ok>();
        launcher.Requests.Should().HaveCount(3, "first, retry, repair");
        launcher.Requests.Select(r => Flag(r, "--round")).Should().Equal([Key, Key, Key], "every launch of the round is told the round's key");
        launcher.Requests.Select(r => Flag(r, "--attempt")).Should().Equal(
            [LaunchAttempts.First, LaunchAttempts.Retry, LaunchAttempts.Repair], "and what it is inside its turn");

        // The follow-up road: a continuation is derived from the first launch and keeps the key (agy's denied command,
        // the one follow-up this build makes), and a remote launch stamped as one says so on the wire.
        var agy = new AntigravityRuntime().Build("Architecture", "review this", _dir, "schema.json", _dir,
            new ReviewerSettings("antigravity") { Timeout = TimeSpan.FromMinutes(1) }) with { Round = Key };
        var followUp = agy.Adapter!.FollowUp(agy, ADeniedCommandIsAskedAgainTests.Transcript(ADeniedCommandIsAskedAgainTests.DeniedStream, ADeniedCommandIsAskedAgainTests.Denied));
        followUp!.Round.Should().Be(Key, "a follow-up continues the first launch's round");
        Flag(AdapterOf(first).ForLaunch(first with { Attempt = LaunchAttempts.FollowUp }), "--attempt").Should().Be(LaunchAttempts.FollowUp);
    }

    // ---------- the budget advertised is the EFFECTIVE one ----------

    [Fact]
    public async Task AShortenedLaunch_AdvertisesItsEffectiveBudget_NotTheOneItWasBuiltWith()
    {
        _server.Answers = Accepting;
        var built = Built(TimeSpan.FromSeconds(600));
        Flag(built, "--timeout-seconds").Should().Be("590", "as built: the reviewer's six hundred less the shim's ten");

        // Forty seconds left of the reviewer's deadline when its repair is launched — RetryLadder.Lesser, as the executor does.
        var launch = AdapterOf(built).ForLaunch(RetryLadder.Lesser(TimeSpan.FromSeconds(40), built with { Attempt = LaunchAttempts.Repair }));

        Flag(launch, "--timeout-seconds").Should().Be("30", "the shim's own waiting deadline follows the shortened timeout");
        Flag(launch, "--vendor-timeout-seconds").Should().Be("40", "and so does what the vendor is allowed");
        Flag(launch, "--attempt").Should().Be(LaunchAttempts.Repair);

        var (exit, said, _) = await ShimAsync(launch);

        exit.Should().Be(RemoteAsk.Ok, said);
        var posted = Posted().Single();
        Number(posted, "remainingSeconds").Should().BeInRange(25, 30, "the seconds left on the shim's OWN deadline at submit, not the six hundred it was built with");
        Number(posted, "timeoutSeconds").Should().Be(40);
        Text(posted, "attempt").Should().Be(LaunchAttempts.Repair);
    }

    [Fact]
    public async Task TheShimsOwnWait_FollowsTheShortenedDeadline_NotTheOneItWasBuiltWith()
    {
        // A server that never finishes the job: the shim's own deadline is the only thing that ends this.
        _server.Answers = seen => seen.Method == "POST"
            ? new ApiEndpointStub.Answer(202, """{"id":"job-1","position":1}""")
            : new ApiEndpointStub.Answer(200, """{"id":"job-1","status":"queued","position":1}""");
        var built = Built(TimeSpan.FromSeconds(600));
        var launch = AdapterOf(built).ForLaunch(RetryLadder.Lesser(TimeSpan.FromSeconds(16), built with { Attempt = LaunchAttempts.Retry }));
        Flag(launch, "--timeout-seconds").Should().Be("6");

        var (exit, said, took) = await ShimAsync(launch);

        exit.Should().Be(RemoteAsk.TooSlow, said);
        said.Should().Contain("did not finish within", "the shim gave up on its own deadline rather than being killed on the executor's");
        // The sentence names the seconds it WAITED, which the poll gap rounds under the deadline; the clock says which deadline that was.
        took.Should().BeGreaterThan(TimeSpan.FromSeconds(3), "it waited the shortened six seconds out, give or take the poll gap")
            .And.BeLessThan(TimeSpan.FromSeconds(30), "and not the five hundred and ninety it was built with");
        _server.Requests.Should().Contain(r => r.Method == "DELETE", "what it gave up on, it cancelled");
    }

    // ---------- MUST LAUNCH: a healthy short reviewer always advertises a positive budget ----------

    [Fact]
    public async Task AFreshThirtySecondReviewer_AFreshSixtySecondReviewer_AndARepairWithFortySecondsLeft_EachSubmitAPositiveBudget()
    {
        _server.Answers = Accepting;
        var thirty = AdapterOf(Built(TimeSpan.FromSeconds(30))).ForLaunch(Built(TimeSpan.FromSeconds(30)));
        var sixty = AdapterOf(Built(TimeSpan.FromSeconds(60))).ForLaunch(Built(TimeSpan.FromSeconds(60)));
        var longer = Built(TimeSpan.FromSeconds(600));
        var repair = AdapterOf(longer).ForLaunch(RetryLadder.Lesser(TimeSpan.FromSeconds(40), longer with { Attempt = LaunchAttempts.Repair }));

        foreach (var launch in (ReviewerInvocation[])[thirty, sixty, repair])
        {
            var (exit, said, _) = await ShimAsync(launch);
            exit.Should().Be(RemoteAsk.Ok, said);
        }

        var posted = Posted();
        posted.Should().HaveCount(3, "each of the three MUST LAUNCH — the plan's own words");
        Number(posted[0], "remainingSeconds").Should().BeInRange(15, 20, "a fresh 30 s reviewer: its shim advertises at most 20");
        Number(posted[1], "remainingSeconds").Should().BeInRange(45, 50, "a fresh 60 s reviewer: at most 50");
        Number(posted[2], "remainingSeconds").Should().BeInRange(25, 30, "a repair with 40 s left: at most 30");
        posted.Select(p => Text(p, "attempt")).Should().Equal([LaunchAttempts.First, LaunchAttempts.First, LaunchAttempts.Repair]);
    }

    // ---------- an exhausted budget is never submitted ----------

    [Fact]
    public async Task AnExhaustedBudget_IsNeverSubmitted_AndNeverSerialisedAsNought()
    {
        _server.Answers = Accepting;
        var built = Built(TimeSpan.FromSeconds(600));
        // Only by hand can the shim be given no deadline at all: the adapter floors a derived one at five seconds.
        var spent = built with { Request = built.Request with { Arguments = RemoteRuntime.WithFlag(built.Request.Arguments, "--timeout-seconds", "0") } };

        var (exit, said, _) = await ShimAsync(spent);

        exit.Should().Be(RemoteAsk.TooSlow, said);
        said.Should().Contain("spent before its review could be submitted");
        _server.Requests.Should().BeEmpty("nothing was sent: a 0 the server dropped would quietly switch its refusal off");
    }

    [Fact]
    public void WithFlag_ReplacesAValueInPlace_OrAppendsThePair()
    {
        RemoteRuntime.WithFlag(["--a", "1", "--b", "2"], "--a", "9").Should().Equal(["--a", "9", "--b", "2"]);
        RemoteRuntime.WithFlag(["--a", "1"], "--c", "3").Should().Equal(["--a", "1", "--c", "3"]);
        RemoteRuntime.WithFlag(["--a"], "--a", "9").Should().Equal(["--a", "--a", "9"], "a trailing flag with no value is left as it was, and the pair appended");
    }

    /// <summary>A launcher that keeps every request and answers each from its own step; the last step answers the rest.</summary>
    private sealed class Recording(params Func<ProcessRequest, ProcessResult>[] steps) : IProcessLauncher
    {
        private readonly List<ProcessRequest> _requests = [];

        public IReadOnlyList<ProcessRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            int at;
            lock (_requests)
            {
                _requests.Add(request);
                at = Math.Min(_requests.Count, steps.Length) - 1;
            }

            return Task.FromResult(steps[at](request));
        }
    }

    /// <summary>A remote shim that exited 0 having written <paramref name="answer"/> where the adapter said to (<c>--out</c>).</summary>
    private static ProcessResult Answering(ProcessRequest request, string answer)
    {
        File.WriteAllText(Flag(request, "--out"), answer);

        return new ProcessResult(0, """{"tokensIn":1,"tokensOut":2}""", string.Empty, TimedOut: false);
    }
}
