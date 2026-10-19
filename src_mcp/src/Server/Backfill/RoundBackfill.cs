using CoaiMcp.Core.Authors;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Server;

/// <summary>The writes a run makes — the real ones (<see cref="BackfillWriter"/>) or none at all (<see cref="DryWrites"/>).</summary>
internal interface IBackfillWrites
{
    WriteResult Write(RoundWrite write);

    WriteResult Heal(StoredGate gate, StoredRound round, string expected);
}

/// <summary>
/// A dry run's writes: none. Every write "succeeds" so the counts, and the in-memory journal the next round reads from, are the writing run's —
/// a predecessor's size "saved" in memory before its successor's delta is measured, database-only sessions included — and no store is opened for writing.
/// </summary>
internal sealed class DryWrites : IBackfillWrites
{
    public WriteResult Write(RoundWrite write) => WriteResult.Written;

    public WriteResult Heal(StoredGate gate, StoredRound round, string expected) => WriteResult.Written;
}

/// <summary>
/// One stored round, from its facts to its counts (E7.S3/S4): decide (S1), compute or re-project the authors, settle against what the database
/// holds, reconstruct the size, write both halves in ONE call, and count what happened. The journal of the round's gate is then given what was
/// written, so the next round of the session sees it — the earlier caller of its timeline, the predecessor of its delta.
/// </summary>
/// <remarks>
/// <para><b>Nothing is written for a deferral, a keep or an equal result.</b> A weaker result never replaces a stronger one; an equal one
/// writes nothing (no claim, no transaction, no save) — except the FILE, when it lacks the snapshot a recompute is there to give it back.</para>
/// <para><b>A heal comes first, then the write:</b> a heal changes only the file, so the fingerprint the write validates against is still the one
/// the round was selected with.</para>
/// </remarks>
internal sealed class RoundBackfill(
    SqliteConnection read,
    BackfillAttribution attribution,
    StoredSizer sizer,
    IBackfillWrites writes,
    BackfillCounts counts,
    DateTime? since)
{
    private enum Step
    {
        Nothing,
        Write,
        FileOnly,
    }

    private sealed record AuthorsPlan(Step Step, AuthorsSnapshot? Snapshot, WrittenAs As, bool HealFirst)
    {
        public static AuthorsPlan Nothing { get; } = new(Step.Nothing, null, WrittenAs.Computed, false);
    }

    private static readonly Dictionary<SizeVerb, string> Unrecorded = new()
    {
        [SizeVerb.NoBaseRecorded] = "size: no base recorded",
        [SizeVerb.NoHeadRecorded] = "size: no head recorded",
    };

    public async Task ProcessAsync(RoundFacts facts, CancellationToken ct)
    {
        var action = BackfillDecision.Decide(facts.Input(since));
        if (action.Verb == BackfillVerb.NotSelected)
        {
            return;
        }

        counts.Selected++;
        if (action.Verb == BackfillVerb.Defer)
        {
            // The WHOLE round waits (code gate finding 7): nothing is measured and nothing is written on either side — a size written to the database
            // alone while the file did not read would leave the two stores disagreeing on it for ever. Both halves are retried on the next run.
            counts.Deferred.Add(action.Why);

            return;
        }

        var authors = Authors(facts, action);
        var size = await SizeAsync(facts, ct);
        Commit(facts, authors, size);
    }

    // ---------- the authors ----------

    private AuthorsPlan Authors(RoundFacts facts, BackfillAction action) =>
        action.Verb == BackfillVerb.Keep ? Kept(action.Why)
        : action.Verb == BackfillVerb.Heal ? AuthorsPlan.Nothing with { HealFirst = true }
        : action.Verb == BackfillVerb.Reproject ? Settled(facts, facts.Record.Authors!, WrittenAs.Reprojected)
        : Settled(facts, Compute(facts), AsOf(action.Why));

    private AuthorsPlan Kept(BackfillWhy why)
    {
        counts.Kept.Add(why);

        return AuthorsPlan.Nothing;
    }

    private AuthorsSnapshot Compute(RoundFacts facts) =>
        attribution.Compute(facts.Before, facts.Record, facts.CallerId, facts.Peers, facts.Round.AuthorsIdSource, planning: false);

    private static WrittenAs AsOf(BackfillWhy why) => why switch
    {
        BackfillWhy.RecomputedProvisional => WrittenAs.RecomputedProvisional,
        BackfillWhy.RecomputedSnapshotLost => WrittenAs.RecomputedSnapshotLost,
        _ => WrittenAs.Computed,
    };

    /// <summary>A result meets what the database holds: written, kept stronger (healing the file instead), or unchanged.</summary>
    private AuthorsPlan Settled(RoundFacts facts, AuthorsSnapshot snapshot, WrittenAs written)
    {
        var round = facts.Round;
        var settlement = BackfillDecision.Settle(new SettleInput(
            round.AuthorsState, round.AuthorsReader, round.AuthorsAttribution, snapshot.Render(), AuthorsProjection.Agree(snapshot, read, round.RoundId), facts.Presence.Lost,
            snapshot.Reader, snapshot.Attribution));

        return settlement is Settlement.Unchanged or Settlement.KeepStronger or Settlement.KeepStrongerAndHeal
            ? Kept(settlement)
            : new AuthorsPlan(settlement == Settlement.WriteFileOnly ? Step.FileOnly : Step.Write, snapshot, settlement == Settlement.WriteCorrected ? WrittenAs.Corrected : written, false);
    }

    private AuthorsPlan Kept(Settlement settlement)
    {
        counts.Kept.Add(settlement == Settlement.Unchanged ? BackfillWhy.Unchanged : BackfillWhy.StrongerExisting);

        return AuthorsPlan.Nothing with { HealFirst = settlement == Settlement.KeepStrongerAndHeal };
    }

    // ---------- the size ----------

    private async Task<SizeOutcome.Measured?> SizeAsync(RoundFacts facts, CancellationToken ct)
    {
        var verb = BackfillDecision.SizeOf(facts.Round, since);
        if (verb != SizeVerb.Compute)
        {
            CountSize(verb);

            return null;
        }

        var outcome = await sizer.MeasureAsync(facts.Round, facts.Gate.RepoPath, facts.Before.Rounds, ct);
        if (outcome is SizeOutcome.Unrecoverable unrecoverable)
        {
            BackfillCounts.Add(counts.SizeUnrecoverable, unrecoverable.Reason);
        }

        return outcome as SizeOutcome.Measured;
    }

    private void CountSize(SizeVerb verb)
    {
        if (verb == SizeVerb.Keep)
        {
            counts.SizeKept++;
        }
        else if (Unrecorded.TryGetValue(verb, out var name))
        {
            BackfillCounts.Add(counts.SizeUnrecoverable, name);
        }
    }

    // ---------- the write ----------

    private void Commit(RoundFacts facts, AuthorsPlan authors, SizeOutcome.Measured? size)
    {
        if (HealRefused(facts, authors))
        {
            return;
        }

        if (HasSomethingToWrite(authors, size))
        {
            Submit(facts, authors, size);
        }
    }

    private bool HealRefused(RoundFacts facts, AuthorsPlan authors) => authors.HealFirst && !Healed(facts);

    private static bool HasSomethingToWrite(AuthorsPlan authors, SizeOutcome.Measured? size) => authors.Step != Step.Nothing || size is not null;

    private void Submit(RoundFacts facts, AuthorsPlan authors, SizeOutcome.Measured? size)
    {
        var result = writes.Write(WriteOf(facts, authors, size));
        if (result == WriteResult.Written)
        {
            Written(facts, authors, size);
        }
        else
        {
            counts.Deferred.Add(DeferredWhy(result));
        }
    }

    private static BackfillWhy DeferredWhy(WriteResult result) => result == WriteResult.DeferredBusy ? BackfillWhy.Busy : BackfillWhy.ChangedUnderUs;

    /// <summary>The round's write: the snapshot (null when only the size changes), the size and its gitlink count, and the fingerprint it was selected with.</summary>
    private static RoundWrite WriteOf(RoundFacts facts, AuthorsPlan authors, SizeOutcome.Measured? size) =>
        new(facts.Gate, facts.Round, KindOf(facts, authors, size), authors.Snapshot, size?.Size, size?.Gitlinks, facts.Fingerprint);

    private static WriteHalves KindOf(RoundFacts facts, AuthorsPlan authors, SizeOutcome.Measured? size) =>
        !facts.Presence.Holds ? WriteHalves.DatabaseOnly
        : authors.Step == Step.FileOnly && size is null ? WriteHalves.FileOnly
        : WriteHalves.Both;

    private bool Healed(RoundFacts facts)
    {
        var result = writes.Heal(facts.Gate, facts.Round, facts.Fingerprint);
        if (result == WriteResult.Written)
        {
            counts.Healed++;

            return true;
        }

        counts.Deferred.Add(DeferredWhy(result));

        return false;
    }

    private void Written(RoundFacts facts, AuthorsPlan authors, SizeOutcome.Measured? size)
    {
        if (authors.Step != Step.Nothing)
        {
            CountAuthors(facts, authors);
        }

        if (size is not null)
        {
            counts.SizeWritten++;
            BackfillCounts.Add(counts.SizeDelta, size.Size.Delta.State);
        }

        Overlay(facts, authors, size);
    }

    private void CountAuthors(RoundFacts facts, AuthorsPlan authors)
    {
        counts.Written.Add(authors.As == WrittenAs.Corrected || facts.Presence.Holds ? authors.As : WrittenAs.DatabaseOnly);
        if (authors.Snapshot is { State: ObservationKinds.Unobserved } unobserved)
        {
            BackfillCounts.Add(counts.Unobserved, string.Join(", ", unobserved.Reasons));
        }
    }

    /// <summary>The journal learns what was written: the next round of the session reads its earlier callers and its delta's predecessor from it.</summary>
    private static void Overlay(RoundFacts facts, AuthorsPlan authors, SizeOutcome.Measured? size)
    {
        if (facts.Index < 0)
        {
            return;
        }

        var record = facts.Gate.Journal[facts.Index];
        facts.Gate.Journal[facts.Index] = WithSize(record with { Authors = authors.Snapshot ?? record.Authors }, size);
    }

    private static RoundRecord WithSize(RoundRecord record, SizeOutcome.Measured? size) =>
        size is null ? record : record with { Size = size.Size, Gitlinks = size.Gitlinks };
}
