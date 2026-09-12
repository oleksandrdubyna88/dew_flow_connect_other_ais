using CoaiMcp.Core.Rounds;
using System.Net;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A review this machine abandons must stop costing the company money.
/// </summary>
/// <remarks>
/// The code round's blocking finding was that the mechanism existed and nothing called it: the shim
/// wrote a claim file, `CancelAbandonedAsync` could read it, and no path between the two was ever
/// taken — so a killed reviewer ran to completion on the team's subscription for an answer nobody
/// would collect. These tests are that path.
/// </remarks>
public sealed class RemoteCancellationTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "coai-cancel-" + Guid.NewGuid().ToString("N"));

    private string JobFile => Path.Combine(_dir, "a.job");

    private string TokenFile => Path.Combine(_dir, "a.token");

    public RemoteCancellationTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(TokenFile, "the-token");
        RemoteRuntime.Claim(JobFile, "https://coai.example.com/", "job-42", TokenFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public async Task ACancellationTellsTheRightServerAboutTheRightJob()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, "");

        var cancelled = await RemoteRuntime.CancelAbandonedAsync(JobFile, new HttpClient(handler));

        cancelled.Should().BeTrue();
        var request = handler.Seen[0];
        request.Method.Should().Be(HttpMethod.Delete);
        request.RequestUri!.ToString().Should().Be("https://coai.example.com/api/reviews/job-42");
        request.Headers.GetValues("Authorization").Should().ContainSingle().Which.Should().Be("Bearer the-token");
    }

    [Fact]
    public async Task ACancellationCarriesTheContractHeader()
    {
        // The server judges this BEFORE the token and answers 426 without it, so a cancellation that
        // omitted it was refused by every server it was ever sent to — the mechanism would have
        // looked wired and worked on nothing. Caught on the code round.
        var handler = new StubHandler(HttpStatusCode.NoContent, "");

        await RemoteRuntime.CancelAbandonedAsync(JobFile, new HttpClient(handler));

        handler.Seen[0].Headers.GetValues(RemoteAsk.ContractHeader).Should().ContainSingle()
            .Which.Should().Be(RemoteAsk.ContractVersion.ToString());
    }

    [Fact]
    public async Task ASuccessfulCancellationForgetsTheClaim()
    {
        await RemoteRuntime.CancelAbandonedAsync(JobFile, new HttpClient(new StubHandler(HttpStatusCode.NoContent, "")));

        File.Exists(JobFile).Should().BeFalse("the job is stopped; there is nothing left to cancel");
    }

    [Fact]
    public async Task AJobTheServerHasNeverHeardOfIsAlsoForgotten()
    {
        // 404 is terminal in the other direction: there is no such job and there never will be.
        await RemoteRuntime.CancelAbandonedAsync(JobFile, new HttpClient(new StubHandler(HttpStatusCode.NotFound, "{}")));

        File.Exists(JobFile).Should().BeFalse();
    }

    [Fact]
    public async Task AFAILEDCancellationKEEPSTheClaim()
    {
        // The claim used to be deleted in a `finally`, so a laptop that was briefly offline at exactly
        // the wrong moment lost the job id for ever and the review ran to completion anyway — the one
        // outcome the whole mechanism exists to prevent. Raised on the code round.
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var cancelled = await RemoteRuntime.CancelAbandonedAsync(JobFile, new HttpClient(handler));

        cancelled.Should().BeFalse();
        File.Exists(JobFile).Should().BeTrue("a later attempt is the only thing that can still stop it");
    }

    [Fact]
    public async Task BeingSignedOutKeepsTheClaimToo()
    {
        // Signing in again is what makes this cancellable; deleting the claim now would throw away
        // the only way to do it.
        File.Delete(TokenFile);
        var handler = new StubHandler(HttpStatusCode.NoContent, "");

        await RemoteRuntime.CancelAbandonedAsync(JobFile, new HttpClient(handler));

        handler.Requests.Should().Be(0);
        File.Exists(JobFile).Should().BeTrue();
    }

    [Fact]
    public async Task NoClaimMeansNoRequest()
    {
        var handler = new StubHandler(HttpStatusCode.NoContent, "");

        var cancelled = await RemoteRuntime.CancelAbandonedAsync(
            Path.Combine(_dir, "nothing-here.job"), new HttpClient(handler));

        cancelled.Should().BeFalse();
        handler.Requests.Should().Be(0);
    }

    [Fact]
    public async Task AnInvocationWithNoJobFileAsksNobodyAnything()
    {
        // Every reviewer launch reaches the abandon hook, including the ones that never claimed
        // anything — a remote review refused before the server accepted it, for instance.
        var invocation = new ReviewerInvocation(
            "codex", RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], "."),
            Adapter: new RemoteRuntime("codex", "https://s"));

        var abandon = async () => await invocation.Adapter!.AbandonAsync(invocation);

        await abandon.Should().NotThrowAsync();
    }

    [Fact]
    public void UsageThatCannotBeReadIsZeroRatherThanACrash()
    {
        // The shim prints its usage line on stdout; a shim that died before printing leaves whatever
        // it managed to write. The round must still get its answer.
        var invocation = new ReviewerInvocation(
            "codex", RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], "."));

        var usage = new RemoteRuntime("codex", "https://s").ReadUsage(
            invocation, new ProcessResult(0, "not json at all", "", false));

        usage.TokensIn.Should().Be(0);
        usage.TokensOut.Should().Be(0);
    }

    [Fact]
    public void UsageIsReadOffTheShimsOwnLine()
    {
        var invocation = new ReviewerInvocation(
            "codex", RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], "."));

        var usage = new RemoteRuntime("codex", "https://s").ReadUsage(
            invocation, new ProcessResult(0, RemoteAsk.UsageLine(31, 41), "", false));

        usage.TokensIn.Should().Be(31);
        usage.TokensOut.Should().Be(41);
    }

    [Fact]
    public async Task AnAdapterWithNothingToCleanUpDoesNothing()
    {
        // The default on the interface, which is what keeps every CLI adapter unchanged.
        var invocation = new ReviewerInvocation(
            "codex", RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], "."), Adapter: new CodexRuntime());

        var abandon = async () => await invocation.Adapter!.AbandonAsync(invocation);

        await abandon.Should().NotThrowAsync();
    }
}

/// <summary>
/// The executor is the ONE place every reviewer launch passes through, so it is the one place the
/// clean-up can be wired without every adapter having to remember to.
/// </summary>
public sealed class ExecutorAbandonsRemoteWorkTests
{
    /// <summary>Records that it was asked to clean up, and what for.</summary>
    private sealed class SpyRuntime : IReviewerRuntime
    {
        public int Abandoned { get; private set; }

        public string Provider => "spy";

        public string DefaultExecutable => "spy";

        public ReviewerInvocation Build(
            string role, string prompt, string worktree, string schema, string outputDir, ReviewerSettings settings) =>
            throw new NotSupportedException();

        public Task AbandonAsync(ReviewerInvocation invocation, CancellationToken ct = default)
        {
            Abandoned++;

            return Task.CompletedTask;
        }
    }

    private sealed class Launcher(Func<ProcessRequest, CancellationToken, ProcessResult> answer) : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            Task.FromResult(answer(request, ct));
    }

    private static ReviewerInvocation InvocationFor(SpyRuntime spy) =>
        new("spy", RoleCatalog.ArchitectureRole, new ProcessRequest("spy", [], "."),
            Adapter: spy, JobFile: "a.job");

    [Fact]
    public async Task AReviewerKilledOnItsTIMEOUTHasItsRemoteWorkCancelled()
    {
        var spy = new SpyRuntime();
        var executor = new ReviewerExecutor(
            new Launcher((_, _) => new ProcessResult(-1, "", "", TimedOut: true)));

        var launch = await executor.LaunchAsync(InvocationFor(spy), CancellationToken.None);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.TimedOut>();
        spy.Abandoned.Should().Be(1, "the tree was killed, so the shim never reached its own DELETE");
    }

    [Fact]
    public async Task AReviewerKilledByCANCELLATIONHasItsRemoteWorkCancelledToo()
    {
        var spy = new SpyRuntime();
        var executor = new ReviewerExecutor(
            new Launcher((_, ct) => throw new OperationCanceledException(ct)));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var launch = async () => await executor.LaunchAsync(InvocationFor(spy), cancelled.Token);

        // The abandonment is a courtesy on the way past; the cancellation still propagates, because
        // the round above must still learn that this reviewer did not run.
        await launch.Should().ThrowAsync<OperationCanceledException>();
        spy.Abandoned.Should().Be(1);
    }

    [Fact]
    public async Task AReviewerThatFINISHEDIsNotCancelled()
    {
        var spy = new SpyRuntime();
        var executor = new ReviewerExecutor(new Launcher((_, _) => new ProcessResult(0, "{}", "", false)));

        await executor.LaunchAsync(InvocationFor(spy), CancellationToken.None);

        spy.Abandoned.Should().Be(0, "there is nothing to stop — it already stopped");
    }

    [Fact]
    public async Task ACleanupThatFAILSDoesNotReplaceTheReviewersOwnFailure()
    {
        // This runs on a path where something has already gone wrong. An exception here would hide
        // the vendor's real failure behind a cleanup's.
        var executor = new ReviewerExecutor(
            new Launcher((_, _) => new ProcessResult(-1, "", "", TimedOut: true)));
        var invocation = new ReviewerInvocation(
            "throwing", RoleCatalog.ArchitectureRole, new ProcessRequest("x", [], "."),
            Adapter: new ThrowingAdapter(), JobFile: "a.job");

        var launch = await executor.LaunchAsync(invocation, CancellationToken.None);

        launch.Terminal.Should().BeOfType<ReviewerOutcome.TimedOut>();
    }

    private sealed class ThrowingAdapter : IReviewerRuntime
    {
        public string Provider => "throwing";

        public string DefaultExecutable => "x";

        public ReviewerInvocation Build(
            string role, string prompt, string worktree, string schema, string outputDir, ReviewerSettings settings) =>
            throw new NotSupportedException();

        public Task AbandonAsync(ReviewerInvocation invocation, CancellationToken ct = default) =>
            throw new InvalidOperationException("the server was unreachable");
    }
}
