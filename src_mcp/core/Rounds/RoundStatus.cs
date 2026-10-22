namespace CoaiMcp.Core.Rounds;

/// <summary>
/// A status word of a round's record that the server and core must spell the same — kept here, in the assembly both reference, so there is one
/// string and not a copy in each (<c>RoundRecord</c> in the server, <c>Windows</c> in core).
/// </summary>
public static class RoundStatus
{
    /// <summary>
    /// The caller withdrew the call, or the host stopped, while the round ran: nothing was judged, so the round opens no authors window
    /// and no reader mistakes it for a verdict. Its <c>Verdict</c> is the cause word (<c>withdrawn</c> or <c>hostStopped</c>), never this.
    /// </summary>
    public const string Cancelled = "cancelled";
}
