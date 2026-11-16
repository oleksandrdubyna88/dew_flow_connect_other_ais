namespace CoaiMcp.Core.Rounds;

/// <summary>A hold a build before policy auto raised — the only hold a configuration may release.</summary>
public static class LegacyHold
{
    /// <summary>The single eligibility test: a session is held, and its config has no <c>autonomous</c> member.</summary>
    public static bool Stands(SessionState s) => s.HumanGate && s.Config.PredatesAuto;

    /// <summary>The session as an auto verdict would have left it: nothing held, the stage advances on the next resolve.</summary>
    /// <remarks>
    /// <c>AwaitingResolve</c> and <c>AdvanceOnResolve</c> are set together, whether or not the held round had been resolved: the next
    /// <c>resolve</c> consumes both, through the one path that closes the cadence and records the decisions. The stage is not moved here.
    /// </remarks>
    public static SessionState Released(SessionState s, PanelConfig current) => s with
    {
        HumanGate = false,
        HoldQuestions = [],
        AwaitingResolve = true,
        AdvanceOnResolve = true,
        Config = s.Config.UnderPolicyOf(current),
    };

    /// <summary>The config a deciding load reads: the settings' policy — but untouched while a hold from before auto stands.</summary>
    /// <remarks>
    /// Stamping a standing hold from before policy auto would write the marker onto it and make it this build's hold: a person's, for good.
    /// </remarks>
    public static PanelConfig Stamped(SessionState s, PanelConfig current) =>
        Stands(s) ? s.Config : s.Config.UnderPolicyOf(current);

    /// <summary>What the held round's record says once policy auto released its hold. The verdict stays what it was.</summary>
    public const string Note = "released by policy auto: this hold was raised by a build before auto";

    /// <summary>What a review entry answers after a release, until the resolve that moves the stage.</summary>
    public static string ReleasedSentence(string stage) =>
        $"policy auto released this {stage} hold, raised by a build before auto; call resolve (with the decisions still open, or none) and the stage moves on";

    /// <summary>What a review entry answers while the release could not be written. Never GateHeld: no person has to decide.</summary>
    public static string NotWrittenSentence(string stage, string why) =>
        $"policy auto is releasing this {stage} hold, raised by a build before auto; the release could not be written yet ({why}) — call again";

    // The four reasons a release is not written, and no fifth.
    public const string SettingsUnread = "the settings file could not be read";
    public const string ClaimBusy = "another call holds this session";
    public const string NoticeNotWithdrawn = "a notice could not be withdrawn";
    public const string SessionNotSaved = "the session could not be saved";
}
