using System.Runtime.CompilerServices;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What <see cref="ApiEndpointStub.Dispose"/> does with a handler that is still running — case C16 of
/// <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md</c> §3.6.
/// </summary>
/// <remarks>
/// <para><b>The failure.</b> Linux CI, four runs (2026-10-19): <c>AUsageTheVendorNeverReportedIsUnknownTests</c> threw a
/// <see cref="NullReferenceException"/> out of the TEST's <c>Dispose</c> — <c>NameValueCollection.GetKey ←
/// HttpListenerResponse.FormatHeaders ← … ← HttpListener.Stop ← ApiEndpointStub.Dispose</c>. The test sets the hold that wakes the
/// handler and disposes the stub in one breath; the handler starts writing its status and headers on the pool while
/// <c>HttpListener.Stop()</c> enumerates the same header collection on the test thread. Two threads, one collection, and the stub
/// tracked nothing, so it could not wait for the one and keep the other off.</para>
/// <para><b>The contract these pin.</b> Dispose is <i>close admission, then abort every in-flight response and cancel the handlers'
/// <see cref="ApiEndpointStub.Disposing"/> token — both synchronously, before any wait — then drain the tracked handler with a bound,
/// then stop the listener</i>. The listener is never stopped while a handler is running; a handler that ignores both its token and
/// its aborted response costs the bound once and is named in the journal; a late callback can no longer touch the response.</para>
/// <para><b>Why the race itself has no test here.</b> It is a collision of two threads inside the framework's header collection and
/// shows only on the managed (Linux) listener; the arms below pin the ORDER that makes the collision impossible, which is
/// deterministic on every OS. The race is reproduced by the plan's §6 loop, not by an arm.</para>
/// </remarks>
public sealed class ApiEndpointStubTests
{
    /// <summary>Names a hang only — no assertion depends on it elapsing or not elapsing.</summary>
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

    /// <summary>What the handlers answer; the marker is what a test looks for on the client's side, because a status is not evidence (see <see cref="PostAsync"/>).</summary>
    private const string LateAnswer = "LATE-ANSWER-MARKER";

    private static readonly ApiEndpointStub.Answer Ok = new(200, ApiEndpointStub.Completion(LateAnswer));

    /// <summary>
    /// A handler released by its gate and then busy for 300 ms before it returns — a REAL sleep inside the handler, the only one in
    /// E5 that stays, because it stands for the writer's own progress and is the subject. Dispose lands in that window.
    /// </summary>
    [Fact]
    public async Task AStubDisposedWhileItsHandlerWrites_WaitsForTheHandler_AndDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = ApiEndpointStub.Start();
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        var finished = new StrongBox<bool>();
        var listeningAtTheEnd = new StrongBox<bool>();
        stub.Answers = _ =>
        {
            entered.Set();
            gate.Wait(Guard);
            Thread.Sleep(300);
            finished.Value = true;
            listeningAtTheEnd.Value = stub.IsListening;

            return Ok;
        };
        var post = PostAsync(stub, ct);
        entered.Wait(Guard, ct).Should().BeTrue("the request must reach the handler before the window can be opened");
        gate.Set();

        var thrown = Record.Exception(stub.Dispose);

        finished.Value.Should().BeTrue("Dispose waits for the handler it still tracks, so the listener is not stopped under a writer");
        thrown.Should().BeNull("a stub disposed under a running handler is an ordinary teardown, not a crash in the test's Dispose");
        listeningAtTheEnd.Value.Should().BeTrue("the listener is stopped only AFTER the handler finished — never while a header is being written");
        stub.Journal().Should().MatchRegex(@"POST /v1/chat/completions -> \w*Exception", "the handler returned into a response Dispose had aborted, and its write failed in the open");
        (await post).Should().NotContain(LateAnswer, "the response was aborted before the handler could return into it, so none of its answer reaches the client");
    }

    /// <summary>
    /// A handler that ignores both its token and its aborted response: Dispose gives it the bound, says so in the journal, stops, and
    /// the late callback — released only after Dispose returned — fails on the aborted response and is written down.
    /// </summary>
    [Fact]
    public async Task AHandlerStillActivePastTheBound_IsAbortedBeforeTheListenerStops_AndDisposeDoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = ApiEndpointStub.Start();
        using var entered = new ManualResetEventSlim();
        using var gate = new ManualResetEventSlim();
        stub.Answers = _ =>
        {
            entered.Set();
            gate.Wait(Guard);

            return Ok;
        };
        var post = PostAsync(stub, ct);
        entered.Wait(Guard, ct).Should().BeTrue("the request must reach the handler before it can outlive anything");

        var thrown = Record.Exception(stub.Dispose);

        stub.Journal().Should().Contain("outlived", "the bound names the handler it gave up on");
        stub.Journal().Should().Contain("POST /v1/chat/completions", "and says WHICH request it was");
        thrown.Should().BeNull("giving up on a stuck handler is not a failure of the teardown");
        gate.Set();
        await stub.HandlersFinished();
        stub.Journal().Should().NotContain("POST /v1/chat/completions -> 200", "a late answer reaches an aborted response and fails there");
        stub.Journal().Should().MatchRegex(@"POST /v1/chat/completions -> \w*Exception", "and the failure is written down, not swallowed");
        (await post).Should().NotContain(LateAnswer, "nothing of the late answer reaches the client");
    }

    /// <summary>
    /// A cancellation-aware handler is released by the cancel alone: Dispose does not spend the bound on it. The claim is carried by
    /// events, not a stopwatch — the handler has returned by the time Dispose does, and no sentence says it outlived anything.
    /// </summary>
    [Fact]
    public async Task AHandlerThatWaitsOnTheDisposingToken_IsReleasedByDispose_WithoutCostingTheBound()
    {
        var ct = TestContext.Current.CancellationToken;
        var stub = ApiEndpointStub.Start();
        using var entered = new ManualResetEventSlim();
        var released = new StrongBox<bool>();
        stub.CancellableAnswers = (_, token) =>
        {
            entered.Set();
            released.Value = token.WaitHandle.WaitOne(Guard);

            return Ok;
        };
        var post = PostAsync(stub, ct);
        entered.Wait(Guard, ct).Should().BeTrue("the request must reach the handler before Dispose can cancel it");

        var thrown = Record.Exception(stub.Dispose);

        released.Value.Should().BeTrue("Dispose cancels the Disposing token synchronously, before any wait, so a handler blocked on it is freed at once");
        stub.Journal().Should().NotContain("outlived", "a handler that honoured its token never reaches the bound");
        thrown.Should().BeNull();
        stub.Journal().Should().MatchRegex(@"POST /v1/chat/completions -> \w*Exception", "the released handler returned into an aborted response and its write failed in the open");
        (await post).Should().NotContain(LateAnswer, "none of the answer of a handler released by Dispose reaches the client");
    }

    /// <summary>The fifty callers dispose a stub that served nothing, or once too often; neither is an error.</summary>
    [Fact]
    public void ADisposedStub_IsQuietWhenNothingIsRunning_AndWhenDisposedAgain()
    {
        var stub = ApiEndpointStub.Start();

        var first = Record.Exception(stub.Dispose);
        var second = Record.Exception(stub.Dispose);

        first.Should().BeNull();
        second.Should().BeNull("Dispose is idempotent");
        stub.Disposing.IsCancellationRequested.Should().BeTrue("a disposed stub says so on its token");
    }

    /// <summary>
    /// One POST to the stub; what the client RECEIVED — its status and body as text, or the exception that cut it off.
    /// </summary>
    /// <remarks>
    /// The status alone proves nothing about an aborted response. Windows (http.sys) resets the connection on an Abort, so the client sees an exception. Linux's managed listener, asked to Abort a response whose head is not sent yet, answers the client first — observed on the CI runner (scratch run 39822665330): status 200, chunked, an empty body — and then drops the connection. So a client can legitimately see a 200 after an abort, and what a teardown must guarantee on both is that the answer the late handler tried to write never arrives. The tests therefore look for the answer's marker in the body (and, for what Dispose did to the handler, in the stub's journal) and never at the status alone.
    /// </remarks>
    private static Task<string> PostAsync(ApiEndpointStub stub, CancellationToken ct) => Task.Run(
        async () =>
        {
            try
            {
                using var client = new HttpClient();
                using var response = await client.PostAsync(stub.Endpoint + "/chat/completions", new StringContent("{}"), ct);

                return ((int)response.StatusCode).ToString() + ": " + await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
            {
                return e.GetType().Name;
            }
        },
        ct);
}
