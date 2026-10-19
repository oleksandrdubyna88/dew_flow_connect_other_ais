using System.Text.Json;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E7.S2, the size half — a stored code round's size is reconstructed from its RECORDED base and head, through the live assembly, and is the size
/// the live round recorded; never re-based on a merge-base taken today. Driven over a REAL <see cref="PanelService"/>, real git and FakeCli: two
/// code rounds are recorded live, their sizes stripped, and rebuilt by <see cref="StoredSizer"/>.
/// </summary>
public sealed class ABackfilledSizeIsTheLiveSizeTests : FakeCliGateBase
{
    private const string Code = "CodeReview";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string DatabaseFile => Path.Combine(Data, RoundsDb.FileName);

    /// <summary>Open, a plan round, a code round, one more commit, a second code round: the live path records both sizes.</summary>
    private async Task RecordTwoCodeRoundsLiveAsync()
    {
        var service = Service(_ => null, Temp.At("home"));
        await service.OpenAsync(Repo, "feature");
        await service.ReviewPlanAsync(Repo, "feature", "a plan nobody objects to");
        await service.ResolveAsync(Repo, "feature", "[]");
        Parse(await service.ReviewCodeAsync(Repo, "feature", "main", Scope)).TryGetProperty("error", out _).Should().BeFalse();
        await service.ResolveAsync(Repo, "feature", "[]");
        await File.AppendAllTextAsync(Path.Combine(Repo, "app.cs"), "one more\nand another\n", Ct);
        await Git("add", ".");
        await Git("commit", "-m", "the fix");
        Parse(await service.ReviewCodeAsync(Repo, "feature", "main", Scope, true)).TryGetProperty("error", out _).Should().BeFalse();
    }

    private List<RoundRecord> LiveCodeRounds() => [.. Saved().Rounds.Where(round => round.Stage == Code)];

    private static string Shape(RoundSize size) => JsonSerializer.Serialize(new { size.Whole, size.Delta });

    /// <summary>The database's own view of the rounds' sizes: states, origin, gitlinks and every cell row.</summary>
    private List<string> SizeRows()
    {
        SqliteConnection.ClearAllPools();
        using var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False;Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = """
            SELECT r.number, r.size_state, r.delta_state, r.delta_from, r.size_origin, r.size_gitlinks, c.scope, c.coverage, c.kind, c.files, c.added, c.removed
              FROM rounds r LEFT JOIN round_changes c ON c.round_id = r.id
             WHERE r.stage = 'CodeReview' ORDER BY r.number, c.scope, c.coverage, c.kind
            """;
        using var rows = read.ExecuteReader();
        var found = new List<string>();
        while (rows.Read())
        {
            found.Add(string.Join('|', Enumerable.Range(0, rows.FieldCount).Select(at => rows.IsDBNull(at) ? "NULL" : Convert.ToString(rows.GetValue(at), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return found;
    }

    private void StripSizes()
    {
        SqliteConnection.ClearAllPools();
        using (var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False"))
        {
            db.Open();
            BackfillWorld.Run(db, "UPDATE rounds SET size_state = '', delta_state = '', delta_from = '', size_origin = '', size_gitlinks = -1; DELETE FROM round_changes;");
        }

        var store = new SessionStore(Data);
        var file = store.Load(Repo, "feature")!;
        store.Save(file with { Rounds = [.. file.Rounds.Select(round => round with { Size = null, Gitlinks = null })] });
        SqliteConnection.ClearAllPools();
    }

    /// <summary>Every code round of the stored history, sized in order — each size saved into the journal before the next round is measured.</summary>
    private async Task<List<(StoredRound Round, SizeOutcome Outcome)>> BackfillSizesAsync()
    {
        using var db = AuthorPeers.OpenReadOnly(Data)!;
        var gate = new StoredHistory(db, new SessionStore(Data)).Gates().Single();
        var sizer = new StoredSizer(new ContextAssembler(Launcher), Launcher, Serilog.Core.Logger.None);
        var outcomes = new List<(StoredRound, SizeOutcome)>();
        foreach (var round in gate.Rounds.Where(round => round.Stage == Code))
        {
            var index = gate.IndexOf(round);
            var outcome = await sizer.MeasureAsync(round, gate.RepoPath, [.. gate.Journal.Take(index)], Ct);
            if (outcome is SizeOutcome.Measured measured)
            {
                gate.Journal[index] = gate.Journal[index] with { Size = measured.Size, Gitlinks = measured.Gitlinks };
            }

            outcomes.Add((round, outcome));
        }

        return outcomes;
    }

    // ---------- the main one ----------

    [Fact]
    public async Task ABackfilledSize_IsTheLiveSize_FromTheRecordedBase()
    {
        await RecordTwoCodeRoundsLiveAsync();
        var live = LiveCodeRounds();
        live.Should().HaveCount(2).And.OnlyContain(round => round.Size != null, "positive control: the live path recorded both sizes");
        var liveRows = SizeRows();
        StripSizes();
        SizeRows().Should().OnlyContain(row => row.Contains("||") || row.Contains("|NULL|NULL"), "the copy has no size left to compare by accident");

        var outcomes = await BackfillSizesAsync();

        outcomes.Select(outcome => outcome.Outcome).Should().AllBeOfType<SizeOutcome.Measured>();
        for (var at = 0; at < 2; at++)
        {
            var measured = (SizeOutcome.Measured)outcomes[at].Outcome;
            Shape(measured.Size).Should().Be(Shape(live[at].Size!), $"round {at + 1}: the partition and the delta, state by state");
            measured.Size.Origin.Should().Be(SizeOrigin.Backfill);
            measured.Size.Whole.From.Should().Be(outcomes[at].Round.BaseRef, "the whole scope's From IS rounds.base_ref, by construction");
            measured.Gitlinks.Should().Be(live[at].Gitlinks);
        }

        outcomes[1].Outcome.Should().BeOfType<SizeOutcome.Measured>().Which.Size.Delta.State.Should().Be(ScopeState.Recorded, "round 2's delta is measured from round 1's head, the size this run just gave round 1");
        liveRows.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ABackfilledSize_IsStillTheLiveSize_AfterTheBranchShipped()
    {
        await RecordTwoCodeRoundsLiveAsync();
        var live = LiveCodeRounds();
        StripSizes();
        await Git("checkout", "main");
        await Git("merge", "--no-ff", "-m", "ship it", "feature");
        await Git("checkout", "feature");

        var outcomes = await BackfillSizesAsync();

        outcomes.Select(outcome => outcome.Outcome).Should().AllBeOfType<SizeOutcome.Measured>(
            "merge-base(main, head) is the head itself now; the recorded base_ref is what keeps the whole scope from being zero");
        for (var at = 0; at < 2; at++)
        {
            Shape(((SizeOutcome.Measured)outcomes[at].Outcome).Size).Should().Be(Shape(live[at].Size!), $"round {at + 1}: never a zero whole");
        }

        ((SizeOutcome.Measured)outcomes[0].Outcome).Size.Whole.Cells.Sum(cell => cell.Added ?? 0).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ATwoTipRound_IsNotResized()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        await Git("checkout", "main");
        await File.WriteAllTextAsync(Path.Combine(Repo, "other.cs"), "main moved on\n", Ct);
        await Git("add", ".");
        await Git("commit", "-m", "main advanced");
        var moved = await RevParse("main");
        await Git("checkout", "feature");
        SqliteConnection.ClearAllPools();
        using (var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False"))
        {
            db.Open();
            BackfillWorld.Run(db, $"UPDATE rounds SET base_ref = '{moved}' WHERE stage = 'CodeReview' AND number = 1");
        }

        var outcomes = await BackfillSizesAsync();

        outcomes[0].Outcome.Should().Be(new SizeOutcome.Unrecoverable(SizeOutcome.BaseNotAnAncestor), "a base that is not an ancestor of the head is named, never replaced by a base guessed today");
    }

    [Fact]
    public async Task AnUnknownCommitOrRepository_IsNamedNotMeasured()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        SqliteConnection.ClearAllPools();
        using (var db = new SqliteConnection($"Data Source={DatabaseFile};Pooling=False"))
        {
            db.Open();
            BackfillWorld.Run(db, "UPDATE rounds SET head_sha = 'deadbeefdeadbeefdeadbeefdeadbeefdeadbeef' WHERE stage = 'CodeReview' AND number = 1; UPDATE rounds SET base_ref = '-x' WHERE stage = 'CodeReview' AND number = 2");
        }

        var outcomes = await BackfillSizesAsync();

        outcomes[0].Outcome.Should().Be(new SizeOutcome.Unrecoverable(SizeOutcome.HeadNotFound));
        outcomes[1].Outcome.Should().Be(new SizeOutcome.Unrecoverable(SizeOutcome.BaseNotFound), "a value that is not a hex object name never reaches a command line");
    }

    [Fact]
    public async Task APredecessorSizedInThisRun_GivesTheNextRoundItsDelta()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();

        var sized = await BackfillSizesAsync();

        ((SizeOutcome.Measured)sized[1].Outcome).Size.Delta.State.Should().Be(ScopeState.Recorded);
        ((SizeOutcome.Measured)sized[1].Outcome).Size.Delta.From.Should().Be(sized[0].Round.HeadSha, "the delta is measured against round 1's head");
    }

    [Fact]
    public async Task WithRound1Unsizable_Round2sDeltaIsUnavailableBaseUnknown()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        using var db = AuthorPeers.OpenReadOnly(Data)!;
        var gate = new StoredHistory(db, new SessionStore(Data)).Gates().Single();
        var second = gate.Rounds.Single(round => round.Stage == Code && round.Number == 2);
        var sizer = new StoredSizer(new ContextAssembler(Launcher), Launcher, Serilog.Core.Logger.None);

        var outcome = await sizer.MeasureAsync(second, gate.RepoPath, [.. gate.Journal.Take(gate.IndexOf(second))], Ct);

        outcome.Should().BeOfType<SizeOutcome.Measured>().Which.Size.Delta.State.Should().Be(ScopeState.BaseUnknown,
            "round 1 has no size to take a base from, and the base is never filled in from rounds.base_ref behind the predicate's back");
    }

    private async Task<string> RevParse(string rev)
    {
        var result = await Launcher.RunAsync(new CoaiMcp.Runners.Processes.ProcessRequest("git", ["rev-parse", rev], Repo), Ct);

        return result.StdOut.Trim();
    }
}
