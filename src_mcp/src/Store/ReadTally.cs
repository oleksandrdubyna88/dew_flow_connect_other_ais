namespace CoaiMcp.Store;

/// <summary>
/// What one read of a caller's transcript counted, apart from what the scan meter counts: the REASONS (each with its count, ordinal by
/// code) and the diagnostics that make no state. One instance serves ONE read.
/// </summary>
/// <remarks>The principle (S4): a reason is recorded when something was LOST or a counted value is uncertain; a diagnostics count is enough
/// when the reader could choose between readings and lost nothing.</remarks>
internal sealed class ReadTally
{
    private readonly SortedDictionary<string, int> _reasons = new(StringComparer.Ordinal);

    public int FilesRead { get; set; }

    public int FilesSkippedByLastWrite { get; set; }

    public int TornTails { get; set; }

    public int SyntheticLines { get; set; }

    public int MetasWithoutModel { get; set; }

    public int ValuesOffShape { get; set; }

    public int WriteCallsRefused { get; set; }

    public int WriteCallsUnanswered { get; set; }

    public int MessagesWithoutUsage { get; set; }

    public int MessagesDroppedAtBudget { get; set; }

    public int AgentFilesEmpty { get; set; }

    public IReadOnlyList<ObservationReason> Reasons => [.. _reasons.Select(pair => new ObservationReason(pair.Key, pair.Value))];

    /// <summary>Adds <paramref name="count"/> to the reason; a count of zero records nothing.</summary>
    public void Reason(string code, int count = 1)
    {
        if (count > 0)
        {
            _reasons[code] = _reasons.GetValueOrDefault(code) + count;
        }
    }
}
