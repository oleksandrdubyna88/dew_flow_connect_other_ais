using CoaiMcp.Core.Context;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// E7.S3, the size half — a stored code round's size is written through the two shipped writers inside ONE transaction, carries the backfill origin
/// in the file and in the database alike, and survives a live re-record that measured nothing. Over a real <see cref="PanelService"/>, real git and FakeCli.
/// </summary>
public sealed class ABackfilledSizeIsWrittenWithItsRowsTests : LiveSizedGateBase
{
    private BackfillRig Rig(Action<WritePoint>? fault = null) =>
        new(Data, new PanelSettings { DataDir = Data, AgentLogDir = Temp.At("home") }, new SessionStore(Data), fault: fault);

    [Fact]
    public async Task ABackfilledSize_CarriesTheBackfillOrigin_InTheFileAndInTheDatabase()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        using (var rig = Rig())
        {
            await rig.RunAsync();
            rig.Counts.SizeWritten.Should().Be(2);
            rig.Counts.SizeDelta[ScopeState.FirstRound].Should().Be(1);
            rig.Counts.SizeDelta[ScopeState.Recorded].Should().Be(1, "round 2's delta was measured against the size this run gave round 1");
        }

        StoreDump.Rows(Data, "SELECT size_origin FROM rounds WHERE stage = 'CodeReview' ORDER BY number").Should().Be("backfill\nbackfill\n");
        LiveCodeRounds().Should().OnlyContain(round => round.Size!.Origin == SizeOrigin.Backfill);
        SizeRows().Should().OnlyContain(row => !row.Contains("|live|"));
    }

    [Fact]
    public async Task ASecondRun_KeepsEverySizeItGave()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        using (var first = Rig())
        {
            await first.RunAsync();
        }

        var before = StoreDump.AuthorsAndSize(Data);

        using var second = Rig();
        await second.RunAsync();

        second.Counts.SizeWritten.Should().Be(0);
        second.Counts.SizeKept.Should().Be(2, "a backfill never re-measures its own size");
        second.Writes.Writes.Should().Be(0);
        StoreDump.AuthorsAndSize(Data).Should().Be(before);
    }

    /// <summary>
    /// Code gate finding 7 (codex): a round whose decision is a DEFERRAL is not recorded on either side — the size waits with the authors. Before the
    /// fix the authors were deferred and the size was measured and written to the database alone, so the file (unreadable now, readable later) and
    /// the database disagreed on the size for ever.
    /// </summary>
    [Fact]
    public async Task ADeferredRound_HasNoSizeWrittenEither_TheSessionFileDidNotRead()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        var sessionFile = Directory.GetFiles(Path.Combine(Data, "sessions"), "session-*.json").Single();
        await File.WriteAllTextAsync(sessionFile, "{ torn", Ct);
        var before = StoreDump.AuthorsAndSize(Data);

        using var rig = Rig();
        await rig.RunAsync();

        rig.Counts.Deferred[BackfillWhy.FileUnreadable].Should().BeGreaterThan(0, "a file that exists but does not load defers every round of its gate");
        rig.Counts.SizeWritten.Should().Be(0, "a deferred round is not recorded on either side: the size is retried with the authors, never written to the database alone");
        rig.Writes.Writes.Should().Be(0);
        StoreDump.AuthorsAndSize(Data).Should().Be(before);
    }

    [Fact]
    public async Task ALiveSize_IsNeverReplaced()
    {
        await RecordTwoCodeRoundsLiveAsync();
        var before = StoreDump.AuthorsAndSize(Data);

        using var rig = Rig();
        await rig.RunAsync();

        rig.Counts.SizeKept.Should().Be(2);
        rig.Counts.SizeWritten.Should().Be(0);
        SizeRows().Should().OnlyContain(row => row.Contains("|live|") || row.Contains("||"), "the live origin stands");
        before.Should().Contain("|live|");
    }

    [Fact]
    public async Task TheProjectionIsOneTransaction_AFaultBetweenTheTwoWritersLeavesNoHalfASize()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        using var killed = Rig(fault: point =>
        {
            if (point == WritePoint.ChangesRecorded)
            {
                throw new IOException("killed between the size rows and the authors rows");
            }
        });

        var run = () => killed.RunAsync();
        await run.Should().ThrowAsync<IOException>();

        StoreDump.Rows(Data, "SELECT COUNT(*) FROM round_changes").Should().Be("0\n", "the size rows the first writer inserted were rolled back with the transaction");
        StoreDump.Rows(Data, "SELECT size_state FROM rounds WHERE stage = 'CodeReview' ORDER BY number").Should().Be("\n\n", "and so were the states beside them");
        LiveCodeRounds()[0].Size.Should().NotBeNull("the file, written first, holds the size: the next run projects it");
    }

    [Fact]
    public async Task ALiveReRecordWithoutASize_KeepsTheBackfilledSize()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        using (var rig = Rig())
        {
            await rig.RunAsync();
        }

        var before = StoreDump.AuthorsAndSize(Data);
        var session = new SessionStore(Data).Load(Repo, "feature")!;

        using (var db = RoundsDb.Open(Data, Serilog.Core.Logger.None)!)
        {
            foreach (var round in session.Rounds)
            {
                db.RecordRound(session.State, round with { Size = null, Gitlinks = null, Authors = null }, []);
            }
        }

        StoreDump.AuthorsAndSize(Data).Should().Be(before, "a null size writes nothing: not recorded, never recorded empty");
    }

    [Fact]
    public async Task NoSentinelNeedsToBeAskedAbout_TheSizeCarriesNoPathAndNoText()
    {
        await RecordTwoCodeRoundsLiveAsync();
        StripSizes();
        using (var rig = Rig())
        {
            await rig.RunAsync();
        }

        SqliteConnection.ClearAllPools();
        StoreDump.AuthorsAndSize(Data).Should().NotContain("app.cs").And.NotContain(Repo, "no path is stored with a size");
    }
}
