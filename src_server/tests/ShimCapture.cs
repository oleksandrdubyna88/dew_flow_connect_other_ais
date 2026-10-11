using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CoaiServer.Tests;

/// <summary>
/// A loopback stand-in for a Team server that keeps every body the real <c>--ask-remote</c> shim POSTs to it and answers
/// enough for the shim to finish cleanly: 202 to a submit, a <c>done</c> status to a poll, 204 to a cancel.
/// </summary>
/// <remarks>
/// <para>What it is for: the budget rule (story 4.3 of <c>todo/PLAN_every_round_is_counted.md</c>) is judged against
/// the POST the real shim builds — the shim's own ten-second margin and its five-second floor are in that body and in
/// nothing a test writes by hand — and the body is then replayed to the REAL in-process server, which has no socket for
/// the shim's <see cref="HttpClient"/> to reach. Capture here, replay there.</para>
/// <para>The port is taken as <c>CoaiMcp.Tests</c>' <c>LoopbackStub</c> learned to take it: a free port is asked of
/// the OS and may be gone by the time the listener binds it, so a refused bind is retried on a fresh number a few times
/// before it is a failure. A smaller copy of that helper rather than a reference to another test assembly.</para>
/// </remarks>
internal sealed class ShimCapture : IDisposable
{
    private const int Attempts = 10;

    private readonly HttpListener _listener;
    private readonly List<string> _posted = [];

    /// <summary>The origin the shim is pointed at — <c>http://127.0.0.1:port</c>.</summary>
    public string Origin { get; }

    private ShimCapture(HttpListener listener, string origin)
    {
        _listener = listener;
        Origin = origin;
        _ = Task.Run(ServeAsync);
    }

    public static ShimCapture Start()
    {
        for (var attempt = 1; ; attempt++)
        {
            var port = FreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            try
            {
                listener.Start();

                return new ShimCapture(listener, $"http://127.0.0.1:{port}");
            }
            catch (HttpListenerException) when (attempt < Attempts)
            {
                // Somebody took the number between the probe and the bind; the next probe answers another.
            }
        }
    }

    /// <summary>Every body the shim POSTed, in order.</summary>
    public IReadOnlyList<string> Posted
    {
        get
        {
            lock (_posted)
            {
                return [.. _posted];
            }
        }
    }

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();

        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            await AnswerAsync(context);
        }
    }

    private async Task AnswerAsync(HttpListenerContext context)
    {
        var (status, body) = context.Request.HttpMethod switch
        {
            "POST" => (202, """{"id":"job-1","position":0}"""),
            "DELETE" => (204, string.Empty),
            _ => (200, """{"id":"job-1","status":"done","position":0,"answer":"{\"findings\":[]}","tokensIn":1,"tokensOut":2}"""),
        };
        if (context.Request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var posted = await reader.ReadToEndAsync();
            lock (_posted)
            {
                _posted.Add(posted);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    public void Dispose()
    {
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
            // Already closed.
        }
    }
}
