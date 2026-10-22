using System.Net;
using System.Text;

namespace CoaiMcp.Tests;

/// <summary>
/// A stand-in for a hosted OpenAI-compatible endpoint: a real <see cref="HttpListener"/> on a
/// loopback port that records every request — method, path, the <c>Authorization</c> header and the
/// body — and answers whatever the test decides.
/// </summary>
/// <remarks>
/// <para>ONE stub for the three suites that need one (<c>AskApiModeTests</c>, <c>ProbeApiModeTests</c>,
/// <c>ApiShimScenarioTests</c>), on the port-taking rules <see cref="LoopbackStub"/> learned the hard
/// way. It records the bearer VERBATIM on purpose: the tests' whole question is whether that string
/// ever reaches stdout, stderr or argv, and a stub that could not see it could not ask.</para>
/// <para>The serving loop never ends on a request: an answer that throws is written down and the loop
/// goes on, the lesson of issue #462.</para>
/// </remarks>
internal sealed class ApiEndpointStub : IDisposable
{
    /// <param name="Headers">Every request header, case-insensitively — what a cache-routing header test reads.</param>
    internal sealed record Seen(string Method, string Path, string Authorization, string Body, IReadOnlyDictionary<string, string>? Headers = null)
    {
        /// <summary>The request header by name, or empty.</summary>
        public string Header(string name) => Headers is not null && Headers.TryGetValue(name, out var value) ? value : string.Empty;
    }

    /// <param name="ContentType">The answer's media type — <c>text/event-stream</c> for a streamed one.</param>
    /// <param name="Chunks">When set, the body is sent as these pieces, each flushed on its own, with no length declared —
    /// a stream (research/PLAN_api_streaming.md); <see cref="Body"/> is then unused.</param>
    /// <param name="Drop">After the chunks, abort the connection instead of closing it: a stream cut mid-answer.</param>
    internal sealed record Answer(
        int Status,
        string Body,
        IReadOnlyDictionary<string, string>? Headers = null,
        string ContentType = "application/json",
        IReadOnlyList<byte[]>? Chunks = null,
        bool Drop = false);

    private readonly HttpListener _server;
    private readonly List<Seen> _seen = [];
    private readonly List<string> _journal = [];

    /// <summary>What the stub answers, set per test. The default is a review-shaped 200.</summary>
    public Func<Seen, Answer> Answers { get; set; } = _ => new Answer(200, Completion("{\"findings\":[]}"));

    /// <summary>The OpenAI-compatible base this stub answers on — <c>http://127.0.0.1:port/v1</c>.</summary>
    public string Endpoint { get; }

    /// <summary>
    /// The bare origin — <c>http://127.0.0.1:port</c> — for a stub standing in for something that is not an OpenAI
    /// endpoint: a Team server, whose shim posts to <c>{origin}/api/reviews</c> (<c>ARoundHasAnIdOnTheWireTests</c>).
    /// The stub answers every path alike; only the test's <see cref="Answers"/> reads it.
    /// </summary>
    public string Origin { get; }

    /// <summary>
    /// How long <see cref="Dispose"/> waits for a handler that ignored both its <see cref="Disposing"/> token and its aborted
    /// response. Reached only by such a handler: one that honours either is freed by the synchronous cancel-and-abort before this
    /// wait begins, so a teardown never pays it for a well-behaved handler. Two seconds is longer than any handler here legitimately
    /// needs after being told to stop, and short enough that a stuck one cannot make the suite look hung.
    /// </summary>
    internal static readonly TimeSpan DrainBound = TimeSpan.FromSeconds(2);

    private readonly CancellationTokenSource _disposing = new();
    private readonly object _gate = new();
    private readonly List<Tracked> _tracked = [];
    private bool _closed;

    /// <summary>One request the serving loop took: where it came from, what it was, and when its handler is done.</summary>
    private sealed record Tracked(HttpListenerContext Context, string Method, string Path, TaskCompletionSource Done);

    /// <summary>
    /// Cancelled — synchronously, before any wait — the moment <see cref="Dispose"/> begins. A handler that blocks should block on
    /// this (see <see cref="CancellableAnswers"/>) so that disposing the stub frees it instead of costing the drain bound.
    /// </summary>
    public CancellationToken Disposing => _disposing.Token;

    /// <summary>
    /// <see cref="Answers"/> for a handler that waits on something: it receives <see cref="Disposing"/>. Assigning either property
    /// replaces the other; the fifty callers of <see cref="Answers"/> are unchanged.
    /// </summary>
    public Func<Seen, CancellationToken, Answer> CancellableAnswers
    {
        set => Answers = seen => value(seen, Disposing);
    }

    /// <summary>Whether the listener is still accepting — false once <see cref="Dispose"/> has stopped it, and never before every tracked handler finished.</summary>
    public bool IsListening => _server.IsListening;

    /// <summary>
    /// Completes when every request taken so far has been fully handled — answered, or failed and journaled. The event a test
    /// awaits after releasing a handler Dispose gave up on, instead of polling the journal.
    /// </summary>
    public Task HandlersFinished()
    {
        lock (_gate)
        {
            return Task.WhenAll(_tracked.Select(t => t.Done.Task));
        }
    }

    private ApiEndpointStub(HttpListener server, string prefix)
    {
        _server = server;
        Origin = prefix.TrimEnd('/');
        Endpoint = Origin + "/v1";
        _ = Task.Run(ServeAsync);
    }

    public static ApiEndpointStub Start()
    {
        var (server, prefix) = LoopbackStub.Start();

        return new ApiEndpointStub(server, prefix);
    }

    /// <summary>Every request the stub saw, in order.</summary>
    public IReadOnlyList<Seen> Requests
    {
        get
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }
    }

    /// <summary>A chat-completion answer carrying <paramref name="content"/> and the given usage.</summary>
    public static string Completion(string content, long promptTokens = 11, long completionTokens = 22, long cachedTokens = 0)
    {
        var escaped = content.Replace("\\", "\\\\").Replace("\"", "\\\"");

        return "{\"id\":\"cmpl-1\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"" + escaped + "\"},"
            + "\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":" + promptTokens + ",\"completion_tokens\":" + completionTokens
            + ",\"prompt_tokens_details\":{\"cached_tokens\":" + cachedTokens + "}}}";
    }

    public string Journal()
    {
        lock (_journal)
        {
            return _journal.Count == 0 ? "The stub saw no request." : string.Join(Environment.NewLine, _journal);
        }
    }

    private async Task ServeAsync()
    {
        while (_server.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _server.GetContextAsync();
            }
            catch (Exception e)
            {
                Noted($"accept failed: {e.GetType().Name}");
                if (!_server.IsListening)
                {
                    return;
                }

                await Task.Delay(50);
                continue;
            }

            if (Admitted(context) is { } tracked)
            {
                await HandledAsync(tracked);
            }
            else
            {
                Aborted(context);
            }
        }
    }

    /// <summary>The request, now tracked — or nothing when <see cref="Dispose"/> already closed admission.</summary>
    private Tracked? Admitted(HttpListenerContext context)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return null;
            }

            _tracked.RemoveAll(t => t.Done.Task.IsCompleted);
            var tracked = new Tracked(
                context, context.Request.HttpMethod, context.Request.Url!.AbsolutePath, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            _tracked.Add(tracked);

            return tracked;
        }
    }

    /// <summary>
    /// The answer, with whatever stopped it before the answer's own catch written down — a body read on a connection
    /// <see cref="Dispose"/> aborted is such a failure — and the handler marked done on every path.
    /// </summary>
    private async Task HandledAsync(Tracked tracked)
    {
        try
        {
            await AnsweredAsync(tracked.Context);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Noted($"{tracked.Method} {tracked.Path} -> {e.GetType().Name}");
            Aborted(tracked.Context);
        }
        finally
        {
            tracked.Done.TrySetResult();
        }
    }

    /// <summary>Abort the response — idempotent; a response that cannot even be aborted is journaled, not swallowed.</summary>
    private void Aborted(HttpListenerContext context)
    {
        try
        {
            context.Response.Abort();
        }
        catch (Exception torn) when (torn is not OutOfMemoryException)
        {
            Noted($"abort failed: {torn.GetType().Name}");
        }
    }

    private async Task AnsweredAsync(HttpListenerContext context)
    {
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync();
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in context.Request.Headers.AllKeys)
        {
            if (name is not null)
            {
                headers[name] = context.Request.Headers[name] ?? string.Empty;
            }
        }

        var seen = new Seen(
            context.Request.HttpMethod,
            context.Request.Url!.AbsolutePath,
            context.Request.Headers["Authorization"] ?? string.Empty,
            body,
            headers);
        lock (_seen)
        {
            _seen.Add(seen);
        }

        try
        {
            var answer = Answers(seen);
            context.Response.StatusCode = answer.Status;
            context.Response.ContentType = answer.ContentType;
            foreach (var (name, value) in answer.Headers ?? new Dictionary<string, string>())
            {
                context.Response.Headers[name] = value;
            }

            if (answer.Chunks is { } chunks)
            {
                await StreamedAsync(context, chunks, answer.Drop);
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(answer.Body);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            Noted($"{seen.Method} {seen.Path} -> {answer.Status}");
        }
        catch (Exception e)
        {
            Noted($"{seen.Method} {seen.Path} -> {e.GetType().Name}");
            try
            {
                context.Response.Abort();
            }
            catch (Exception torn) when (torn is not OutOfMemoryException)
            {
            }
        }
    }

    /// <summary>The body in pieces, each flushed, no length declared; closed — or aborted mid-answer when asked.</summary>
    private static async Task StreamedAsync(HttpListenerContext context, IReadOnlyList<byte[]> chunks, bool drop)
    {
        context.Response.SendChunked = true;
        foreach (var chunk in chunks)
        {
            await context.Response.OutputStream.WriteAsync(chunk);
            await context.Response.OutputStream.FlushAsync();
        }

        if (drop)
        {
            context.Response.Abort();
        }
        else
        {
            context.Response.Close();
        }
    }

    private void Noted(string line)
    {
        lock (_journal)
        {
            _journal.Add(line);
        }
    }

    /// <summary>
    /// Close admission, then abort every in-flight response and cancel <see cref="Disposing"/> — both synchronously, before any
    /// wait — then drain the tracked handlers for at most <see cref="DrainBound"/>, and only then stop the listener. Never a wait
    /// before the cancel, and never a stop while a handler may still be writing a header: that order is what the
    /// <c>NullReferenceException</c> out of <c>HttpListener.Stop</c> on Linux CI (C16) was missing.
    /// </summary>
    public void Dispose()
    {
        Tracked[] live;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            live = [.. _tracked.Where(t => !t.Done.Task.IsCompleted)];
        }

        foreach (var tracked in live)
        {
            Aborted(tracked.Context);
        }

        _disposing.Cancel();
        Drained(live);
        Stopped();
    }

    /// <summary>Wait for the handlers Dispose found running; name the ones that outlived the bound.</summary>
    private void Drained(Tracked[] live)
    {
        if (Task.WhenAll(live.Select(t => t.Done.Task)).Wait(DrainBound))
        {
            return;
        }

        foreach (var tracked in live.Where(t => !t.Done.Task.IsCompleted))
        {
            Noted($"dispose: a handler outlived the {DrainBound.TotalSeconds:0} s bound and was aborted: {tracked.Method} {tracked.Path}");
        }
    }

    private void Stopped()
    {
        try
        {
            _server.Stop();
            _server.Close();
        }
        catch (Exception e) when (e is ObjectDisposedException or HttpListenerException)
        {
            Noted($"dispose: the listener was already gone ({e.GetType().Name})");
        }
    }
}
