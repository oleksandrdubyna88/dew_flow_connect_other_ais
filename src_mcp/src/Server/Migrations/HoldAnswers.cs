using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// What reading ONE card's answer file found: nothing to announce, an answer to announce, or a file that is there and could not be read. The third is its own state — it is
/// NOT "no answer": an answer that cannot be read may still be one, and converting the session over it would end the only chance of its notice.
/// </summary>
internal abstract record AnswerRead(string Card)
{
    /// <summary>No answer: the file is missing, empty (no words) or malformed. A card nobody answered; the hold converts as unanswered.</summary>
    internal sealed record None(string Card) : AnswerRead(Card);

    /// <summary>A person's non-empty answer; <paramref name="Decision"/> is empty for a typed answer.</summary>
    internal sealed record Given(string Card, string Decision) : AnswerRead(Card);

    /// <summary>The file exists and an IO or permission error stopped the read: whether a person answered is UNKNOWN.</summary>
    internal sealed record Unreadable(string Card, string Why) : AnswerRead(Card);
}

/// <summary>The answers of one held session: the holds a person answered, and the cards whose answer could not be read.</summary>
internal sealed record HoldAnswers(IReadOnlyList<MigrateV2AnsweredHold> Answered, IReadOnlyList<string> Unreadable)
{
    /// <summary>True when every card of the session was read: nothing in it is UNKNOWN.</summary>
    public bool Known => Unreadable.Count == 0;

    /// <summary>The sentence for a session that cannot be converted yet because an answer could not be read.</summary>
    public string Unknown => $"the answer of card {string.Join(", ", Unreadable)} exists and could not be read, so whether a person answered the hold is unknown";
}

/// <summary>
/// What <see cref="HoldBoundary.Released"/> made of one held session: the conversion, or the session as it was with the reason it was left held — a notice that could not be
/// written and read back, or an answer that could not be read.
/// </summary>
internal sealed record HoldRelease(PersistedSession Session, string Why)
{
    public bool IsConverted => Why.Length == 0;

    public static HoldRelease Converted(PersistedSession session) => new(session, string.Empty);

    public static HoldRelease Held(PersistedSession session, string why) => new(session, why);
}
