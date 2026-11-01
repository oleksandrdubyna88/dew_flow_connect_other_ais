namespace CoaiMcp.Runners.Acp;

/// <summary>
/// The words the generic ACP shim (<c>coai-mcp --ask-acp</c>) and its readers share — the flags on its argv, the end words of its one JSON report
/// and the exit it writes for a spent allowance. They belong to the shim, not to a profile: the shim (<c>AskAcpMode</c>) writes them for ANY profile,
/// and a reader of a given profile (<c>KimiRuntime</c> today) imports them from here, exactly as <c>ApiRuntime.EndedBeforeAnAnswerExit</c> is shared with
/// <c>AskApiMode</c>. They lived in the Kimi classes until E4.5 moved them (deviation 57's M3 of todo/PLAN_vibe_and_kimi_runtimes_E4.md); nothing
/// about their values changed.
/// </summary>
public static class AcpWords
{
    /// <summary>The coai-owned ROOT of a profile that needs a home — it holds <c>home/</c> and <c>os/</c>.</summary>
    public const string HomeFlag = "--home";

    /// <summary>The person's tool folder, the first entry of the agent's PATH.</summary>
    public const string PersonsBinFlag = "--persons-bin";

    public const string ModelFlag = "--model";

    /// <summary>The absolute agent executable the shim starts — resolved by the runtime in the unredirected parent, never re-derived inside the redirected shim (deviation 7 of the E4 plan).</summary>
    public const string ExecutableFlag = "--executable";

    /// <summary>The lock folder the shim's tree hold lives in — the runtime's own, never one derived from a profile the shim does not have.</summary>
    public const string LocksFlag = "--locks";

    /// <summary>The session an earlier turn's report named, to continue with <c>session/load</c> instead of starting one (E5.2) — the shim's own flag, parsed apart from <c>--findings</c>'s.</summary>
    public const string SessionFlag = "--session";

    /// <summary>The shim's exit for a known SPENT sentence (EX_TEMPFAIL) — written by <c>AskAcpMode</c>, read by the profile's runtime.</summary>
    public const int SpentExit = 75;

    /// <summary>The end word of an answered turn.</summary>
    public const string AnsweredWord = "answered";

    /// <summary>The end word of a turn that never started an agent.</summary>
    public const string NotStartedWord = "not_started";

    /// <summary>The end word of a turn whose session did not offer the asked model — found out before any prompt.</summary>
    public const string ModelNotOfferedWord = "model_not_offered";

    /// <summary>The end word of a turn whose <c>--session</c> the agent no longer holds: its <c>session/load</c> was answered with an error no known failure names (E5.2 decision 4).</summary>
    public const string SessionLostWord = "session_lost";
}
