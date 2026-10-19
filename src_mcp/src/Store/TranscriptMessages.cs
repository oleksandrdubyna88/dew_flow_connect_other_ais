namespace CoaiMcp.Store;

/// <summary>Where a block's message sits: found (counted, in range) with its earliest instant and its agent, or not.</summary>
internal readonly record struct MessageSpot(bool Found, DateTime Utc, string AgentKey)
{
    public static MessageSpot None { get; } = new(false, DateTime.MinValue, string.Empty);
}

/// <summary>What the table knows of one <c>message.id</c>: the earliest line's values, the per-term maximum, and what disagreed.</summary>
internal sealed class MessageEntry
{
    private string _anyModel = string.Empty;
    private string _anyRequested = string.Empty;
    private string _anyEffort = string.Empty;
    private bool _crossFile;
    private bool _clash;

    private MessageEntry(int fileIndex, string agentKey, DateTime stamp, LineFacts line)
    {
        FileIndex = fileIndex;
        AgentKey = agentKey;
        Earliest = stamp;
        Latest = stamp;
        Values = line.Values;
        Tokens = line.Tokens;
        UsageSeen = line.UsageSeen;
        _ = Clashes(line.Values);
    }

    /// <summary>The file that saw this id first in read order — main, then agent files by key.</summary>
    public int FileIndex { get; }

    public string AgentKey { get; }

    public DateTime Earliest { get; private set; }

    public DateTime Latest { get; private set; }

    /// <summary>Model, requested and effort of the EARLIEST line (S4-14), already through their shapes.</summary>
    public MessageValues Values { get; private set; }

    public TokenUsage Tokens { get; private set; }

    public bool UsageSeen { get; private set; }

    /// <summary>Dropped at a budget stop (S4-3): it may still have had a line after the stop.</summary>
    public bool Dropped { get; set; }

    public static MessageEntry First(int fileIndex, string agentKey, DateTime stamp, LineFacts line) => new(fileIndex, agentKey, stamp, line);

    /// <summary>Folds another line of the same id in. The result says which inconsistency, if any, THIS line made visible for the first time.</summary>
    public Disagreement Merge(int fileIndex, DateTime stamp, LineFacts line)
    {
        Latest = stamp > Latest ? stamp : Latest;
        Tokens = PerTermMaximum(Tokens, line.Tokens);
        UsageSeen |= line.UsageSeen;
        TakeIfEarlier(stamp, line);

        return new Disagreement(NewlyInSecondFile(fileIndex), NewlyClashing(line.Values));
    }

    /// <summary>Each term on its own: the three input terms agree across a message's lines and only the output differs, but the rule does not depend on it.</summary>
    private static TokenUsage PerTermMaximum(TokenUsage a, TokenUsage b) => new(
        Math.Max(a.Input, b.Input),
        Math.Max(a.CacheCreation, b.CacheCreation),
        Math.Max(a.CacheRead, b.CacheRead),
        Math.Max(a.Output, b.Output));

    private void TakeIfEarlier(DateTime stamp, LineFacts line)
    {
        if (stamp >= Earliest)
        {
            return;
        }

        Earliest = stamp;
        Values = line.Values;
    }

    private bool NewlyInSecondFile(int fileIndex)
    {
        var now = fileIndex != FileIndex && !_crossFile;
        _crossFile |= now;

        return now;
    }

    private bool NewlyClashing(MessageValues values)
    {
        var now = Clashes(values) && !_clash;
        _clash |= now;

        return now;
    }

    /// <summary>Two PRESENT values of one field that differ. An absent value never differs (G14).</summary>
    private bool Clashes(MessageValues values) =>
        Differs(ref _anyModel, values.Model) | Differs(ref _anyRequested, values.Requested) | Differs(ref _anyEffort, values.Effort);

    private static bool Differs(ref string seen, string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        if (seen.Length == 0)
        {
            seen = value;

            return false;
        }

        return !string.Equals(seen, value, StringComparison.Ordinal);
    }
}

/// <summary>What a merge made visible for the first time: the id in a second file (S4-21), or two present values that differ (S4-14).</summary>
internal readonly record struct Disagreement(bool InSecondFile, bool ValuesDiffer);

/// <summary>
/// The per-<c>message.id</c> table across every file of one read (E3.S4 step 7): one message per id, its figures the per-term
/// MAXIMUM over the id's lines — never the last line, never the sum.
/// </summary>
internal sealed class TranscriptMessages(ReadTally tally)
{
    private readonly Dictionary<string, MessageEntry> _byId = new(StringComparer.Ordinal);

    public void Absorb(int fileIndex, string agentKey, DateTime stamp, LineFacts line)
    {
        if (!_byId.TryGetValue(line.MessageId, out var entry))
        {
            _byId[line.MessageId] = MessageEntry.First(fileIndex, agentKey, stamp, line);

            return;
        }

        var seen = entry.Merge(fileIndex, stamp, line);
        tally.Reason(ObservationReasons.InconsistentTranscript, (seen.InSecondFile ? 1 : 0) + (seen.ValuesDiffer ? 1 : 0));
    }

    /// <summary>
    /// A budget stop inside a file (S4-3): every id first seen in it whose latest seen stamp is after <c>stopStamp − StraddleGuard</c> may
    /// still have a line after the stop, so it is dropped and counted. An older id cannot, without spanning more than the guard.
    /// </summary>
    public void DropAfterStop(int fileIndex, DateTime stopStamp, DateTime? fromUtc)
    {
        var cutoff = CallerTranscript.Before(stopStamp, CallerTranscript.StraddleGuard);
        var dropped = _byId.Values.Where(entry => entry.FileIndex == fileIndex && !entry.Dropped && entry.Latest > cutoff).ToList();
        dropped.ForEach(entry => entry.Dropped = true);
        tally.MessagesDroppedAtBudget += dropped.Count(entry => InRange(entry, fromUtc));
    }

    /// <summary>The earliest instant among the ids' lines — the instant of a block (S4-8) — dropped ids included: the call was parsed.</summary>
    public DateTime? InstantOf(IReadOnlyList<string> messageIds) =>
        messageIds.Select(id => _byId.GetValueOrDefault(id)).OfType<MessageEntry>().Select(entry => (DateTime?)entry.Earliest).Min();

    /// <summary>The counted, in-range message among the ids with the earliest instant, or none.</summary>
    public MessageSpot Locate(IReadOnlyList<string> messageIds, DateTime? fromUtc)
    {
        var counted = messageIds.Select(id => _byId.GetValueOrDefault(id)).OfType<MessageEntry>()
            .Where(entry => Countable(entry, fromUtc)).OrderBy(entry => entry.Earliest).ToList();

        return counted.Count > 0 ? new MessageSpot(true, counted[0].Earliest, counted[0].AgentKey) : MessageSpot.None;
    }

    /// <summary>The messages, ordered by (instant, id ordinal). <paramref name="aliasOf"/> is the meta's alias for an agent key, or empty.</summary>
    public IReadOnlyList<ObservedMessage> Project(Func<string, string> aliasOf, DateTime? fromUtc)
    {
        var messages = _byId.Where(pair => Countable(pair.Value, fromUtc))
            .Select(pair => ToMessage(pair.Key, pair.Value, aliasOf(pair.Value.AgentKey)))
            .OrderBy(message => message.Utc).ThenBy(message => message.MessageId, StringComparer.Ordinal).ToList();
        tally.MessagesWithoutUsage = messages.Count(message => !message.UsageSeen);

        return messages;
    }

    private static ObservedMessage ToMessage(string id, MessageEntry entry, string alias) => new(
        entry.AgentKey,
        id,
        entry.Earliest,
        alias.Length > 0 ? alias : entry.Values.Requested,
        entry.Values.Model,
        entry.Values.Effort.Length > 0 ? entry.Values.Effort : "unknown",
        entry.Tokens,
        entry.UsageSeen);

    private static bool Countable(MessageEntry entry, DateTime? fromUtc) => !entry.Dropped && InRange(entry, fromUtc);

    /// <summary>A message belongs to the window of its EARLIEST line (S4-13).</summary>
    private static bool InRange(MessageEntry entry, DateTime? fromUtc) => fromUtc is not { } from || entry.Earliest >= from;
}
