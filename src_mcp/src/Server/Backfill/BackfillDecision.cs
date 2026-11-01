using CoaiMcp.Core.Authors;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// What a backfill may do to each stored round — the whole of "never overwrite a newer or equal reader" with its two exceptions, and of "never
/// replace a stronger observation with a weaker one", in ONE pure place (E7.S1 of
/// <c>research/PLAN_a_round_records_who_wrote_the_code_and_how_much_E7.md</c>). No I/O, no clock: every fact it reads is an argument.
/// </summary>
/// <remarks>
/// <para><b>The rule order</b> is E5 §10 item 11's as corrected by the 2026-10-17 judge review and the 2026-10-18 consultants: selection,
/// deferral, then the rounds whose transcript is GONE (re-project, keep, heal or compute), then — transcript PRESENT — the provisional
/// recompute (a <c>peer running</c> row once its peers are final, or a peer set that grew), the lost-snapshot recompute, the keep, the
/// re-projection and the compute. A transcript that is gone is checked BEFORE anything is healed, so a filled row whose snapshot an older
/// server stripped is repaired by a recompute for as long as the transcript exists.</para>
/// <para><b>The two exceptions</b> to "equal or newer is kept" are the only recomputes of a current row: a provisional one, and a snapshot lost
/// from the file. Everything else this does to a row that has a state is keep, re-project or heal. A second run reads every row this wrote
/// as keep or unchanged, so it writes nothing.</para>
/// <para><b>Two halves.</b> <see cref="Decide"/> says what to do BEFORE any result exists; <see cref="Settle"/> says what becomes of a computed
/// or re-projected result once it does — the strength guard and the equality that makes a re-run write nothing.</para>
/// </remarks>
internal static class BackfillDecision
{
    private const string PlanStage = "PlanReview";
    private const string CodeStage = "CodeReview";
    private const string Healed = "healed";

    private static readonly BackfillAction Pass = BackfillAction.Of(BackfillVerb.NotSelected, BackfillWhy.None);

    /// <summary>
    /// The one exception for J1 (gate finding 4): the reader and attribution versions whose results a RED-test fix story found WRONG. A row whose
    /// stored version is below the named one is replaced whatever the strengths — a defective <c>observed</c> is a false observation, not a
    /// stronger one. <c>0</c>/<c>0</c> names none, which is every shipped build; a fix that merely REFINES leaves it so and the guard holds.
    /// </summary>
    public static (int Reader, int Attribution) CorrectedBelow { get; } = (0, 0);

    // ---------- strength ----------

    /// <summary><c>observed</c> 3 &gt; <c>partial</c> 2 &gt; <c>unobserved</c> 1 &gt; empty 0 — over the kind of a state the store could have written; a state it refuses counts as empty.</summary>
    public static int Strength(string state) => AuthorsSnapshot.IsWellFormed(state) ? KindStrength(AuthorsSnapshot.Split(state).State) : 0;

    private static int KindStrength(string kind) => kind switch
    {
        ObservationKinds.Observed => 3,
        ObservationKinds.Partial => 2,
        ObservationKinds.Unobserved => 1,
        _ => 0,
    };

    /// <summary>Whether a well-formed state names <paramref name="reason"/> — read through the format's one inverse, <see cref="AuthorsSnapshot.Split"/>.</summary>
    private static bool ReasonsInclude(string state, string reason) =>
        AuthorsSnapshot.IsWellFormed(state) && AuthorsSnapshot.Split(state).Reasons.Contains(reason);

    // ---------- selection ----------

    /// <summary>A settled plan or code round that started at or after <c>--since</c> (E5 §3's one sentence).</summary>
    public static bool IsSelected(StoredRound round, DateTime? since) =>
        IsSettled(round) && (since is not { } from || round.StartedUtc >= from);

    private static bool IsSettled(StoredRound round) =>
        round.Stage is PlanStage or CodeStage && round.Status == RoundRecord.Done && round.Verdict != RoundRecord.Skipped;

    // ---------- the authors, before a result exists ----------

    /// <summary>What to do to <paramref name="input"/>'s round, first rule that matches.</summary>
    public static BackfillAction Decide(BackfillInput input)
    {
        if (!IsSelected(input.Round, input.Since))
        {
            return BackfillAction.Of(BackfillVerb.NotSelected);
        }

        if (input.Snapshot.FileState == SessionFileState.Unreadable)
        {
            return BackfillAction.Of(BackfillVerb.Defer, BackfillWhy.FileUnreadable);
        }

        return input.TranscriptLocated ? WithTranscript(input) : WithoutTranscript(input);
    }

    // rule 2 — the transcript is GONE
    private static BackfillAction WithoutTranscript(BackfillInput input)
    {
        if (input.Snapshot.Has)
        {
            return ReprojectionDue(input) ? BackfillAction.Of(BackfillVerb.Reproject) : BackfillAction.Of(BackfillVerb.Keep, BackfillWhy.TranscriptGone);
        }

        return Strength(input.Round.AuthorsState) == 0 ? BackfillAction.Of(BackfillVerb.Compute, BackfillWhy.Computed) : HealOrNoFile(input.Snapshot);
    }

    private static BackfillAction HealOrNoFile(SnapshotPresence snapshot) =>
        snapshot.Holds ? BackfillAction.Of(BackfillVerb.Heal) : BackfillAction.Of(BackfillVerb.Keep, BackfillWhy.NoFile);

    /// <summary>The database is empty, either version is older, or the projection disagrees — the completion of a run killed between its two writes.</summary>
    private static bool ReprojectionDue(BackfillInput input) =>
        Strength(input.Round.AuthorsState) == 0 || DatabaseIsOlder(input.Round) || !input.ProjectionAgrees;

    private static bool DatabaseIsOlder(StoredRound round) =>
        round.AuthorsReader < ObservedSession.ReaderVersion || round.AuthorsAttribution < RoundAttribution.Version;

    // rules 3 – 7 — the transcript is PRESENT
    private static BackfillAction WithTranscript(BackfillInput input)
    {
        var provisional = Provisional(input);
        if (provisional != Pass)
        {
            return provisional;
        }

        return input.Snapshot.Lost && Strength(input.Round.AuthorsState) > 0
            ? BackfillAction.Of(BackfillVerb.Compute, BackfillWhy.RecomputedSnapshotLost)
            : KeepReprojectOrCompute(input);
    }

    // rule 3 — both disjuncts are gated on PeersKnown: a lookup that did not complete hides a running peer round, and a recompute without it
    // would hand the peer's writes to the main session as a stronger, wrong `observed`.
    private static BackfillAction Provisional(BackfillInput input)
    {
        var running = ReasonsInclude(input.Round.AuthorsState, AttributionReasons.PeerRunning);
        if (input.Peers.PeersKnown)
        {
            return ProvisionalWhenKnown(input, running);
        }

        return running ? BackfillAction.Of(BackfillVerb.Defer, BackfillWhy.PeersUnknown) : Pass;
    }

    private static BackfillAction ProvisionalWhenKnown(BackfillInput input, bool running)
    {
        if (!running && !PeerSetGrew(input))
        {
            return Pass;
        }

        return input.Peers.ANamedPeerRoundStillRuns
            ? BackfillAction.Of(BackfillVerb.Keep, BackfillWhy.PeerStillRunning)
            : BackfillAction.Of(BackfillVerb.Compute, BackfillWhy.RecomputedProvisional);
    }

    /// <summary>
    /// A failed projection left out a peer the lookup now finds (E4 G3 (iii)). A healed snapshot, or none, has an UNKNOWN peer set — never an
    /// empty one — so it never triggers this (H1).
    /// </summary>
    private static bool PeerSetGrew(BackfillInput input) =>
        input.Snapshot.Has && input.Snapshot.IdSource != Healed && !input.Peers.PeerIds.All(input.Snapshot.PeerGates.Contains);

    // rules 5 – 7
    private static BackfillAction KeepReprojectOrCompute(BackfillInput input) =>
        CanKeep(input) ? KeepUnlessItDisagrees(input)
        : CanReproject(input) ? BackfillAction.Of(BackfillVerb.Reproject)
        : BackfillAction.Of(BackfillVerb.Compute, BackfillWhy.Computed);

    /// <summary>A current row whose file snapshot and projection disagree is re-projected, never kept: a run killed after the file save leaves exactly that.</summary>
    private static BackfillAction KeepUnlessItDisagrees(BackfillInput input) =>
        input.Snapshot.Has && !input.ProjectionAgrees ? BackfillAction.Of(BackfillVerb.Reproject) : BackfillAction.Of(BackfillVerb.Keep, BackfillWhy.EqualOrNewer);

    /// <summary>
    /// Equal or newer on BOTH versions, a state worth keeping — never <c>unobserved</c>, whose versions are <c>0</c> where a component did not run and
    /// which a present transcript can improve (H2) — and either a snapshot in the file or no file that could hold one.
    /// </summary>
    private static bool CanKeep(BackfillInput input) =>
        (input.Snapshot.Has || !input.Snapshot.Holds) && Strength(input.Round.AuthorsState) >= 2 && !DatabaseIsOlder(input.Round);

    /// <summary>The file's snapshot is current and the database is empty, older or disagrees: rows FROM it, the transcript not read.</summary>
    private static bool CanReproject(BackfillInput input) =>
        input.Snapshot.Has && SnapshotIsCurrent(input.Snapshot) && ReprojectionDue(input);

    private static bool SnapshotIsCurrent(SnapshotPresence snapshot) =>
        snapshot.Reader >= ObservedSession.ReaderVersion && snapshot.Attribution >= RoundAttribution.Version;

    // ---------- what becomes of a result ----------

    /// <summary>
    /// A computed or re-projected result meets what the database holds: written when it is at least as strong, kept stronger when it is not
    /// (healing the file instead of leaving it empty), unchanged when it IS what the database holds. A result no store could have written
    /// (strength 0) is never written.
    /// </summary>
    public static Settlement Settle(SettleInput input, (int Reader, int Attribution)? correctedBelow = null) =>
        input.ProjectionEqualsDatabase ? OfAnEqualResult(input) : OfADifferentResult(input, correctedBelow ?? CorrectedBelow);

    /// <summary>Nothing to write — unless the file lacks the snapshot a recompute is there to give it back, and then only the file is.</summary>
    private static Settlement OfAnEqualResult(SettleInput input) =>
        input.FileLacksSnapshot ? Settlement.WriteFileOnly : Settlement.Unchanged;

    private static Settlement OfADifferentResult(SettleInput input, (int Reader, int Attribution) below) =>
        IsCorrected(input, below) && Strength(input.ResultState) > 0 ? Settlement.WriteCorrected
        : Weaker(input) ? KeepStronger(input)
        : Settlement.Write;

    private static bool IsCorrected(SettleInput input, (int Reader, int Attribution) below) =>
        input.DatabaseReader < below.Reader || input.DatabaseAttribution < below.Attribution;

    /// <summary>
    /// Weaker: a state no store could have written, a lesser strength — or a result a OLDER reader or attribution produced than the one that wrote the row.
    /// A newer reader's row is never overwritten by an older one's output, whatever the two states say.
    /// </summary>
    private static bool Weaker(SettleInput input) =>
        Strength(input.ResultState) is var result && (result == 0 || result < Strength(input.DatabaseState) || ProducedByAnOlderVersion(input));

    private static bool ProducedByAnOlderVersion(SettleInput input) =>
        input.ResultReader < input.DatabaseReader || input.ResultAttribution < input.DatabaseAttribution;

    private static Settlement KeepStronger(SettleInput input) =>
        input.FileLacksSnapshot ? Settlement.KeepStrongerAndHeal : Settlement.KeepStronger;

    // ---------- the size, decided apart ----------

    /// <summary>
    /// A live size is never replaced and a backfill never re-measures its own: a non-empty <c>size_state</c> is kept. An empty one with both the
    /// recorded base and head is computed; without either it is named, and nothing is written.
    /// </summary>
    public static SizeVerb SizeOf(StoredRound round, DateTime? since)
    {
        if (round.Stage != CodeStage || !IsSelected(round, since))
        {
            return SizeVerb.NotCode;
        }

        return round.SizeState.Length > 0 ? SizeVerb.Keep : SizeToCompute(round);
    }

    private static SizeVerb SizeToCompute(StoredRound round) =>
        round.BaseRef.Length == 0 ? SizeVerb.NoBaseRecorded : round.HeadSha.Length == 0 ? SizeVerb.NoHeadRecorded : SizeVerb.Compute;
}
