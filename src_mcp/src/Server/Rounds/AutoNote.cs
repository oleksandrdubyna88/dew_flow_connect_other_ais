using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>What a round's record says when policy auto decided it: the verdict's own reason, or nothing for every other round.</summary>
/// <remarks>
/// The record's <c>Note</c> is where the reason lives for good — in the session file and, through <c>LoggedNote</c>, in <c>coai.db</c>'s
/// <c>rounds.note</c> — so a later reader can tell a round nobody reviewed from one that passed. A round a person's policy decided
/// has no auto reason, and an empty note is what it always had.
/// </remarks>
internal static class AutoNote
{
    internal static string Of(RoundVerdict verdict) => verdict switch
    {
        RoundVerdict.NotReviewed n => n.Reason,
        RoundVerdict.GoodEnough { Reason.Length: > 0 } g => g.Reason,
        _ => string.Empty,
    };
}
