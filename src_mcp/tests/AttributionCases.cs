using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The worked examples of E4 §S2.6, one factory each, through <see cref="AttributionScenario"/>. Notation: <c>+m</c> is the base plus
/// <c>m</c> minutes; <c>x</c> = <c>D:/x</c>, <c>y</c> = <c>D:/y</c>; every message has 100 output tokens unless said otherwise.
/// The identity theories (<c>EveryMessageFallsIntoExactlyOneWindowTests</c>, <c>NoMessageIsOwnedTwiceTests</c>) run over
/// <see cref="Names"/>.
/// </summary>
public static class AttributionCases
{
    public const string X = "D:/x";
    public const string Y = "D:/y";

    public static TheoryData<string> Names =>
    [
        "case1-two-repositories-in-one-session",
        "case2-background-work-across-rounds",
        "case3-a-model-switch-inside-one-window",
        "case4-a-resumed-agent",
        "case5-missing-child-transcripts",
        "case7-before-open-and-a-second-gate",
        "case8b-a-peer-of-another-caller",
        "case8b-a-peer-still-running",
        "case8c-the-prior-end-stops",
        "case8d-a-settled-peer-after-an-unsettled-one",
        "caller-changed",
        "blind-rounds",
        "two-repositories-one-agent",
    ];

    internal static AttributionScenario Named(string name) => name switch
    {
        "case1-two-repositories-in-one-session" => Case1(),
        "case2-background-work-across-rounds" => Case2(),
        "case3-a-model-switch-inside-one-window" => Case3(),
        "case4-a-resumed-agent" => Case4(),
        "case5-missing-child-transcripts" => Case5(),
        "case7-before-open-and-a-second-gate" => Case7(),
        "case8b-a-peer-of-another-caller" => Case1(peerCaller: "c9"),
        "case8b-a-peer-still-running" => Case1(peerStatus: "running", peerStamp: 105),
        "case8c-the-prior-end-stops" => Case8c(),
        "case8d-a-settled-peer-after-an-unsettled-one" => Case8d(),
        "caller-changed" => CallerChanged(),
        "blind-rounds" => BlindRounds(),
        "two-repositories-one-agent" => TwoRepositoriesOneAgent(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Not a worked example."),
    };

    /// <summary>
    /// Case 1 — two repositories in one caller session. Gate A on x (open +0; C1 60→70, C2 120→130, C3 180→190), peer B on y (open
    /// +30; C1 100→110), one caller. <paramref name="peerCaller"/>, <paramref name="peerStatus"/> and <paramref name="peerStamp"/> are the
    /// variants of case 8b (B served by another caller; B still running with a stamp on disk).
    /// </summary>
    internal static AttributionScenario Case1(
        string peerCaller = "c0",
        string peerStatus = "done",
        double peerStamp = 110,
        double peerStart = 100,
        double? cx75Answered = 75) =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Code(1, 60, 70).Code(2, 120, 130).Code(3, 180, 190)
            .Gate("B", Y).Opened(30).Round("CodeReview", 1, peerStart, peerStamp, status: peerStatus, caller: peerCaller)
            .Agent("cx", launchedAt: 8).Agent("cy", launchedAt: 33)
            .Agent("s1", launchedAt: 44, requested: "opus").Agent("s2", launchedAt: 79).Agent("s3", launchedAt: 139)
            .Main(-5).Main(20)
            .Message("cx", 10).Message("cx", 40).Message("cx", 50).Message("cx", 75).Message("cx", 130)
            .Message("cy", 35).Message("cy", 90)
            .Message("s1", 45).Message("s2", 80).Message("s3", 140)
            .Write("cx", 10, X + "/a.cs").Write("cx", 40, X + "/a.cs").Write("cx", 130, X + "/a.cs")
            .Write("cy", 35, Y + "/b.cs").Write("cy", 90, Y + "/b.cs")
            .WriteWhenAnswered("cx", 75, X + "/a.cs", cx75Answered);

    /// <summary>
    /// Case 2 — background work across rounds. Gate A on x, open +0, C1 60→75, C2 120→130. Main writes x at +20 and +100; the
    /// background agent <c>bg</c> (first line +50) has messages +55 +65 +70 +110 and no writes.
    /// </summary>
    internal static AttributionScenario Case2() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Code(1, 60, 75).Code(2, 120, 130)
            .Main(20).Main(100)
            .Write("main", 20, X + "/p.md").Write("main", 100, X + "/q.cs")
            .Agent("bg", launchedAt: 50, requested: "opus", type: "background")
            .Message("bg", 55, model: "claude-opus-5-5").Message("bg", 65, model: "claude-opus-5-5").Message("bg", 70, model: "claude-opus-5-5").Message("bg", 110, model: "claude-opus-5-5");

    /// <summary>Case 3 — one window; main answers on opus at +10 and +20 and on sonnet at +30; a write into x at +10.</summary>
    internal static AttributionScenario Case3() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Code(1, 60, 70)
            .Main(10).Main(20).Main(30, model: "claude-sonnet-5")
            .Write("main", 10, X + "/a.cs");

    /// <summary>
    /// Case 4 — a resumed agent. Gate A, C1 60→70, C2 120→125, C3 180→190. <c>r1</c> (first line +10) has messages +15 +20, then
    /// +130 +140; it writes x at +15 only; main writes x at +125.
    /// </summary>
    internal static AttributionScenario Case4() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Code(1, 60, 70).Code(2, 120, 125).Code(3, 180, 190)
            .Agent("r1", launchedAt: 10)
            .Message("r1", 15).Message("r1", 20).Message("r1", 130).Message("r1", 140)
            .Write("r1", 15, X + "/a.cs")
            .Main(125).Write("main", 125, X + "/m.cs");

    /// <summary>
    /// Case 5 — missing child transcripts. Gate A, C1 60→70, C2 120→130. An <c>Agent</c> call without a transcript (+30) and a meta
    /// without one (<c>m2</c>, +40); main writes x at +20 and +100, with messages at both.
    /// </summary>
    internal static AttributionScenario Case5() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Code(1, 60, 70).Code(2, 120, 130)
            .MissingChild("main", 30, "agent call without transcript")
            .MetaWithoutTranscript("m2", "main", 40)
            .Main(20).Main(100)
            .Write("main", 20, X + "/a.cs").Write("main", 100, X + "/a.cs");

    /// <summary>
    /// Case 7 — before-open work and a second gate session on the same <c>repo_path</c>. Gate A on x, open +30, C1 60→70; main
    /// messages −5, +10 (writes x at +10), +40, +45 (writes x at +45). Gate G on x, open +200, C1 250→260; main messages +100 and +220,
    /// each with a write into x.
    /// </summary>
    internal static AttributionScenario Case7() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(30).Code(1, 60, 70)
            .Gate("G", X).Opened(200).Code(1, 250, 260)
            .Main(-5).Main(10).Main(40).Main(45).Main(100).Main(220)
            .Write("main", 10, X + "/a.cs").Write("main", 45, X + "/a.cs").Write("main", 100, X + "/a.cs").Write("main", 220, X + "/a.cs");

    /// <summary>
    /// Case 8c — the prior end stops at an unsettled peer round. Gate A on x, open +0, C1 starts +60 and is still running; gate G on x,
    /// open +100, C1 120→140; main message +30 with a write into x at +30.
    /// </summary>
    internal static AttributionScenario Case8c(string aStatus = "running", double aStamp = 65) =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Round("CodeReview", 1, 60, aStamp, status: aStatus)
            .Gate("G", X).Opened(100).Code(1, 120, 140)
            .Main(30).Write("main", 30, X + "/a.cs");

    /// <summary>
    /// Case 8d — a settled peer round after an unsettled one. Case 8c with A C1 60→70 done and A C2 starting +80 and running; G open
    /// +100; main messages +50 and +70 (writes into x at both).
    /// </summary>
    internal static AttributionScenario Case8d() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Code(1, 60, 70).Round("CodeReview", 2, 80, 85, status: "running")
            .Gate("G", X).Opened(100).Code(1, 120, 140)
            .Main(50).Main(70).Write("main", 50, X + "/a.cs").Write("main", 70, X + "/a.cs");

    /// <summary>Caller changed — gate A, C1 60→70 by <c>c0</c>, C2 120→130 by <c>c1</c>; the scenario's caller is the one asked for.</summary>
    internal static AttributionScenario CallerChanged(string caller = "c1", string priorCaller = "c0") =>
        AttributionScenario.For(caller)
            .Gate("A", X).Opened(0)
            .Round("CodeReview", 1, 60, 70, caller: priorCaller)
            .Round("CodeReview", 2, 120, 130, caller: caller)
            .Main(80).Write("main", 80, X + "/a.cs");

    /// <summary>
    /// Blind rounds — journal C1 (no start), C2 120→130, C3 180→190, and a skipped round S at +90 between C1 and C2 that opens no
    /// window. Main messages at +130 and +150.
    /// </summary>
    internal static AttributionScenario BlindRounds() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0)
            .Round("CodeReview", 1, null, 55)
            .Round("CodeReview", 2, 120, 130)
            .Round("CodeReview", 3, 180, 190)
            .Main(130).Main(150).Write("main", 130, X + "/a.cs");

    /// <summary>
    /// Two repositories, one agent — gate A on x, peer B on y, both opened +0, A C1 60→70, B C1 60→80. <c>w2</c> writes x at +10 and y
    /// at +20 and has messages +10 +20 +30.
    /// </summary>
    internal static AttributionScenario TwoRepositoriesOneAgent() =>
        AttributionScenario.For()
            .Gate("A", X).Opened(0).Code(1, 60, 70)
            .Gate("B", Y).Opened(0).Code(1, 60, 80)
            .Agent("w2", launchedAt: 9)
            .Message("w2", 10).Message("w2", 20).Message("w2", 30)
            .Write("w2", 10, X + "/a.cs").Write("w2", 20, Y + "/b.cs");
}
