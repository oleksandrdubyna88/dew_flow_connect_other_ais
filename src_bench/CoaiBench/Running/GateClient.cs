using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiBench.Running;

/// <summary>
/// One `coai-mcp` process, spoken to the way a client speaks to it.
/// </summary>
/// <remarks>
/// <para>Over stdio against the built binary, not by referencing <c>PanelService</c>. Two reasons,
/// and the first is the one that keeps being needed: <b>five windows is five processes</b>, and the
/// failures worth measuring here — a shared data directory, a lock held across processes, one GPU
/// behind them all — do not exist inside a single one. The second is that this exercises the
/// protocol a person actually runs, including its serialisation.</para>
/// <para>Newline-delimited JSON-RPC, which is what the server writes on this transport. Its stderr
/// is kept whole: when a round produces nothing at all, that is the only thing left to read.</para>
/// </remarks>
public sealed class GateClient : IAsyncDisposable
{
    private readonly Process _server;
    private readonly StringBuilder _stderr = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonNode?>> _waiting = [];
    private readonly Lock _gate = new();
    private int _nextId = 1;

    public GateClient(string executable, string dataDir, IReadOnlyDictionary<string, string> env)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["COAI_DATA_DIR"] = dataDir;
        foreach (var (key, value) in env)
        {
            start.Environment[key] = value;
        }

        _server = Process.Start(start) ?? throw new InvalidOperationException($"'{executable}' did not start");
        _server.OutputDataReceived += (_, line) => Receive(line.Data);
        _server.ErrorDataReceived += (_, line) => Remember(line.Data);
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
    }

    /// <summary>The tail of what the server said for itself. Empty when it said nothing.</summary>
    public string ServerSaid
    {
        get
        {
            lock (_gate)
            {
                var lines = _stderr.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);

                return string.Join(" | ", lines.TakeLast(3)).Trim();
            }
        }
    }

    private void Remember(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_gate)
        {
            _stderr.AppendLine(line);
        }
    }

    private void Receive(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || JsonNode.Parse(line) is not JsonObject message)
        {
            return;
        }

        if (message["id"]?.GetValue<int>() is not { } id)
        {
            return; // a notification; nobody is waiting on it
        }

        TaskCompletionSource<JsonNode?>? waiting;
        lock (_gate)
        {
            _waiting.Remove(id, out waiting);
        }

        waiting?.TrySetResult(message["result"]);
    }

    /// <summary>The handshake, which every tool call is refused without.</summary>
    public async Task HandshakeAsync(CancellationToken ct)
    {
        await RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2024-11-05",
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "coai-bench", ["version"] = "1" },
        }, ct);
        Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/initialized" });
    }

    /// <summary>One tool call, with how long it took — which is half of what a bench is for.</summary>
    public async Task<(JsonNode? Answer, double Seconds)> CallAsync(
        string tool, JsonObject arguments, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var result = await RequestAsync(
            "tools/call", new JsonObject { ["name"] = tool, ["arguments"] = arguments }, ct);
        var text = result?["content"]?[0]?["text"]?.GetValue<string>() ?? string.Empty;

        return (Parsed(text), Math.Round(clock.Elapsed.TotalSeconds, 1));
    }

    /// <summary>A tool that answered with something other than JSON has still answered.</summary>
    private static JsonNode? Parsed(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return new JsonObject { ["error"] = text.Length == 0 ? "the tool returned nothing" : text };
        }
    }

    private Task<JsonNode?> RequestAsync(string method, JsonObject parameters, CancellationToken ct)
    {
        var waiting = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int id;
        lock (_gate)
        {
            id = _nextId++;
            _waiting[id] = waiting;
        }

        Write(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters,
        });
        ct.Register(() => waiting.TrySetCanceled(ct));

        return waiting.Task;
    }

    private void Write(JsonObject message)
    {
        lock (_gate)
        {
            _server.StandardInput.WriteLine(message.ToJsonString());
            _server.StandardInput.Flush();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _server.Kill(entireProcessTree: true);
            await _server.WaitForExitAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // Already gone, which is the state we were asking for.
        }

        _server.Dispose();
    }
}
