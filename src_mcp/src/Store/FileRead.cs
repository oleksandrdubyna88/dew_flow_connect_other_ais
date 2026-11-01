namespace CoaiMcp.Store;

/// <summary>A <c>tool_use</c> block the reader kept: the call, where its message sits, and what it names. One per block id per file (S4-16).</summary>
internal sealed class CallRecord(BlockKind kind, string tool, string target, int position, string messageId)
{
    public BlockKind Kind { get; } = kind;

    public string Tool { get; } = tool;

    /// <summary>A write's path, empty for an <c>Agent</c> call. A string, never the block (C4).</summary>
    public string Target { get; } = target;

    /// <summary>The order in which the file first carried this block id.</summary>
    public int Position { get; } = position;

    /// <summary>The message ids of every line that carried this block id — usually one; a re-logged line can repeat it.</summary>
    public List<string> MessageIds { get; } = [messageId];

    public void NoteMessage(string messageId)
    {
        if (!MessageIds.Contains(messageId))
        {
            MessageIds.Add(messageId);
        }
    }
}

/// <summary>What the file's <c>tool_result</c> lines said about one tool-use id: an error on ANY line, and the EARLIEST stamp that answered it.</summary>
internal sealed class ResultRecord
{
    public bool IsError { get; private set; }

    /// <summary>The earliest stamp of a result line for this id; null when every line for it had no parseable stamp.</summary>
    public DateTime? Answered { get; private set; }

    public void Note(bool isError, DateTime? stamp)
    {
        IsError |= isError;
        Answered = stamp is { } at && (Answered is not { } known || at < known) ? at : Answered;
    }
}

/// <summary>
/// What a read learned about ONE transcript file (main is index 0, then the agent files in key order): its first and largest stamps, its
/// torn lines, and the calls and results its lines carried (E3.S4). The only mutable state of a file; one instance serves one read.
/// </summary>
internal sealed class FileRead(int index, string key, string fullName)
{
    private int _nextPosition;

    public int Index { get; } = index;

    public string Key { get; } = key;

    public string FullName { get; } = fullName;

    public bool IsMain => Index == 0;

    /// <summary>The first parseable line WITH a timestamp, in file order, whatever its type.</summary>
    public DateTime? FirstStamp { get; private set; }

    /// <summary>The largest stamp parsed so far — what a budget stop measures its guard from.</summary>
    public DateTime? MaxStamp { get; private set; }

    public int TornLines { get; private set; }

    /// <summary>Whether the last line this file yielded failed to parse: a torn TAIL, if no later line was yielded.</summary>
    public bool LastLineTorn { get; set; }

    public bool AgentIdChecked { get; set; }

    public bool AgentIdDisagreed { get; set; }

    /// <summary>The read went to the end of the file (it was not stopped by the budget and not skipped).</summary>
    public bool Completed { get; set; }

    /// <summary>The budget stopped the read inside this file.</summary>
    public bool Stopped { get; set; }

    public Dictionary<string, CallRecord> Calls { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, ResultRecord> Results { get; } = new(StringComparer.Ordinal);

    public void Tear()
    {
        TornLines++;
        LastLineTorn = true;
    }

    public void NoteStamp(DateTime? stamp)
    {
        if (stamp is not { } at)
        {
            return;
        }

        FirstStamp ??= at;
        MaxStamp = MaxStamp is { } largest && largest > at ? largest : at;
    }

    public void NoteCall(BlockFacts block, string messageId)
    {
        if (Calls.TryGetValue(block.Id, out var call))
        {
            call.NoteMessage(messageId);

            return;
        }

        Calls[block.Id] = new CallRecord(block.Kind, block.Tool, block.Target, _nextPosition++, messageId);
    }

    public void NoteResult(BlockFacts block, DateTime? stamp)
    {
        if (!Results.TryGetValue(block.Id, out var result))
        {
            result = new ResultRecord();
            Results[block.Id] = result;
        }

        result.Note(block.IsError, stamp);
    }
}
