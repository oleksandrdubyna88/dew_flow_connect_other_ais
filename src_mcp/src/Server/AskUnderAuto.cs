using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The words <c>ask_human</c> answers under policy auto (<c>todo/PLAN_auto_never_asks_a_person_E1.md</c> § 9.2 and N3): the AI's own question sent to
/// the consultants first, a held gate answered at once, a feature review's request window told the server decides. The service owns the doors and
/// the log; this owns the sentences, so each is read in one place and none of them can carry a question's text.
/// </summary>
internal static class AskUnderAuto
{
    /// <summary>The parent's § 4.4 sentence. Only <c>ask_consultants</c> is named: only its id opens the door (the desk accepts no other proof).</summary>
    internal const string ConsultFirstInstruction =
        "The person says: call the consultants — ask_consultants — talk it through, and come back to me only if the two of you did not settle it. "
        + "Then call ask_human again with the consultId.";

    /// <summary>A feature review in its request window: nobody can ask for a second round under policy auto.</summary>
    internal const string NoSecondRound =
        "policy auto: a second feature round runs only on a ground the server finds; this review closes on resolve. "
        + "Nothing was shown to a person and nothing was waited for.";

    private const string Opening = "policy auto: coai waits for nobody, and nothing was waited for (0 seconds). ";

    /// <summary>
    /// What a held gate is told under policy auto. <paramref name="notWritten"/> is the reason the load's release could not be written — one of
    /// <see cref="LegacyHold"/>'s four, quoted and never reworded — or empty when the hold is the person's.
    /// </summary>
    /// <remarks>
    /// The case comes from the <c>Release</c> the load returned, never from <see cref="LegacyHold.Stands"/> alone: a hold from before policy auto that
    /// the person answered stands as well, and is theirs.
    /// </remarks>
    internal static string HeldInstruction(PersistedSession session, string notWritten) =>
        notWritten.Length > 0 ? NotReleasedYet(SessionPolicy.StageOf(session), notWritten) : ThePersons(session);

    private static string NotReleasedYet(string stage, string why) =>
        Opening + LegacyHold.NotWrittenSentence(stage, why)
        + ". No card was written and nothing was shown to a person. The next call that reads this session — ask_human, status, resolve or a review — releases it.";

    private static string ThePersons(PersistedSession session)
    {
        var stage = SessionPolicy.StageOf(session);
        var raised = LegacyHold.Stands(session.State)
            ? $"This {stage} gate was held by a build before auto, and the person has answered its notice, so policy auto leaves the hold to them."
            : $"This {stage} gate was held by this server under a policy that asks a person, before the setting became auto.";

        return Opening + raised + " That hold is the person's: only their answer on its ConnectOtherAIs notice" + NoticeOf(session.State)
            + " releases it, and no new card was written. Say in your summary that this gate is held for a person and why; "
            + "work that does not need this gate may go on. Do not run the review again.";
    }

    /// <summary>The hold's notice, as the sentence names it; nothing at all when the session records none.</summary>
    internal static string NoticeIdOf(SessionState state) => state.HoldQuestions.Count > 0 ? state.HoldQuestions[0] : string.Empty;

    private static string NoticeOf(SessionState state) => NoticeIdOf(state) is { Length: > 0 } id ? $" (escalation {id})" : string.Empty;
}
