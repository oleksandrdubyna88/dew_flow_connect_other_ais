using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// One transcript read per caller session id per run, taken at the LOWEST bound any of that caller's selected rounds needs, and kept for the
/// run (E7.S2): the rounds of one caller are attributed from that one read. A caller's largest session was 650 MB in E1's measurement, so a
/// second read of it is a cost nobody asked for.
/// </summary>
/// <remarks>
/// <para><b>Two phases.</b> <see cref="Probe"/> is the reader a PLANNING pass hands to the live step: it records the bound each round asks for and
/// answers an empty observation, so no byte of a transcript is read. <see cref="Read"/> is the reader of the WRITING pass: it reads once, at the
/// lowest bound planned for that caller, and answers every later round from the kept result. A round that asks for something the kept read does
/// not cover — a bound LOWER than it, which only a change since the plan can cause — is read again and the result replaces the kept one; the read
/// count then says so.</para>
/// <para>A kept result carries the state the read reached, <c>partial: read budget</c> included: a caller's later rounds are attributed from the
/// same observation, so they say the same thing about it.</para>
/// </remarks>
internal sealed class CallerReads(Func<CallerTranscriptLocation, ReadBudget, DateTime?, ObservedSession> read)
{
    private sealed record Kept(DateTime? Bound, ObservedSession Session);

    private readonly Dictionary<string, Kept> _kept = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime?> _planned = new(StringComparer.Ordinal);

    /// <summary>How many times a transcript was actually read.</summary>
    public int Reads { get; private set; }

    /// <summary>The planning reader: records the bound, reads nothing, says the session was observed with nobody in it.</summary>
    public ObservedSession Probe(CallerTranscriptLocation where, ReadBudget budget, DateTime? bound)
    {
        if (where is not CallerTranscriptLocation.Found found)
        {
            return read(where, budget, bound);
        }

        Plan(found.Session.Id, bound);

        return new ObservedSession(found.Session, ObservationState.Observed, null, [], [], [], [], ReadDiagnostics.None);
    }

    /// <summary>The writing reader: once per caller at the planned bound, then from memory.</summary>
    public ObservedSession Read(CallerTranscriptLocation where, ReadBudget budget, DateTime? bound) =>
        where is CallerTranscriptLocation.Found found ? ReadOnce(found, budget, bound) : read(where, budget, bound);

    private ObservedSession ReadOnce(CallerTranscriptLocation.Found found, ReadBudget budget, DateTime? bound)
    {
        if (_kept.TryGetValue(found.Session.Id, out var kept) && Covers(kept.Bound, bound))
        {
            return kept.Session;
        }

        var lowest = LowestOf(bound, _planned.TryGetValue(found.Session.Id, out var planned) ? planned : bound);
        Reads++;
        var session = read(found, budget, lowest);
        _kept[found.Session.Id] = new Kept(lowest, session);

        return session;
    }

    private void Plan(string caller, DateTime? bound) =>
        _planned[caller] = _planned.TryGetValue(caller, out var earlier) ? LowestOf(earlier, bound) : bound;

    /// <summary>Null is "from the beginning", the lowest of all.</summary>
    private static DateTime? LowestOf(DateTime? left, DateTime? right) =>
        left is null || right is null ? null : left < right ? left : right;

    /// <summary>A read from <paramref name="have"/> serves a request from <paramref name="need"/> when it began at or before it.</summary>
    private static bool Covers(DateTime? have, DateTime? need) => have is null || (need is { } wanted && have <= wanted);
}
