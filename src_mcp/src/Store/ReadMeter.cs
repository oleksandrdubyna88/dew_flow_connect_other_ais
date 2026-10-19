namespace CoaiMcp.Store;

/// <summary>
/// One <see cref="ScanMeter"/> for a whole read, with the caller's budget as its <see cref="ScanMeter.Stop"/> (E3.S4 step 1).
/// </summary>
/// <remarks>
/// <para>Bytes are charged on the SLICE the gate yielded; time on the whole read, skipping included. The scan asks <c>Stop</c> after
/// every MiB it scans and, for a line that passed the gate, AFTER the line is charged and BEFORE it is handed on — so the line that trips
/// the budget is charged to the meter and never seen. <see cref="SliceBytes"/> therefore does not include it: it is the bytes of the
/// lines the reader actually received, which is what <c>BytesInSlice &lt;= MaxBytes</c> means.</para>
/// <para>Stopping is sticky: once the budget says stop, every later ask says stop too.</para>
/// </remarks>
internal sealed class ReadMeter
{
    private readonly ReadBudget _budget;
    private readonly DateTimeOffset _started;

    public ReadMeter(ReadBudget budget)
    {
        _budget = budget;
        _started = budget.Clock.GetUtcNow();
        Scan = new ScanMeter { MaxLineBytes = budget.MaxLineBytes };
        Scan.Stop = Exceeded;
    }

    public ScanMeter Scan { get; }

    public bool Stopped { get; private set; }

    /// <summary>The bytes of the lines handed to the reader: what the meter had charged at the last ask that did NOT stop the read.</summary>
    public long SliceBytes { get; private set; }

    private bool Exceeded()
    {
        if (Stopped)
        {
            return true;
        }

        Stopped = Over();
        SliceBytes = Stopped ? SliceBytes : Scan.BytesYielded;

        return Stopped;
    }

    private bool Over() =>
        Scan.BytesYielded > _budget.MaxBytes || _budget.Clock.GetUtcNow() - _started > _budget.MaxTime;
}
