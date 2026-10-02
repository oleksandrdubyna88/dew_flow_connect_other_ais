using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The question record (PLAN_question_consultant.md, S2 acceptance 3 and D14 d): written whole, read back
/// null-normalised, ended by the sweep only when its heartbeat is stale AND its server is dead, gone seven
/// days after it ended, and the directory held under 500 files.
/// </summary>
public sealed class QuestionConsultStoreTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-qstore-").FullName;
    private readonly List<QuestionConsultRecord> _projected = [];
    private readonly QuestionConsultStore _store;

    public QuestionConsultStoreTests() => _store = new QuestionConsultStore(_data, projected: _projected.Add);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A row's budget — the deadline the sweep is told, as the service tells it the settings' <c>RowBudget</c>.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(5);

    private static QuestionConsultRecord Consulting(DateTime heartbeat, int pid = 4242) =>
        new(QuestionConsultStore.NewId(), "caller-1", "claude", "no-session", "D:/repo", "main", "0123abc", "Which shape?", QuestionConsultStore.Stamp(Now.AddMinutes(-10)))
        {
            Context = "I tried both.",
            RunnerPid = pid,
            HeartbeatUtc = QuestionConsultStore.Stamp(heartbeat),
            UpdatedUtc = QuestionConsultStore.Stamp(heartbeat),
            Rows = [new QuestionRowRecord("r1", "codex", "gpt-6-astra", "codex", "question-web", "The internet", "web", "unconfined")],
        };

    private static QuestionConsultRecord Ended(DateTime endedUtc, string status = QuestionConsultStatuses.Answered) =>
        Consulting(endedUtc) with { Status = status, EndedUtc = QuestionConsultStore.Stamp(endedUtc), Outcome = QuestionOutcomes.AnsweredByConsultants };

    [Fact]
    public void ARecordSurvivesARoundTrip_AndIsProjectedOnEveryWrite()
    {
        var written = Consulting(Now);
        _store.Write(written);

        var read = _store.Read(written.Id)!;

        read.Should().BeEquivalentTo(written);
        read.Rows.Single().Flag.Should().Be("unconfined", "D13's flag rides on the row");
        _projected.Should().ContainSingle().Which.Id.Should().Be(written.Id);
    }

    /// <summary>A record written without a field — an older build's, or a hand edit — reads back with every accessor answering empty, never null.</summary>
    [Fact]
    public void ARecordWrittenWithoutAField_ReadsBackNullNormalised()
    {
        var id = QuestionConsultStore.NewId();
        Directory.CreateDirectory(_store.Directory);
        File.WriteAllText(_store.PathFor(id), $$"""
            {"id":"{{id}}","caller":"c","callerKind":"claude","sessionId":"no-session","repoPath":"D:/repo","branch":"main","headSha":"abc","question":"q","startedUtc":"2026-10-01T10:00:00.0000000Z",
             "rows":[{"rowId":"r1","vendor":"codex","model":"m","runtime":"codex","promptId":"question-web","promptTitle":"t","capability":"web","flag":""}]}
            """);

        var read = _store.Read(id)!;

        read.Context.Should().BeEmpty();
        read.Status.Should().BeEmpty("an absent status is absent — the initialiser is for records built in code");
        read.Outcome.Should().BeEmpty();
        read.HeartbeatUtc.Should().BeEmpty();
        read.EndedUtc.Should().BeEmpty();
        read.Alert.Should().BeEmpty();
        read.PlanKey.Should().BeEmpty();
        read.Rows.Single().Reason.Should().BeEmpty();
        read.Rows.Single().Advice.Should().BeEmpty();
        read.Rows.Single().Note.Should().BeEmpty();
        var act = () => read.Rows.Single().Status.Length + read.EscalationId.Length + read.RiskReason.Length;
        act.Should().NotThrow();
    }

    [Fact]
    public void AnIdThatIsNotOneOfOurs_IsNeverTurnedIntoAPath()
    {
        foreach (var hostile in (string[])["../../settings", "a/b", "nope", "", new string('f', 40)])
        {
            _store.Read(hostile).Should().BeNull($"'{hostile}' is not a question id");
        }
    }

    [Fact]
    public void AJsonFileThatIsNotAQuestion_IsNotOne_AndASweepLeavesIt()
    {
        Directory.CreateDirectory(_store.Directory);
        File.WriteAllText(Path.Combine(_store.Directory, "notes.json"), """{"id":"not-an-id","status":"consulting"}""");
        _store.Write(Consulting(Now));

        _store.All().Should().ContainSingle();
        _store.Sweep(_ => false, Now.AddYears(1), Deadline).Should().Be(1, "the one real record expired; the note was never a candidate");
        File.Exists(Path.Combine(_store.Directory, "notes.json")).Should().BeTrue();
    }

    // ---------- D14 (d): the heartbeat sweep ----------

    [Fact]
    public void AStaleHeartbeatANDADeadPid_EndTheRecordInterrupted_AndItsRowsFailNamingTheFact()
    {
        var record = Consulting(Now.AddMinutes(-3));
        _store.Write(record);

        _store.Sweep(_ => false, Now, Deadline).Should().Be(1);

        var swept = _store.Read(record.Id)!;
        swept.Status.Should().Be(QuestionConsultStatuses.Interrupted);
        swept.EndedUtc.Should().NotBeEmpty();
        swept.Rows.Single().Status.Should().Be(RowOutcomes.Failed);
        swept.Rows.Single().Reason.Should().Contain("died");
        _projected.Should().HaveCount(2, "the sweep's own transition is projected too");
    }

    [Fact]
    public void AStaleHeartbeat_WithALivePid_IsLeftAlone()
    {
        // A live server whose fan-out is slow to beat is still a live server — it settles its own record.
        var record = Consulting(Now.AddMinutes(-3));
        _store.Write(record);

        _store.Sweep(_ => true, Now, Deadline).Should().Be(0);

        _store.Read(record.Id)!.Status.Should().Be(QuestionConsultStatuses.Consulting);
    }

    [Fact]
    public void ADeadPid_WithAFreshHeartbeat_IsLeftAlone()
    {
        // A recycled Windows pid: the sweep cannot vouch for it, but a heartbeat thirty seconds old is
        // somebody's, and that somebody is working. The pid alone never ends a question.
        var record = Consulting(Now.AddSeconds(-30));
        _store.Write(record);

        _store.Sweep(_ => false, Now, Deadline).Should().Be(0);

        _store.Read(record.Id)!.Status.Should().Be(QuestionConsultStatuses.Consulting);
    }

    /// <summary>
    /// S4b item 9 (plan §5: "a record past twice its deadline is swept to interrupted"): a live server whose fan-out
    /// still beats but never settles — a wedged launch, a record write that failed — is not left consulting for ever.
    /// Past twice its deadline the record is ended whatever its pid and heartbeat say; inside it, a live one is kept.
    /// </summary>
    [Fact]
    public void AConsultingRecordPastTwiceItsDeadline_IsEndedInterrupted_EvenWithALivePidAndAFreshHeartbeat()
    {
        var wedged = Consulting(Now.AddSeconds(-10)) with { StartedUtc = QuestionConsultStore.Stamp(Now - (2 * Deadline) - TimeSpan.FromSeconds(1)) };
        var inside = Consulting(Now.AddSeconds(-10)) with { StartedUtc = QuestionConsultStore.Stamp(Now - (2 * Deadline) + TimeSpan.FromSeconds(1)) };
        _store.Write(wedged);
        _store.Write(inside);

        _store.Sweep(_ => true, Now, Deadline).Should().Be(1, "only the one past twice its deadline");

        var swept = _store.Read(wedged.Id)!;
        swept.Status.Should().Be(QuestionConsultStatuses.Interrupted);
        swept.EndedUtc.Should().NotBeEmpty();
        swept.Rows.Single().Status.Should().Be(RowOutcomes.Failed);
        swept.Rows.Single().Reason.Should().Contain("twice its deadline", "the reason names the fact that ended it, not a death nobody saw");
        _store.Read(inside.Id)!.Status.Should().Be(QuestionConsultStatuses.Consulting, "a live question inside the window settles its own record");
    }

    [Fact]
    public void TheRuleItself_NeedsBothFacts()
    {
        var stale = Consulting(Now - QuestionConsultStore.HeartbeatStale - TimeSpan.FromSeconds(1));
        var fresh = Consulting(Now - QuestionConsultStore.HeartbeatStale + TimeSpan.FromSeconds(1));

        QuestionConsultStore.Orphaned(stale, _ => false, Now).Should().BeTrue();
        QuestionConsultStore.Orphaned(stale, _ => true, Now).Should().BeFalse("alive");
        QuestionConsultStore.Orphaned(fresh, _ => false, Now).Should().BeFalse("beating");
    }

    // ---------- retention and the cap ----------

    [Fact]
    public void ATerminalRecordOlderThanSevenDays_IsDeleted_AndAYoungerOneKept()
    {
        var old = Ended(Now.AddDays(-8));
        var young = Ended(Now.AddDays(-6));
        _store.Write(old);
        _store.Write(young);

        _store.Sweep(_ => true, Now, Deadline).Should().Be(1);

        _store.Read(old.Id).Should().BeNull();
        _store.Read(young.Id).Should().NotBeNull();
    }

    [Fact]
    public void OverFiveHundredFiles_TheOldestTerminalRecordsGo_AndAConsultingOneNever()
    {
        var live = Consulting(Now);
        _store.Write(live);
        var oldest = Ended(Now.AddDays(-5));
        _store.Write(oldest);
        for (var i = 0; i < QuestionConsultStore.MaxFiles; i++)
        {
            _store.Write(Ended(Now.AddDays(-1).AddSeconds(i)));
        }

        _store.All().Should().HaveCount(QuestionConsultStore.MaxFiles + 2);
        _store.Sweep(_ => true, Now, Deadline).Should().Be(2, "two over the cap");

        _store.All().Should().HaveCount(QuestionConsultStore.MaxFiles);
        _store.Read(oldest.Id).Should().BeNull("the oldest terminal record goes first");
        _store.Read(live.Id).Should().NotBeNull("a consulting record is never the price of the cap");
    }

    [Fact]
    public void TheAnswerFiles_GoOnTheRecordsClock_AndOnlyOurs()
    {
        Directory.CreateDirectory(_store.AnswersDir);
        var ours = Path.Combine(_store.AnswersDir, "codex-question-0123456789abcdef0123456789abcdef.txt");
        var theirs = Path.Combine(_store.AnswersDir, "notes.txt");
        File.WriteAllText(ours, "old advice");
        File.WriteAllText(theirs, "a person's note");
        File.SetLastWriteTimeUtc(ours, Now.AddDays(-8));
        File.SetLastWriteTimeUtc(theirs, Now.AddDays(-8));

        _store.Sweep(_ => true, Now, Deadline).Should().Be(1);

        File.Exists(ours).Should().BeFalse();
        File.Exists(theirs).Should().BeTrue("age is not ownership");
    }

    [Fact]
    public void WithRow_ReplacesOneRowById_AndLeavesTheOthers()
    {
        var record = Consulting(Now) with
        {
            Rows =
            [
                new QuestionRowRecord("a", "codex", "m", "codex", "question-web", "t", "web", ""),
                new QuestionRowRecord("b", "claude", "m", "claude", "question-opinion", "t", "none", ""),
            ],
        };

        var updated = record.WithRow(record.Rows[1] with { Status = RowOutcomes.Answered, Advice = "do this" });

        updated.Rows.Select(r => r.Status).Should().Equal(QuestionRowStatuses.Consulting, RowOutcomes.Answered);
        record.Rows[1].Status.Should().Be(QuestionRowStatuses.Consulting, "the original is untouched");
    }
}
