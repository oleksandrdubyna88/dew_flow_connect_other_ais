namespace CoaiMcp.Store;

/// <summary>
/// The writes of one read (E3.S4 step 9): a write call is a write only when its result is there, is not an error, and carries a stamp
/// (S4-7, S4-22); it sits at its message's instant (S4-8) and in the checkout its path resolved to.
/// </summary>
internal static class TranscriptWrites
{
    private sealed record Candidate(FileRead File, string Id, CallRecord Call);

    private sealed record Positioned(ObservedWrite Write, int Position);

    /// <summary>
    /// Every counted write, ordered by (instant, agent key ordinal, position in its file). A file the budget stopped contributes none and
    /// counts none: its calls may have had results after the stop. A call whose message is out of range is skipped, uncounted.
    /// </summary>
    public static IReadOnlyList<ObservedWrite> Project(IReadOnlyList<FileRead> files, TranscriptMessages messages, DateTime? fromUtc, ReadTally tally)
    {
        var roots = new CheckoutRoots();

        return [.. files.Where(file => !file.Stopped).SelectMany(CandidatesOf)
            .SelectMany(candidate => Written(candidate, messages, fromUtc, tally, roots))
            .OrderBy(positioned => positioned.Write.Utc).ThenBy(positioned => positioned.Write.AgentKey, StringComparer.Ordinal).ThenBy(positioned => positioned.Position)
            .Select(positioned => positioned.Write)];
    }

    private static IEnumerable<Candidate> CandidatesOf(FileRead file) =>
        file.Calls.Where(pair => pair.Value.Kind == BlockKind.WriteCall).Select(pair => new Candidate(file, pair.Key, pair.Value));

    private static IEnumerable<Positioned> Written(Candidate candidate, TranscriptMessages messages, DateTime? fromUtc, ReadTally tally, CheckoutRoots roots)
    {
        var spot = messages.Locate(candidate.Call.MessageIds, fromUtc);

        return spot.Found ? Answered(candidate, spot, tally, roots) : [];
    }

    private static IEnumerable<Positioned> Answered(Candidate candidate, MessageSpot spot, ReadTally tally, CheckoutRoots roots)
    {
        if (!candidate.File.Results.TryGetValue(candidate.Id, out var result))
        {
            tally.WriteCallsUnanswered++;

            return [];
        }

        // An answer with no stamp is DROPPED before its error flag is read (S4-22): the call reads unanswered, never refused.
        if (result.Answered is not { } answeredUtc)
        {
            return Unstamped(tally);
        }

        return result.IsError ? Refused(tally) : [Place(candidate, spot, answeredUtc, roots)];
    }

    private static IEnumerable<Positioned> Refused(ReadTally tally)
    {
        tally.WriteCallsRefused++;

        return [];
    }

    /// <summary>A result on a line with no parseable stamp (S4-22): it is dropped, the call reads unanswered, and the transcript is inconsistent.</summary>
    private static IEnumerable<Positioned> Unstamped(ReadTally tally)
    {
        tally.WriteCallsUnanswered++;
        tally.Reason(ObservationReasons.InconsistentTranscript);

        return [];
    }

    private static Positioned Place(Candidate candidate, MessageSpot spot, DateTime answeredUtc, CheckoutRoots roots) =>
        new(new ObservedWrite(spot.AgentKey, spot.Utc, answeredUtc, candidate.Call.Tool, roots.Resolve(candidate.Call.Target)), candidate.Call.Position);
}
