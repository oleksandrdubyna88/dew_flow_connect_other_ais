using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// The budget rule against the REAL request — story 4.3 of <c>todo/PLAN_every_round_is_counted.md</c>, the wire half:
/// the deadline is stamped from this server's clock alone, and every launch a healthy client makes still launches.
/// </summary>
/// <remarks>
/// <para><b>MUST LAUNCH</b> (risk amendment 1): a fresh 30 s reviewer, a fresh 60 s reviewer and a repair with 40 s left
/// each submit through the real client path — <see cref="RemoteRuntime"/> builds the launch, <c>ForLaunch</c> shapes it,
/// the real <c>--ask-remote</c> code (<c>AskRemote.RunAsync</c> over <c>Program.Flags</c>, in this process) POSTs it to
/// a capturing loopback — and the captured body is submitted to the real <c>POST /api/reviews</c> and claimed at once.
/// A hand-built request would miss the shim's own ten seconds, which is the number the rule has to live with.</para>
/// <para>The host's own pump never claims here: no slot is signed in on the host's registry, so the claim in each test is
/// the test's, immediate, on the host's own store.</para>
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class TheCallerCanStillWaitTests
{
    private const string Email = $"dev@{TeamServer.Domain}";

    private const string Vendors = """[{ "id": "codex", "runtime": "codex", "models": ["m"], "slots": ["a"] }]""";

    [Fact]
    public async Task AClientClockTenMinutesOff_GetsTheSameDeadline()
    {
        using var server = Server();
        var policy = server.Services.GetRequiredService<JobStore>().Budget;
        var skewed = DateTimeOffset.UtcNow.AddMinutes(10).ToString("O");

        var before = DateTimeOffset.UtcNow;
        var plain = await Post(server, "\"remainingSeconds\": 50");
        // Every instant a client could plausibly send, ten minutes off — and none of them is read.
        var off = await Post(server,
            $"\"remainingSeconds\": 50, \"submittedAtUtc\": \"{skewed}\", \"clientNowUtc\": \"{skewed}\", \"deadlineUtc\": \"{skewed}\", \"utc\": \"{skewed}\"");
        var after = DateTimeOffset.UtcNow;

        var expected = TimeSpan.FromSeconds(50) - policy.SafetyMargin;
        plain.ClientDeadlineUtc.Should().NotBeNull("a stated budget gets a deadline");
        plain.ClientDeadlineUtc!.Value.Should().BeOnOrAfter(before + expected).And.BeOnOrBefore(after + expected, "receipt on the server's clock, plus the stated seconds, less the margin");
        off.ClientDeadlineUtc!.Value.Should().BeOnOrAfter(before + expected).And.BeOnOrBefore(after + expected, "no client timestamp is ever read");
    }

    [Fact]
    public async Task AFreshThirtySecondReviewer_AFreshSixtySecondReviewer_AndARepairWithFortySecondsLeft_EachLaunch()
    {
        using var capture = ShimCapture.Start();
        using var data = new Scratch();
        var thirty = Built(capture.Origin, data, TimeSpan.FromSeconds(30));
        var sixty = Built(capture.Origin, data, TimeSpan.FromSeconds(60));
        var longer = Built(capture.Origin, data, TimeSpan.FromSeconds(600));
        var repair = longer.Adapter!.ForLaunch(RetryLadder.Lesser(TimeSpan.FromSeconds(40), longer with { Attempt = LaunchAttempts.Repair }));

        foreach (var launch in (ReviewerInvocation[])[thirty.Adapter!.ForLaunch(thirty), sixty.Adapter!.ForLaunch(sixty), repair])
        {
            var said = new List<string>();
            var exit = await CoaiMcp.AskRemote.RunAsync(CoaiMcp.Program.Flags([.. launch.Request.Arguments]), said.Add, TextWriter.Null);
            exit.Should().Be(RemoteAsk.Ok, string.Join("\n", said));
        }

        var posted = capture.Posted;
        posted.Should().HaveCount(3, "each of the three MUST LAUNCH — the plan's own words");
        using var server = Server();
        var jobs = server.Services.GetRequiredService<JobStore>();
        foreach (var (body, what) in posted.Zip(new[] { "a fresh 30 s reviewer", "a fresh 60 s reviewer", "a repair with 40 s left" }))
        {
            var accepted = await Submit(server, body);
            var claim = jobs.TryClaim("codex", "a", DateTimeOffset.UtcNow);

            claim.Refused.Should().BeEmpty($"{what} MUST LAUNCH; it advertised {Advertised(body)} s and was refused");
            claim.Job.Should().NotBeNull($"{what} MUST LAUNCH");
            claim.Job!.Id.Should().Be(accepted.Id);
            claim.Job.RunBudget.Should().BePositive();
            jobs.Finish(JobTransitions.Fail(claim.Job, FailureKind.NotStarted, "the test is done with it", DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public async Task TheConfiguredAllowance_IsWhatTheClaimReads()
    {
        // The numbers come from configuration, not from a constant: an hour's allowance refuses a fifty-second budget.
        using var server = Server(new Dictionary<string, string?> { ["Coai__MinStartSeconds"] = "3600", ["Coai__DeadlineSafetyMarginSeconds"] = "1" });
        var jobs = server.Services.GetRequiredService<JobStore>();
        var job = await Post(server, "\"remainingSeconds\": 50");

        var claim = jobs.TryClaim("codex", "a", DateTimeOffset.UtcNow);

        claim.Job.Should().BeNull();
        var refused = claim.Refused.Should().ContainSingle().Subject;
        refused.Id.Should().Be(job.Id);
        refused.Reason.Should().Contain("at least 3600s");
        var status = await server.ClientFor(Email).GetFromJsonAsync<ReviewStatusDto>($"/api/reviews/{job.Id}");
        status!.Failure.Should().Be(RemoteAsk.NoBudgetFailure, "on the wire by its own name — what the shim reads");
        status.Status.Should().Be("failed");
        status.EndCause.Should().BeEmpty();
    }

    // ---------- the harness ----------

    private static TeamServer Server(IDictionary<string, string?>? overrides = null)
    {
        var server = new TeamServer(overrides);
        File.WriteAllText(Path.Combine(server.DataDir, "vendors.json"), Vendors);

        return server;
    }

    /// <summary>An ordinary review body with <paramref name="members"/> added; the job as the host's store holds it.</summary>
    private static async Task<JobRecord> Post(TeamServer server, string members)
    {
        var accepted = await Submit(server, """{ "vendor": "codex", "model": "m", "prompt": "review this", "role": "Architecture", "timeoutSeconds": 60, """ + members + " }");

        return server.Services.GetRequiredService<JobStore>().Find(accepted.Id, Email)!;
    }

    /// <summary>The body as it is, to the real endpoint.</summary>
    private static async Task<ReviewAcceptedDto> Submit(TeamServer server, string body)
    {
        var response = await server.ClientFor(Email).PostAsync("/api/reviews", new StringContent(body, Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<ReviewAcceptedDto>())!;
    }

    /// <summary>A launch built by the real adapter for the capturing server, signed in, with the given reviewer timeout.</summary>
    private static ReviewerInvocation Built(string origin, Scratch data, TimeSpan timeout)
    {
        var runtime = new RemoteRuntime("srv-codex", origin, "codex");
        TeamServerAuth.WriteToken(TeamServerAuth.TokenPath(data.Dir, runtime.ServerUrl), "a-token");
        var output = Directory.CreateDirectory(Path.Combine(data.Dir, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

        return runtime.Build("Architecture", "review this", output, Path.Combine(output, "schema.json"), output,
            new ReviewerSettings("srv-codex") { Model = "m", DataDir = data.Dir, Timeout = timeout });
    }

    private static int Advertised(string body) =>
        JsonDocument.Parse(body).RootElement.TryGetProperty("remainingSeconds", out var value) ? value.GetInt32() : -1;

    /// <summary>A data directory for the shim's token and its launch files, gone with the test.</summary>
    private sealed class Scratch : IDisposable
    {
        public string Dir { get; } = Directory.CreateTempSubdirectory("coai-still-wait-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temp directory something still holds is the OS's to clean.
            }
        }
    }
}
