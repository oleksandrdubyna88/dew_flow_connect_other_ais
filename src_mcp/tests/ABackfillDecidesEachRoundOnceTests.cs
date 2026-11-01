using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E7.S1 — what a backfill may do to each stored round, decided in ONE pure place: never an equal or newer observation overwritten, never a
/// stronger row replaced by a weaker one, and the two exceptions (a provisional row once its peers are final, a snapshot an older server
/// stripped) the only recomputes of a current row. The table walks every cell of the product of the inputs the decision reads.
/// </summary>
public sealed class ABackfillDecidesEachRoundOnceTests
{
    private static readonly int Reader = ObservedSession.ReaderVersion;
    private static readonly int Attribution = RoundAttribution.Version;

    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    // ---------- the builder: one round that is current, observed, held by its file and agreeing with it ----------

    private sealed record Spec(
        string State = "observed",
        int? DbReader = null,
        int? DbAttribution = null,
        SessionFileState File = SessionFileState.Present,
        bool HasRound = true,
        bool HasAuthors = true,
        string SnapshotState = "observed",
        int? SnapReader = null,
        int? SnapAttribution = null,
        string IdSource = "registry",
        ImmutableArray<string>? Gates = null,
        bool Located = true,
        PeerStatus? Peers = null,
        bool Agrees = true,
        string Stage = "CodeReview",
        string Status = RoundRecord.Done,
        string Verdict = "proceed",
        DateTime? Started = null,
        DateTime? Since = null,
        string SizeState = "",
        string BaseRef = "aaaaaaaa",
        string Head = "bbbbbbbb");

    private static BackfillInput Input(Spec spec) => new(
        new StoredRound(
            1, "s1", spec.Stage, 1, spec.Status, spec.Verdict, "11111111-1111-1111-1111-111111111111", "claude", string.Empty, spec.Started ?? Start,
            spec.State, "registry", spec.DbReader ?? Reader, spec.DbAttribution ?? Attribution, spec.SizeState, spec.BaseRef, spec.Head),
        new SnapshotPresence(
            spec.File, spec.HasRound, spec.HasAuthors, spec.SnapshotState, spec.SnapReader ?? Reader, spec.SnapAttribution ?? Attribution,
            spec.Gates ?? [], spec.IdSource, false),
        spec.Located,
        spec.Peers ?? PeerStatus.Nobody,
        spec.Agrees,
        spec.Since);

    private static BackfillAction Decide(Spec spec) => BackfillDecision.Decide(Input(spec));

    private static void Is(BackfillAction actual, BackfillVerb verb, BackfillWhy why = BackfillWhy.None) =>
        actual.Should().Be(BackfillAction.Of(verb, why));

    // ---------- the table ----------

    private enum DbKind { Empty, Observed, Partial, PartialPeerRunning, Unobserved, IllFormed }

    private enum Rel { Older, Equal, Newer }

    private sealed record Cell(
        SessionFileState File,
        bool HasRound,
        bool HasAuthors,
        DbKind Db,
        Rel DbReader,
        Rel DbAttribution,
        Rel SnapReader,
        Rel SnapAttribution,
        bool Located,
        bool StillRuns,
        bool PeersKnown,
        bool Subset,
        bool Healed,
        bool Agrees);

    private static string StateOf(DbKind kind) => kind switch
    {
        DbKind.Empty => string.Empty,
        DbKind.Observed => "observed",
        DbKind.Partial => "partial: read budget",
        DbKind.PartialPeerRunning => "partial: peer running",
        DbKind.Unobserved => "unobserved: transcript not found",
        _ => "partial: read budget 2",
    };

    private static int Version(int current, Rel rel) => rel switch { Rel.Older => current - 1, Rel.Equal => current, _ => current + 1 };

    private static IEnumerable<Cell> EveryCell() =>
        from file in Enum.GetValues<SessionFileState>()
        from hasRound in new[] { false, true }
        from hasAuthors in new[] { false, true }
        from db in Enum.GetValues<DbKind>()
        from dbReader in Enum.GetValues<Rel>()
        from dbAttribution in Enum.GetValues<Rel>()
        from snapReader in Enum.GetValues<Rel>()
        from snapAttribution in Enum.GetValues<Rel>()
        from located in new[] { false, true }
        from stillRuns in new[] { false, true }
        from peersKnown in new[] { false, true }
        from subset in new[] { false, true }
        from healed in new[] { false, true }
        from agrees in new[] { false, true }
        select new Cell(file, hasRound, hasAuthors, db, dbReader, dbAttribution, snapReader, snapAttribution, located, stillRuns, peersKnown, subset, healed, agrees);

    private static BackfillInput InputOf(Cell cell)
    {
        ImmutableArray<string> found = ["peer-a", "peer-b"];
        ImmutableArray<string> named = cell.Subset ? ["peer-b", "peer-a", "peer-z"] : ["peer-a"];

        return Input(new Spec(
            State: StateOf(cell.Db),
            DbReader: Version(Reader, cell.DbReader),
            DbAttribution: Version(Attribution, cell.DbAttribution),
            File: cell.File,
            HasRound: cell.HasRound,
            HasAuthors: cell.HasAuthors,
            SnapReader: Version(Reader, cell.SnapReader),
            SnapAttribution: Version(Attribution, cell.SnapAttribution),
            IdSource: cell.Healed ? "healed" : "registry",
            Gates: named,
            Located: cell.Located,
            Peers: new PeerStatus(cell.PeersKnown, found, cell.StillRuns),
            Agrees: cell.Agrees));
    }

    /// <summary>The rules of the plan, written once, as an ordered list: first match wins. Independent of the product's own helpers.</summary>
    private static (BackfillVerb Verb, BackfillWhy Why) Expected(Cell c)
    {
        var holds = c.File == SessionFileState.Present && c.HasRound;
        var snapshot = holds && c.HasAuthors;
        var dbKnown = c.Db is not (DbKind.Empty or DbKind.IllFormed);
        var dbOlder = c.DbReader == Rel.Older || c.DbAttribution == Rel.Older;
        var snapCurrent = c.SnapReader != Rel.Older && c.SnapAttribution != Rel.Older;
        var running = c.Db == DbKind.PartialPeerRunning;
        var grown = c.PeersKnown && snapshot && !c.Healed && !c.Subset;
        var obsOrPartial = c.Db is DbKind.Observed or DbKind.Partial or DbKind.PartialPeerRunning;
        var agrees = !snapshot || c.Agrees;

        var rules = new (bool When, BackfillVerb Verb, BackfillWhy Why)[]
        {
            (c.File == SessionFileState.Unreadable, BackfillVerb.Defer, BackfillWhy.FileUnreadable),
            (!c.Located && snapshot && (!dbKnown || dbOlder || !c.Agrees), BackfillVerb.Reproject, BackfillWhy.None),
            (!c.Located && snapshot, BackfillVerb.Keep, BackfillWhy.TranscriptGone),
            (!c.Located && dbKnown && holds, BackfillVerb.Heal, BackfillWhy.None),
            (!c.Located && dbKnown, BackfillVerb.Keep, BackfillWhy.NoFile),
            (!c.Located, BackfillVerb.Compute, BackfillWhy.Computed),
            (running && !c.PeersKnown, BackfillVerb.Defer, BackfillWhy.PeersUnknown),
            ((running || grown) && c.StillRuns, BackfillVerb.Keep, BackfillWhy.PeerStillRunning),
            (running || grown, BackfillVerb.Compute, BackfillWhy.RecomputedProvisional),
            (holds && !c.HasAuthors && dbKnown, BackfillVerb.Compute, BackfillWhy.RecomputedSnapshotLost),
            ((snapshot || !holds) && obsOrPartial && !dbOlder && snapshot && !agrees, BackfillVerb.Reproject, BackfillWhy.None),
            ((snapshot || !holds) && obsOrPartial && !dbOlder, BackfillVerb.Keep, BackfillWhy.EqualOrNewer),
            (snapshot && snapCurrent && (!dbKnown || dbOlder || !c.Agrees), BackfillVerb.Reproject, BackfillWhy.None),
        };

        foreach (var rule in rules)
        {
            if (rule.When)
            {
                return (rule.Verb, rule.Why);
            }
        }

        return (BackfillVerb.Compute, BackfillWhy.Computed);
    }

    [Fact]
    public void EveryCellOfTheTable_GetsTheVerbTheRulesGive()
    {
        var wrong = new List<string>();
        var cells = 0;
        foreach (var cell in EveryCell())
        {
            cells++;
            var actual = BackfillDecision.Decide(InputOf(cell));
            var (verb, why) = Expected(cell);
            if (actual != BackfillAction.Of(verb, why) && wrong.Count < 12)
            {
                wrong.Add($"{cell} => expected {verb}/{why}, found {actual.Verb}/{actual.Why}");
            }
        }

        cells.Should().BeGreaterThan(400_000, "the table is the whole product of the inputs, not a list somebody chose");
        wrong.Should().BeEmpty();
    }

    // ---------- the named guarantees ----------

    [Fact]
    public void AnEqualOrNewerRow_IsKept_OnlyWithItsSnapshotAndAnObservedOrPartialState()
    {
        Is(Decide(new Spec()), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
        Is(Decide(new Spec(State: "partial: read budget")), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
        Is(Decide(new Spec(DbReader: Reader + 1, DbAttribution: Attribution + 1)), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);

        // no snapshot in a file that could hold one: the row is NOT kept, its file is repaired by a recompute
        Is(Decide(new Spec(HasAuthors: false)), BackfillVerb.Compute, BackfillWhy.RecomputedSnapshotLost);

        // a state that is not observed or partial is never kept at current versions
        Is(Decide(new Spec(State: "unobserved: transcript not found", SnapshotState: "unobserved: transcript not found")), BackfillVerb.Compute, BackfillWhy.Computed);

        // no file at all holds no snapshot, and that is what keeps a database-only row from being rewritten
        Is(Decide(new Spec(File: SessionFileState.Gone, HasAuthors: false)), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
        Is(Decide(new Spec(File: SessionFileState.Replaced, HasAuthors: false)), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
    }

    [Fact]
    public void AnOlderAttributionAlone_IsRecomputed()
    {
        // the snapshot in the file came from the same write, so it is older too
        Is(Decide(new Spec(DbAttribution: Attribution - 1, SnapAttribution: Attribution - 1)), BackfillVerb.Compute, BackfillWhy.Computed);
        Is(Decide(new Spec(DbReader: Reader - 1, SnapReader: Reader - 1)), BackfillVerb.Compute, BackfillWhy.Computed);

        // a CURRENT snapshot over an older database row is the completion of a torn run: rows FROM it, not a second read
        Is(Decide(new Spec(DbAttribution: Attribution - 1)), BackfillVerb.Reproject);
    }

    [Theory]
    [InlineData("observed", "observed", true)]
    [InlineData("observed", "partial: read budget", false)]
    [InlineData("observed", "unobserved: transcript not found", false)]
    [InlineData("partial: read budget", "observed", true)]
    [InlineData("partial: read budget", "partial: torn line", true)]
    [InlineData("partial: read budget", "unobserved: transcript not found", false)]
    [InlineData("unobserved: transcript not found", "unobserved: no session id", true)]
    [InlineData("unobserved: transcript not found", "observed", true)]
    [InlineData("", "unobserved: transcript not found", true)]
    public void AnOlderRow_IsReplacedOnlyByAnAtLeastAsStrongResult(string database, string result, bool replaced)
    {
        var settled = BackfillDecision.Settle(new SettleInput(database, Reader - 1, Attribution - 1, result, false, false));

        settled.Should().Be(replaced ? Settlement.Write : Settlement.KeepStronger);
    }

    [Fact]
    public void AWeakerResult_NeverReplacesAStrongerOlderOne()
    {
        BackfillDecision.Settle(new SettleInput("observed", 0, 0, "partial: read budget", false, false)).Should().Be(Settlement.KeepStronger, "observed beats partial");
        BackfillDecision.Settle(new SettleInput("partial: torn line", 0, 0, "unobserved: reader failed", false, false)).Should().Be(Settlement.KeepStronger, "partial beats unobserved");
        BackfillDecision.Settle(new SettleInput("partial: torn line", 0, 0, "partial: read budget", false, false)).Should().Be(Settlement.Write, "equal strength replaces: the newer reader wins a tie");
    }

    [Fact]
    public void AFilledRowWithoutItsSnapshot_IsHealed_WhenTheTranscriptIsGone() =>
        Is(Decide(new Spec(HasAuthors: false, Located: false)), BackfillVerb.Heal);

    [Fact]
    public void AFilledRowWithoutItsSnapshot_IsRecomputed_WhileTheTranscriptExists() =>
        Is(Decide(new Spec(HasAuthors: false, Located: true)), BackfillVerb.Compute, BackfillWhy.RecomputedSnapshotLost);

    [Fact]
    public void APeerRunningRow_IsRecomputed_OnceEveryNamedPeerRoundIsFinal()
    {
        var spec = new Spec(State: "partial: peer running", SnapshotState: "partial: peer running", Gates: ["peer-a"], Peers: new PeerStatus(true, ["peer-a"], false));

        Is(Decide(spec), BackfillVerb.Compute, BackfillWhy.RecomputedProvisional);
    }

    [Fact]
    public void APeerRunningRow_IsKept_WhileANamedPeerRoundStillRuns()
    {
        var spec = new Spec(State: "partial: peer running", SnapshotState: "partial: peer running", Gates: ["peer-a"], Peers: new PeerStatus(true, ["peer-a"], true));

        Is(Decide(spec), BackfillVerb.Keep, BackfillWhy.PeerStillRunning);
    }

    [Fact]
    public void AGrownPeerSet_IsRecomputed_AndAHealedSnapshotNeverIs()
    {
        var grown = new PeerStatus(true, ["peer-a", "peer-new"], false);

        Is(Decide(new Spec(Gates: ["peer-a"], Peers: grown)), BackfillVerb.Compute, BackfillWhy.RecomputedProvisional);
        Is(Decide(new Spec(Gates: [], IdSource: "healed", Peers: grown)), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
        Is(Decide(new Spec(Gates: ["peer-a", "peer-new"], Peers: grown)), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
    }

    [Fact]
    public void AnIncompletePeerLookup_ProvesNoGrowth_AndDefersAPeerRunningRow()
    {
        var unknown = new PeerStatus(false, [], false);

        Is(Decide(new Spec(Gates: [], Peers: unknown)), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
        Is(Decide(new Spec(State: "partial: peer running", SnapshotState: "partial: peer running", Peers: unknown)), BackfillVerb.Defer, BackfillWhy.PeersUnknown);
    }

    [Fact]
    public void AnUnobservedRowAtCurrentVersions_IsNotKept()
    {
        var spec = new Spec(State: "unobserved: transcript not found", SnapshotState: "unobserved: transcript not found");

        Is(Decide(spec), BackfillVerb.Compute, BackfillWhy.Computed);
    }

    [Fact]
    public void AWeakerRecompute_HealsTheFileInstead()
    {
        var settled = BackfillDecision.Settle(new SettleInput("observed", Reader, Attribution, "partial: read budget", false, FileLacksSnapshot: true));

        settled.Should().Be(Settlement.KeepStrongerAndHeal);
    }

    [Fact]
    public void ASnapshotAndProjectionThatDisagreeAtEqualVersions_AreReprojected_NeverKept()
    {
        // the crash shape: a current `observed` snapshot over a current `partial` row
        var spec = new Spec(State: "partial: read budget", SnapshotState: "observed", Agrees: false);

        Is(Decide(spec), BackfillVerb.Reproject);
        Is(Decide(spec with { Located = false }), BackfillVerb.Reproject);
    }

    [Fact]
    public void ARowFromANewerReader_IsNeverReplacedByAnOlderOnesOutput()
    {
        var newer = new SettleInput("observed", Reader + 1, Attribution, "observed", false, false, Reader, Attribution);

        BackfillDecision.Settle(newer).Should().Be(Settlement.KeepStronger, "equal strength, but the database row was written by a newer reader");
        BackfillDecision.Settle(newer with { ResultReader = Reader + 1 }).Should().Be(Settlement.Write, "a result at least as new replaces an equal row");
        BackfillDecision.Settle(newer with { DatabaseReader = Reader, DatabaseAttribution = Attribution + 1, ResultAttribution = Attribution }).Should().Be(Settlement.KeepStronger, "the same for the attribution version");
    }

    [Fact]
    public void AWeakerReprojection_IsKept()
    {
        BackfillDecision.Settle(new SettleInput("observed", Reader, Attribution, "partial: read budget", false, false)).Should().Be(Settlement.KeepStronger);
        BackfillDecision.Settle(new SettleInput("partial: read budget", Reader, Attribution, "observed", false, false)).Should().Be(Settlement.Write);
    }

    [Fact]
    public void ACorrectingVersionBump_ReplacesOlderRowsWhateverTheirStrength_ARefiningOneDoesNot()
    {
        var weaker = new SettleInput("observed", Reader - 1, Attribution, "partial: read budget", false, false);

        BackfillDecision.Settle(weaker, (Reader, 0)).Should().Be(Settlement.WriteCorrected, "version 0 was found WRONG: a defective observed is a false one, not a stronger one");
        BackfillDecision.Settle(weaker, (0, 0)).Should().Be(Settlement.KeepStronger, "a refining bump leaves the guard in force");
        BackfillDecision.Settle(weaker with { DatabaseReader = Reader }, (Reader, 0)).Should().Be(Settlement.KeepStronger, "a row at or above the named version is not below it");
        BackfillDecision.CorrectedBelow.Should().Be((0, 0), "the shipped default names no version");
    }

    [Fact]
    public void AResultThatIsWhatTheDatabaseHolds_WritesNothing_UnlessTheFileLacksItsSnapshot()
    {
        var equal = new SettleInput("observed", Reader, Attribution, "observed", ProjectionEqualsDatabase: true, FileLacksSnapshot: false);

        BackfillDecision.Settle(equal).Should().Be(Settlement.Unchanged);
        BackfillDecision.Settle(equal with { FileLacksSnapshot = true }).Should().Be(Settlement.WriteFileOnly);
    }

    [Fact]
    public void AStateTheStoreRefuses_CountsAsEmpty_SoItIsRecomputed()
    {
        BackfillDecision.Strength("observed").Should().Be(3);
        BackfillDecision.Strength("partial: read budget").Should().Be(2);
        BackfillDecision.Strength("unobserved: transcript not found").Should().Be(1);
        BackfillDecision.Strength(string.Empty).Should().Be(0);
        BackfillDecision.Strength("partial: read budget 2").Should().Be(0, "a count is a state somebody else wrote");
        Is(Decide(new Spec(State: "partial: read budget 2", HasAuthors: false)), BackfillVerb.Compute, BackfillWhy.Computed);
        Is(Decide(new Spec(State: "partial: read budget 2")), BackfillVerb.Reproject);
        BackfillDecision.Settle(new SettleInput("partial: read budget 2", Reader, Attribution, "unobserved: transcript not found", false, false)).Should().Be(Settlement.Write);
    }

    [Fact]
    public void ARoundWithoutItsFile_IsComputedFromTheDatabase_AndKeptOnTheNextRun()
    {
        var gone = new Spec(State: string.Empty, DbReader: 0, DbAttribution: 0, File: SessionFileState.Gone, HasAuthors: false, HasRound: false);

        Is(Decide(gone), BackfillVerb.Compute, BackfillWhy.Computed);
        Is(Decide(gone with { State = "observed", DbReader = Reader, DbAttribution = Attribution }), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
    }

    [Fact]
    public void ACodexCaller_GetsAStateNotASkip()
    {
        // no transcript can be located for a vendor with no reader: the round is still COMPUTED, and the compute ends `unobserved: no reader for codex`
        var spec = new Spec(State: string.Empty, DbReader: 0, DbAttribution: 0, HasAuthors: false, Located: false);

        Is(Decide(spec), BackfillVerb.Compute, BackfillWhy.Computed);
    }

    [Fact]
    public void APlanRound_GetsAuthorsAndNoSize()
    {
        var plan = new Spec(Stage: "PlanReview", State: string.Empty, DbReader: 0, DbAttribution: 0, HasAuthors: false);

        Is(Decide(plan), BackfillVerb.Compute, BackfillWhy.Computed);
        BackfillDecision.SizeOf(Input(plan).Round, null).Should().Be(SizeVerb.NotCode);
    }

    [Theory]
    [InlineData("CodeReview", "interrupted", "proceed")]
    [InlineData("CodeReview", "running", "proceed")]
    [InlineData("CodeReview", "done", "skipped")]
    [InlineData("PlanReview", "interrupted", "proceed")]
    [InlineData("PlanReview", "done", "skipped")]
    [InlineData("FeatureReview", "done", "proceed")]
    [InlineData("DocumentReview", "done", "proceed")]
    public void ASkippedOrInterruptedRound_IsNotSelected(string stage, string status, string verdict)
    {
        var spec = new Spec(Stage: stage, Status: status, Verdict: verdict, State: string.Empty);

        Is(Decide(spec), BackfillVerb.NotSelected);
        BackfillDecision.SizeOf(Input(spec).Round, null).Should().Be(SizeVerb.NotCode);
    }

    [Fact]
    public void ARoundBeforeSince_IsNotSelected()
    {
        Is(Decide(new Spec(Started: Start, Since: Start.AddSeconds(1))), BackfillVerb.NotSelected);
        Is(Decide(new Spec(Started: Start, Since: Start)), BackfillVerb.Keep, BackfillWhy.EqualOrNewer);
        BackfillDecision.SizeOf(Input(new Spec(Started: Start, Since: Start.AddSeconds(1))).Round, Start.AddSeconds(1)).Should().Be(SizeVerb.NotCode);
    }

    [Fact]
    public void ALiveSize_IsNeverReplaced()
    {
        BackfillDecision.SizeOf(Input(new Spec(SizeState: "recorded")).Round, null).Should().Be(SizeVerb.Keep);
        BackfillDecision.SizeOf(Input(new Spec(SizeState: "unavailable: first round")).Round, null).Should().Be(SizeVerb.Keep, "a backfill never re-measures its own either");
        BackfillDecision.SizeOf(Input(new Spec()).Round, null).Should().Be(SizeVerb.Compute);
        BackfillDecision.SizeOf(Input(new Spec(BaseRef: string.Empty)).Round, null).Should().Be(SizeVerb.NoBaseRecorded);
        BackfillDecision.SizeOf(Input(new Spec(Head: string.Empty)).Round, null).Should().Be(SizeVerb.NoHeadRecorded);
    }

    [Fact]
    public void AnExistingButUnreadableSessionFile_IsDeferredNotGone()
    {
        Is(Decide(new Spec(File: SessionFileState.Unreadable)), BackfillVerb.Defer, BackfillWhy.FileUnreadable);
        Is(Decide(new Spec(File: SessionFileState.Unreadable, Located: false)), BackfillVerb.Defer, BackfillWhy.FileUnreadable);
        Is(Decide(new Spec(File: SessionFileState.Gone, HasAuthors: false, State: string.Empty, DbReader: 0, DbAttribution: 0)), BackfillVerb.Compute, BackfillWhy.Computed);
    }

    [Fact]
    public void ARoundWhoseTranscriptIsGone_ReprojectsATornRunsSnapshot_AndOtherwiseKeepsWhatItHas()
    {
        // the completion of a run killed between its two writes: the file holds the snapshot, the database holds none
        Is(Decide(new Spec(State: string.Empty, DbReader: 0, DbAttribution: 0, Located: false)), BackfillVerb.Reproject);
        Is(Decide(new Spec(DbReader: Reader - 1, Located: false)), BackfillVerb.Reproject);
        Is(Decide(new Spec(Located: false)), BackfillVerb.Keep, BackfillWhy.TranscriptGone);
        Is(Decide(new Spec(Located: false, File: SessionFileState.Gone, HasAuthors: false)), BackfillVerb.Keep, BackfillWhy.NoFile);
    }

    [Fact]
    public void ThePlansVersionsAreReadFromTheTypes()
    {
        // the decision compares against the constants of E3 and E4, never against a literal of its own
        Reader.Should().Be(ObservedSession.ReaderVersion);
        Attribution.Should().Be(RoundAttribution.Version);
        Is(Decide(new Spec(DbReader: ObservedSession.ReaderVersion - 1, SnapReader: ObservedSession.ReaderVersion - 1)), BackfillVerb.Compute, BackfillWhy.Computed);
        Is(Decide(new Spec(DbAttribution: RoundAttribution.Version - 1, SnapAttribution: RoundAttribution.Version - 1)), BackfillVerb.Compute, BackfillWhy.Computed);
    }
}
