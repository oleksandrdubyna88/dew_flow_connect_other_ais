using System.Text.Json.Nodes;
using System.Threading.Channels;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CoaiMcp.Tests;

/// <summary>
/// The real MCP SDK server over an in-memory pipe the test holds both ends of: it writes the client's messages and reads what the server
/// SENT, so "what does the SDK answer for a cancelled request" is observed on the wire instead of assumed. No process, no stdio.
/// </summary>
/// <remarks>
/// <paramref name="wrap"/> is how a test puts the production transport decorators (<see cref="CoaiMcp.Server.InputEndSignallingTransport"/>,
/// the cancelled-response filter) between the pipe and the SDK, in the order <c>Program.ServeAsync</c> composes them.
/// </remarks>
internal sealed class InMemoryMcpServer : IAsyncDisposable
{
    /// <summary>The server's end of the pipe: what the test writes is what the SDK reads, what the SDK sends is recorded.</summary>
    private sealed class Pipe : ITransport
    {
        private readonly Channel<JsonRpcMessage> _input = Channel.CreateUnbounded<JsonRpcMessage>();
        private readonly List<JsonRpcMessage> _sent = [];
        private readonly Lock _gate = new();

        public string? SessionId => null;

        public ChannelReader<JsonRpcMessage> MessageReader => _input.Reader;

        public Task Write(JsonRpcMessage message) => _input.Writer.WriteAsync(message).AsTask();

        public void End() => _input.Writer.TryComplete();

        public List<JsonRpcMessage> Sent
        {
            get
            {
                lock (_gate)
                {
                    return [.. _sent];
                }
            }
        }

        public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _sent.Add(message);
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            _input.Writer.TryComplete();

            return ValueTask.CompletedTask;
        }
    }

    private readonly Pipe _pipe = new();
    private readonly CancellationTokenSource _stop = new();
    private McpServer? _server;
    private Task _running = Task.CompletedTask;

    private InMemoryMcpServer() { }

    /// <summary>Starts the SDK server with <paramref name="tools"/> and completes the handshake.</summary>
    public static async Task<InMemoryMcpServer> StartAsync(IEnumerable<McpServerTool> tools, Func<ITransport, ITransport>? wrap = null)
    {
        var memory = new InMemoryMcpServer();
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "in-memory", Version = "0" },
            ToolCollection = [.. tools],
        };
        memory._server = McpServer.Create(wrap is null ? memory._pipe : wrap(memory._pipe), options);
        memory._running = memory._server.RunAsync(memory._stop.Token);
        await memory.Request(0, "initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-06-18",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "test", ["version"] = "0" },
        });
        await memory.ReplyFor(0, TimeSpan.FromSeconds(30));
        await memory.Notify("notifications/initialized", null);

        return memory;
    }

    public Task Request(long id, string method, JsonNode? parameters) =>
        _pipe.Write(new JsonRpcRequest { Id = new RequestId(id), Method = method, Params = parameters });

    public Task Notify(string method, JsonNode? parameters) =>
        _pipe.Write(new JsonRpcNotification { Method = method, Params = parameters });

    /// <summary>A <c>tools/call</c> for <paramref name="tool"/> with <paramref name="arguments"/>.</summary>
    public Task Call(long id, string tool, JsonObject arguments) =>
        Request(id, "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments });

    /// <summary>The client takes request <paramref name="id"/> back — the notification the MCP spec defines.</summary>
    public Task Cancel(long id) =>
        Notify("notifications/cancelled", new JsonObject { ["requestId"] = id, ["reason"] = "the client took it back" });

    /// <summary>Everything the SDK has sent so far.</summary>
    public List<JsonRpcMessage> Sent => _pipe.Sent;

    /// <summary>The response or error the server sent for <paramref name="id"/>, or null when none arrived within <paramref name="within"/>.</summary>
    public async Task<JsonRpcMessage?> ReplyFor(long id, TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        do
        {
            var reply = Sent.FirstOrDefault(m => m is JsonRpcResponse r && r.Id == new RequestId(id)
                || m is JsonRpcError e && e.Id == new RequestId(id));
            if (reply is not null)
            {
                return reply;
            }

            await Task.Delay(25);
        }
        while (DateTime.UtcNow < until);

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _pipe.End();
        if (_server is not null)
        {
            await _server.DisposeAsync();
        }

        try
        {
            await _running;
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            // The server ended under the test: nothing is left to observe.
        }

        _stop.Dispose();
    }
}
