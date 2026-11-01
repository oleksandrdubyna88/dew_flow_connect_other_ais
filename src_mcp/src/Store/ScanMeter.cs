namespace CoaiMcp.Store;

/// <summary>
/// What one read of transcript files counted, across EVERY file of that read. The caller's budget is a delegate on it, so the
/// reader never knows what a budget is.
/// </summary>
/// <remarks>
/// <see cref="BytesScanned"/> is every byte read from disk; <see cref="BytesYielded"/> is only the bytes of lines handed on (the
/// SLICE — parent D7: the budget applies to what is kept, because a 650 MB session is scanned whole every time).
/// <see cref="Stop"/> is asked after every MiB scanned, and once per line that passed the gate AFTER the line is charged and
/// BEFORE it is yielded, so the slice never holds a line past the byte budget.
/// </remarks>
internal sealed class ScanMeter
{
    public long BytesScanned { get; set; }

    public long BytesYielded { get; set; }

    public int LinesSkipped { get; set; }

    public int LinesTooLong { get; set; }

    public int ReadFailures { get; set; }

    /// <summary>A line over this is never buffered whole (64 MiB: the biggest real line is far below it).</summary>
    public long MaxLineBytes { get; set; } = 64L * 1024 * 1024;

    public Func<bool> Stop { get; set; } = static () => false;
}
