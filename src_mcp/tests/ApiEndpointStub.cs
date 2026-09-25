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
    internal sealed record Seen(string Method, string Path, string Authorization, string Body);

    internal sealed record Answer(int Status, string Body, IReadOnlyDictionary<string, string>? Headers = null);

    private readonly HttpListener _server;
    private readonly List<Seen> _seen = [];
    private readonly List<string> _journal = [];

    /// <summary>What the stub answers, set per test. The default is a review-shaped 200.</summary>
    public Func<Seen, Answer> Answers { get; set; } = _ => new Answer(200, Completion("{\"findings\":[]}"));

    /// <summary>The OpenAI-compatible base this stub answers on — <c>http://127.0.0.1:port/v1</c>.</summary>
    public string Endpoint { get; }

    private ApiEndpointStub(HttpListener server, string prefix)
    {
        _server = server;
        Endpoint = prefix.TrimEnd('/') + "/v1";
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

            await AnsweredAsync(context);
        }
    }

    private async Task AnsweredAsync(HttpListenerContext context)
    {
        string body;
        using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync();
        }

        var seen = new Seen(
            context.Request.HttpMethod,
            context.Request.Url!.AbsolutePath,
            context.Request.Headers["Authorization"] ?? string.Empty,
            body);
        lock (_seen)
        {
            _seen.Add(seen);
        }

        try
        {
            var answer = Answers(seen);
            var bytes = Encoding.UTF8.GetBytes(answer.Body);
            context.Response.StatusCode = answer.Status;
            context.Response.ContentType = "application/json";
            foreach (var (name, value) in answer.Headers ?? new Dictionary<string, string>())
            {
                context.Response.Headers[name] = value;
            }

            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
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

    private void Noted(string line)
    {
        lock (_journal)
        {
            _journal.Add(line);
        }
    }

    public void Dispose()
    {
        try
        {
            _server.Stop();
            _server.Close();
        }
        catch (Exception e) when (e is ObjectDisposedException or HttpListenerException)
        {
        }
    }
}
