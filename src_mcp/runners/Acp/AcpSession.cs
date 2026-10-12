using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CoaiMcp.Runners.Acp;

/// <summary>
/// The JSON-RPC conversation over one <see cref="AcpTransport"/>: requests with ids whose results are awaited,
/// and the pump that reads every line the agent writes — answering its reverse requests WHILE a request of ours
/// is still outstanding, and collecting its notifications.
/// </summary>
/// <remarks>
/// <para><b>The pump is one task and it never blocks on us.</b> A <c>session/prompt</c> is awaited for minutes;
/// in that time the agent asks <c>session/request_permission</c> and <c>fs/read_text_file</c> and will not go on
/// until each is answered. So the reader task handles those inline and writes the reply through the transport's
/// serialised writer; our own request's response is matched to its id and completes the awaiting caller.</para>
/// <para><b>Permission is refused without looking at the tool</b> (<see cref="Permission"/>): the option whose
/// kind is <c>reject_once</c>, else any <c>reject_*</c>, else <c>cancelled</c>. A client that chose by tool
/// name would allow the write a vendor renames tomorrow. <b>Reads go to the boundary</b>, and every other request
/// — <c>fs/write_text_file</c>, <c>terminal/*</c>, anything unknown — is answered with a JSON-RPC error: nothing
/// that was not offered in <c>initialize</c> is served.</para>
/// <para><b>The agent's end of output fails every pending request</b> with a protocol reason: a prompt awaited on a
/// stream that has closed would otherwise wait for its deadline to say nothing.</para>
/// </remarks>
internal sealed class AcpSession(AcpTransport transport, IAcpReadBoundary boundary, CancellationToken ct)
{
    /// <summary>The most answer kept — past it the agent is not answering, it is dumping; the turn fails rather than holding it.</summary>
    public const int MaxAnswerChars = 8 * 1024 * 1024;

    /// <summary>
    /// How long lines the agent wrote before exiting may still be read after its exit, before its pending requests are
    /// failed. The process going is the fact; the pipe may lag it by a buffer — or never close at all, when a grandchild
    /// holds it (<see cref="AcpTransport.Exited"/>).
    /// </summary>
    public static readonly TimeSpan ExitGrace = TimeSpan.FromMilliseconds(300);

    private readonly Dictionary<long, TaskCompletionSource<JsonElement>> _pending = [];
    private readonly Lock _gate = new();
    private readonly StringBuilder _answer = new();
    private readonly List<AcpRefusal> _refusals = [];
    private readonly List<AcpToolCall> _toolCalls = [];
    private readonly List<AcpReadRecord> _reads = [];
    private long _nextId = 1;
    private AcpUsage _usage = AcpUsage.Unknown;
    private string _broken = string.Empty;

    /// <summary>The reader, started by <see cref="Pump"/>; awaited by the client on the way out.</summary>
    public Task Reading { get; private set; } = Task.CompletedTask;

    public string Answer => _answer.ToString();

    public AcpUsage Usage => _usage;

    public IReadOnlyList<AcpRefusal> Refusals => _refusals;

    public IReadOnlyList<AcpToolCall> ToolCalls => _toolCalls;

    public IReadOnlyList<AcpReadRecord> Reads => _reads;

    /// <summary>Why the stream is unusable, once it is — empty while it is fine.</summary>
    public string Broken => _broken;

    /// <summary>Starts the reader, and beside it the watch on the process itself — two ways to learn the agent is gone.</summary>
    public void Pump() => Reading = Task.WhenAll(PumpAsync(), WatchExitAsync());

    /// <summary>Sends a request and awaits its result; a JSON-RPC error is <see cref="AcpAgentException"/>, a broken stream <see cref="AcpProtocolException"/>.</summary>
    public async Task<JsonElement> CallAsync(string method, JsonObject parameters)
    {
        long id;
        var answer = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_broken.Length > 0)
            {
                throw new AcpProtocolException(_broken);
            }

            id = _nextId++;
            _pending[id] = answer;
        }

        if (!await transport.WriteLineAsync(AcpMessages.Request(id, method, parameters), ct))
        {
            throw new AcpProtocolException($"the agent stopped listening before `{method}` could be sent");
        }

        return await answer.Task.WaitAsync(ct);
    }

    /// <summary>
    /// Sends a notification, bounded by the transport's grace rather than by the turn's token — it is the
    /// <c>session/cancel</c> of a teardown, sent AFTER that token fired. A closed or busy pipe is the agent's
    /// decision and is not reported.
    /// </summary>
    public async Task NotifyAsync(string method, JsonObject parameters)
    {
        using var grace = new CancellationTokenSource(AcpTransport.GraceWait);
        await transport.WriteLineAsync(AcpMessages.Notification(method, parameters), grace.Token);
    }

    /// <summary>
    /// The option that says no: <c>reject_once</c>, else the first whose kind starts with <c>reject</c>, else none
    /// (the outcome is then <c>cancelled</c>). Pure, so every option list is a row in a test.
    /// </summary>
    public static string Permission(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var offered = options.EnumerateArray().ToArray();
        var once = offered.FirstOrDefault(option => AcpMessages.Text(option, "kind") == "reject_once");
        var any = once.ValueKind == JsonValueKind.Object
            ? once
            : offered.FirstOrDefault(option => AcpMessages.Text(option, "kind").StartsWith("reject", StringComparison.Ordinal));

        return AcpMessages.Text(any, "optionId");
    }

    private async Task PumpAsync()
    {
        try
        {
            while (await transport.ReadLineAsync(ct) is { } line)
            {
                await HandleAsync(AcpIncoming.Parse(line));
            }

            Break(ct.IsCancellationRequested ? "the turn was ended" : "the agent closed its output before answering");
        }
        catch (OperationCanceledException)
        {
            // The turn's token, in a read or in a reply: the caller already knows why; the pending requests learn it here.
            Break("the turn was ended");
        }
    }

    /// <summary>The agent's exit, as the end of the conversation — after the grace for what it wrote on its way out.</summary>
    private async Task WatchExitAsync()
    {
        await transport.Exited;
        try
        {
            await Task.Delay(ExitGrace, ct);
        }
        catch (OperationCanceledException)
        {
            // The turn ended meanwhile; the reason below is still the one a pending request should read.
        }

        Break($"the agent exited with code {transport.ExitCode} before answering");
    }

    private Task HandleAsync(AcpIncoming message) => message.Kind switch
    {
        AcpIncomingKind.Response => Resolved(message),
        AcpIncomingKind.Request => AnswerAsync(message),
        AcpIncomingKind.Notification => Noted(message),
        _ => Task.CompletedTask,
    };

    private Task Resolved(AcpIncoming response)
    {
        TaskCompletionSource<JsonElement>? waiting;
        lock (_gate)
        {
            // A response to an id nobody is waiting for — or one spelled as something other than the number this
            // client sent — is the agent's mistake, not a fault here: ignored, never a throw inside the pump.
            if (response.Id.ValueKind != JsonValueKind.Number || !response.Id.TryGetInt64(out var id) || !_pending.Remove(id, out waiting))
            {
                return Task.CompletedTask;
            }
        }

        if (response.Error.ValueKind == JsonValueKind.Object)
        {
            waiting.TrySetException(new AcpAgentException(
                AcpMessages.Number(response.Error, "code"), AcpMessages.Text(response.Error, "message")));
        }
        else
        {
            waiting.TrySetResult(response.Result);
        }

        return Task.CompletedTask;
    }

    private Task AnswerAsync(AcpIncoming request) => request.Method switch
    {
        "session/request_permission" => RefuseAsync(request),
        "fs/read_text_file" => ReadAsync(request),
        _ => transport.WriteLineAsync(
            AcpMessages.Error(request.Id, AcpMessages.MethodNotFound, $"`{request.Method}` is not offered by this client"), ct),
    };

    private Task RefuseAsync(AcpIncoming request)
    {
        var toolCall = AcpMessages.Child(request.Params, "toolCall");
        var optionId = Permission(AcpMessages.Child(request.Params, "options"));
        var option = optionId.Length > 0 ? OptionKind(request.Params, optionId) : "cancelled";
        _refusals.Add(new AcpRefusal(AcpMessages.Text(toolCall, "title"), AcpMessages.Text(toolCall, "kind"), option));

        return transport.WriteLineAsync(
            AcpMessages.Result(request.Id, optionId.Length > 0 ? AcpMessages.Selected(optionId) : AcpMessages.CancelledOutcome()), ct);
    }

    private static string OptionKind(JsonElement parameters, string optionId) =>
        AcpMessages.Child(parameters, "options").EnumerateArray()
            .Where(option => AcpMessages.Text(option, "optionId") == optionId)
            .Select(option => AcpMessages.Text(option, "kind"))
            .FirstOrDefault() ?? string.Empty;

    private Task ReadAsync(AcpIncoming request)
    {
        var path = AcpMessages.Text(request.Params, "path");
        var answer = boundary.Read(path);
        _reads.Add(new AcpReadRecord(path, answer.Served, answer.Refusal));

        return transport.WriteLineAsync(
            answer.Served
                ? AcpMessages.Result(request.Id, AcpMessages.FileContent(Window(answer.Text, request.Params)))
                : AcpMessages.Error(request.Id, AcpMessages.InvalidParams, answer.Refusal),
            ct);
    }

    /// <summary>The agent's optional <c>line</c> (1-based) and <c>limit</c> (lines), applied to the served text; the whole file without them.</summary>
    internal static string Window(string text, JsonElement parameters)
    {
        var line = AcpMessages.Number(parameters, "line");
        var limit = AcpMessages.Number(parameters, "limit");
        if (line <= 1 && limit <= 0)
        {
            return text;
        }

        var lines = text.Split('\n');
        var from = (int)Math.Clamp(line - 1, 0, lines.Length);
        var count = limit > 0 ? (int)Math.Min(limit, lines.Length - from) : lines.Length - from;

        return string.Join('\n', lines.Skip(from).Take(count));
    }

    private Task Noted(AcpIncoming notification)
    {
        if (notification.Method == "session/update")
        {
            Update(AcpMessages.Child(notification.Params, "update"));
        }

        return Task.CompletedTask;
    }

    private void Update(JsonElement update)
    {
        switch (AcpMessages.Text(update, "sessionUpdate"))
        {
            case "agent_message_chunk":
                Append(AcpMessages.Child(update, "content"));
                break;
            case "usage_update":
                _usage = new AcpUsage(AcpMessages.Number(update, "used"), AcpMessages.Number(update, "size"), true);
                break;
            case "tool_call" or "tool_call_update":
                ToolCall(update);
                break;
        }
    }

    private void Append(JsonElement content)
    {
        if (AcpMessages.Text(content, "type") != "text")
        {
            return;
        }

        var text = AcpMessages.Text(content, "text");
        if (_answer.Length + text.Length > MaxAnswerChars)
        {
            Break($"the agent wrote more than {MaxAnswerChars / (1024 * 1024)} Mi characters of answer — not an answer, not kept");

            return;
        }

        _answer.Append(text);
    }

    /// <summary>A tool call, or the update of one by id: the last status reported stands.</summary>
    private void ToolCall(JsonElement update)
    {
        var id = AcpMessages.Text(update, "toolCallId");
        var at = _toolCalls.FindIndex(call => call.Id == id);
        var was = at >= 0 ? _toolCalls[at] : new AcpToolCall(id, string.Empty, string.Empty, string.Empty);
        var now = new AcpToolCall(
            id,
            Either(AcpMessages.Text(update, "title"), was.Title),
            Either(AcpMessages.Text(update, "kind"), was.Kind),
            Either(AcpMessages.Text(update, "status"), was.Status));
        if (at >= 0)
        {
            _toolCalls[at] = now;
        }
        else
        {
            _toolCalls.Add(now);
        }
    }

    private static string Either(string fresh, string kept) => fresh.Length > 0 ? fresh : kept;

    /// <summary>The stream is unusable from here on: every pending request fails with the reason, and no new one is sent.</summary>
    private void Break(string why)
    {
        TaskCompletionSource<JsonElement>[] waiting;
        lock (_gate)
        {
            if (_broken.Length == 0)
            {
                _broken = why;
            }

            waiting = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var request in waiting)
        {
            request.TrySetException(new AcpProtocolException(why));
        }
    }
}

/// <summary>The agent answered a request of ours with a JSON-RPC error — its refusal, in its words.</summary>
internal sealed class AcpAgentException(long code, string message) : Exception(message)
{
    public long Code { get; } = code;
}

/// <summary>The stream itself failed: closed early, not listening, or not an answer.</summary>
internal sealed class AcpProtocolException(string message) : Exception(message);
