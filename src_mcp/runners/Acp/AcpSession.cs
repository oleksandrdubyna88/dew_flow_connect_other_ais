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
/// <para><b>The trace is bounded in SIZE, not only in count</b> (the code round, two reviewers, 2026-10-12). A thousand
/// entries was the cap, and each could carry fields near the 16 Mi-character line cap — a thousand read paths, a
/// thousand permission titles — so a peer could have the session retain gigabytes. Every string a trace entry keeps is
/// cut to <see cref="MaxTraceFieldChars"/> with an ellipsis (<see cref="TruncatedTraceFields"/> counts the cuts), and
/// all three lists share <see cref="MaxTraceChars"/>; an entry that would pass it is dropped and counted with the ones the
/// entry cap drops. The answer keeps its own cap (<see cref="MaxAnswerChars"/>); the report on stdout carries the same
/// bounded lists and the two counts.</para>
/// </remarks>
internal sealed class AcpSession(AcpTransport transport, IAcpReadBoundary boundary, CancellationToken ct)
{
    /// <summary>The most answer kept — past it the agent is not answering, it is dumping; the turn fails rather than holding it.</summary>
    public const int MaxAnswerChars = 8 * 1024 * 1024;

    /// <summary>
    /// The most entries each trace list keeps (refusals, tool calls, reads). A review makes dozens; past a thousand
    /// the agent is looping, and the trace says how many more it dropped rather than growing with the loop.
    /// </summary>
    public const int MaxTraceEntries = 1000;

    /// <summary>The most characters one retained trace field keeps — a title, a kind, an option id, a path; past it, a prefix and an ellipsis.</summary>
    public const int MaxTraceFieldChars = 512;

    /// <summary>The most characters all three trace lists keep together; past it an entry is dropped and counted.</summary>
    public const int MaxTraceChars = 1024 * 1024;

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
    private readonly Dictionary<string, int> _toolCallAt = new(StringComparer.Ordinal);
    private readonly List<AcpReadRecord> _reads = [];
    private long _nextId = 1;
    private int _droppedTraceEntries;
    private long _traceChars;
    private AcpUsage _usage = AcpUsage.Unknown;
    private string _broken = string.Empty;

    /// <summary>The reader, started by <see cref="Pump"/>; awaited by the client on the way out.</summary>
    public Task Reading { get; private set; } = Task.CompletedTask;

    public string Answer => _answer.ToString();

    public AcpUsage Usage => _usage;

    public IReadOnlyList<AcpRefusal> Refusals => _refusals;

    public IReadOnlyList<AcpToolCall> ToolCalls => _toolCalls;

    public IReadOnlyList<AcpReadRecord> Reads => _reads;

    /// <summary>How many trace entries the caps dropped, over all three lists.</summary>
    public int DroppedTraceEntries => _droppedTraceEntries;

    /// <summary>How many retained trace fields were cut to <see cref="MaxTraceFieldChars"/>.</summary>
    public int TruncatedTraceFields { get; private set; }

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
    /// Waits, at most <paramref name="atMost"/>, for every request of ours to be answered — the graceful step of a
    /// teardown: <c>session/cancel</c> was sent, and an agent that is listening answers the prompt <c>cancelled</c>
    /// within a moment. Nothing is waited for when nothing is pending, and nothing past the agent's own exit.
    /// </summary>
    public async Task WaitUntilQuietAsync(TimeSpan atMost)
    {
        var until = DateTime.UtcNow + atMost;
        while (DateTime.UtcNow < until && !transport.Exited.IsCompleted)
        {
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }
            }

            await Task.Delay(50, CancellationToken.None);
        }
    }

    /// <summary>
    /// The option that says no: <c>reject_once</c>, else the first whose kind starts with <c>reject</c>, else none
    /// — and none, too, when the reject option's id is empty or is shared with ANY other option, because an agent
    /// resolving a shared id may land on the allow (finding 1 of the security review). The outcome is then
    /// <c>cancelled</c>. Pure, so every option list is a row in a test.
    /// </summary>
    public static AcpChoice Choose(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Array)
        {
            return AcpChoice.None;
        }

        var offered = options.EnumerateArray()
            .Where(option => option.ValueKind == JsonValueKind.Object)
            .Select(option => new AcpChoice(AcpMessages.Text(option, "optionId"), AcpMessages.Text(option, "kind")))
            .ToArray();
        var chosen = offered.FirstOrDefault(option => option.Kind == "reject_once")
            ?? offered.FirstOrDefault(option => option.Kind.StartsWith("reject", StringComparison.Ordinal))
            ?? AcpChoice.None;

        return chosen.OptionId.Length > 0 && offered.Count(option => option.OptionId == chosen.OptionId) == 1 ? chosen : AcpChoice.None;
    }

    /// <summary>The id of <see cref="Choose"/>'s option, or empty when the outcome is <c>cancelled</c>.</summary>
    public static string Permission(JsonElement options) => Choose(options).OptionId;

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
        catch (AcpProtocolException e)
        {
            // The transport's own verdict — a line past the cap is not a message.
            Break(e.Message);
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

    /// <summary>
    /// One line, handled — and a handler's own exception is this line's problem, never the pump's: a request is
    /// answered with an error, a notification is dropped, and the next line is read (finding 3 of the security
    /// review: a path the filesystem could not normalise faulted the pump and left the agent waiting until the deadline).
    /// </summary>
    private async Task HandleAsync(AcpIncoming message)
    {
        try
        {
            await (message.Kind switch
            {
                AcpIncomingKind.Response => Resolved(message),
                AcpIncomingKind.Request => AnswerAsync(message),
                AcpIncomingKind.Notification => Noted(message),
                _ => Task.CompletedTask,
            });
        }
        catch (Exception e) when (e is not OperationCanceledException && message.Kind == AcpIncomingKind.Request)
        {
            // The agent is told the request failed, not what broke: a system message can name a path.
            await transport.WriteLineAsync(
                AcpMessages.Error(message.Id, message.Method == "fs/read_text_file" ? AcpMessages.InvalidParams : AcpMessages.InternalError, "the request could not be served"),
                ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A notification or a response this client could not take: nothing to answer, nothing to stop for.
        }
    }

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
        var choice = Choose(AcpMessages.Child(request.Params, "options"));
        // The trace records the kind of the option ACTUALLY chosen — read off the choice, never looked up by an id.
        var cuts = 0;
        var refusal = new AcpRefusal(
            Bounded(AcpMessages.Text(toolCall, "title"), ref cuts),
            Bounded(AcpMessages.Text(toolCall, "kind"), ref cuts),
            Bounded(choice.Selected ? choice.Kind : "cancelled", ref cuts));
        Trace(_refusals, refusal, Chars(refusal), cuts);

        return transport.WriteLineAsync(
            AcpMessages.Result(request.Id, choice.Selected ? AcpMessages.Selected(choice.OptionId) : AcpMessages.CancelledOutcome()), ct);
    }

    private Task ReadAsync(AcpIncoming request)
    {
        var path = AcpMessages.Text(request.Params, "path");
        var answer = boundary.Read(path);
        var cuts = 0;
        var record = new AcpReadRecord(Bounded(path, ref cuts), answer.Served, Bounded(answer.Refusal, ref cuts));
        Trace(_reads, record, Chars(record), cuts);

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

        // Cut by scanning for the window's own newlines, never by splitting the file: a two-line window of a served file
        // of two million short lines allocated 80 MB that way (the code gate of epic 4, 2026-10-12).
        var from = LineStart(text, Math.Max(line - 1, 0));
        if (from < 0)
        {
            return string.Empty;
        }

        return text[from..(limit > 0 ? LineEnd(text, from, limit) : text.Length)];
    }

    /// <summary>Where the 0-based line <paramref name="index"/> starts, or -1 when the text has fewer lines.</summary>
    private static int LineStart(string text, long index)
    {
        var at = 0;
        for (var i = 0L; i < index; i++)
        {
            var newline = text.IndexOf('\n', at);
            if (newline < 0)
            {
                return -1;
            }

            at = newline + 1;
        }

        return at;
    }

    /// <summary>Where <paramref name="count"/> lines from <paramref name="from"/> end — before their last newline, or at the end of the text.</summary>
    private static int LineEnd(string text, int from, long count)
    {
        var at = from;
        for (var i = 1L; i < count; i++)
        {
            var newline = text.IndexOf('\n', at);
            if (newline < 0)
            {
                return text.Length;
            }

            at = newline + 1;
        }

        var end = text.IndexOf('\n', at);

        return end < 0 ? text.Length : end;
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

    /// <summary>
    /// A tool call, or the update of one by id: the last status reported stands. Found by id, not by a scan — and
    /// the id is the BOUNDED one, so the index holds no more of it than the entry does; ids that share their first
    /// 511 characters are therefore one entry, which is the bounded side of that bargain and is said here.
    /// </summary>
    private void ToolCall(JsonElement update)
    {
        var cuts = 0;
        var id = Bounded(AcpMessages.Text(update, "toolCallId"), ref cuts);
        var known = _toolCallAt.TryGetValue(id, out var at);
        var was = known ? _toolCalls[at] : new AcpToolCall(id, string.Empty, string.Empty, string.Empty);
        var now = new AcpToolCall(
            id,
            Either(Bounded(AcpMessages.Text(update, "title"), ref cuts), was.Title),
            Either(Bounded(AcpMessages.Text(update, "kind"), ref cuts), was.Kind),
            Either(Bounded(AcpMessages.Text(update, "status"), ref cuts), was.Status));
        if (known)
        {
            Replace(at, was, now, cuts);
        }
        else if (Trace(_toolCalls, now, Chars(now), cuts))
        {
            _toolCallAt[id] = _toolCalls.Count - 1;
        }
    }

    private static string Either(string fresh, string kept) => fresh.Length > 0 ? fresh : kept;

    /// <summary>An update of a kept tool call, within the budget; one that would pass it is dropped and counted, the kept entry standing.</summary>
    private void Replace(int at, AcpToolCall was, AcpToolCall now, int cuts)
    {
        var delta = Chars(now) - Chars(was);
        if (_traceChars + delta > MaxTraceChars)
        {
            _droppedTraceEntries += 1;

            return;
        }

        _toolCalls[at] = now;
        _traceChars += delta;
        TruncatedTraceFields += cuts;
    }

    /// <summary>Adds to a trace list under both caps — the list's count and the trace's characters; past either the entry is counted as dropped instead.</summary>
    /// <param name="cuts">How many of the entry's fields were cut, counted only when the entry is kept.</param>
    /// <returns>True when the entry was kept.</returns>
    private bool Trace<T>(List<T> list, T entry, int chars, int cuts)
    {
        if (list.Count >= MaxTraceEntries || _traceChars + chars > MaxTraceChars)
        {
            _droppedTraceEntries += 1;

            return false;
        }

        list.Add(entry);
        _traceChars += chars;
        TruncatedTraceFields += cuts;

        return true;
    }

    /// <summary>The text within <see cref="MaxTraceFieldChars"/>: as it is, or its prefix and an ellipsis, <paramref name="cuts"/> counting the latter.</summary>
    private static string Bounded(string text, ref int cuts)
    {
        if (text.Length <= MaxTraceFieldChars)
        {
            return text;
        }

        cuts += 1;

        return string.Concat(text.AsSpan(0, MaxTraceFieldChars - 1), "…");
    }

    private static int Chars(AcpRefusal refusal) => refusal.Tool.Length + refusal.Kind.Length + refusal.Option.Length;

    private static int Chars(AcpToolCall call) => call.Id.Length + call.Title.Length + call.Kind.Length + call.Status.Length;

    private static int Chars(AcpReadRecord read) => read.Path.Length + read.Reason.Length;

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

/// <summary>The option a permission request is answered with — or <see cref="None"/>, which is the <c>cancelled</c> outcome.</summary>
/// <param name="OptionId">The id sent back as <c>selected</c>; empty for none.</param>
/// <param name="Kind">The chosen option's kind (<c>reject_once</c>, <c>reject_always</c>), or <c>cancelled</c>.</param>
internal sealed record AcpChoice(string OptionId, string Kind)
{
    public static readonly AcpChoice None = new(string.Empty, "cancelled");

    public bool Selected => OptionId.Length > 0;
}

/// <summary>The agent answered a request of ours with a JSON-RPC error — its refusal, in its words.</summary>
internal sealed class AcpAgentException(long code, string message) : Exception(message)
{
    public long Code { get; } = code;
}

/// <summary>The stream itself failed: closed early, not listening, or not an answer.</summary>
internal sealed class AcpProtocolException(string message) : Exception(message);
