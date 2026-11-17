using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Serilog;
using Serilog.Events;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Policy auto's release of a hold from before it: what it writes, in which order, and what it leaves alone
/// (<c>todo/PLAN_auto_never_asks_a_person_E1_S1_spec.md</c>, § S1.10.5, § S1.10.9, § S1.10.10 and T63).
/// </summary>
/// <remarks>
/// No CLI: the holds are planted through the real <see cref="SessionStore"/> and <see cref="Escalations"/> of a folder of the test's own, and the
/// policy is the real one over them. The failing writes are the seams the store and the escalations already have.
/// </remarks>
public sealed class SessionPolicyTests : IDisposable
{
    private const string Repo = "D:/fixture-policy";
    private const string Branch = "main";
    private const string SessionId = "s-1";

    private readonly TempDir _data = TempDir.For("coai-policy-");
    private readonly ListSink _sink = new();
    private readonly ILogger _log;

    public SessionPolicyTests() => _log = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(_sink).CreateLogger();

    public void Dispose() => _data.Dispose();

    private static readonly SessionAddress At = new(Branch, string.Empty, string.Empty);

    private SessionStore Store() => new(_data.Path);

    private PanelSettings Settings(bool unread = false) =>
        new PanelSettings { DataDir = _data.Path, Rounds = PanelConfig.Uniform(1, 0) } with { PolicyUnread = unread };

    private SessionPolicy PolicyOver(SessionStore store, Escalations escalations, PanelSettings? settings = null) =>
        new(settings ?? Settings(), store, escalations, new Projection(_data.Path, _log), _log);

    private SessionPolicy Policy(PanelSettings? settings = null) => PolicyOver(Store(), new Escalations(_data.Path), settings);

    private static EscalationQuestion Card(string id, string kind) =>
        new(id, SessionId, Repo, Branch, "The plan gate needs your decision", "The plan gate needs your decision", "en", string.Empty, [], DateTime.UtcNow.ToString("O"))
        {
            Kind = kind,
            Decisions = kind == EscalationKinds.Notice ? EscalationDecisions.Notice : EscalationDecisions.Question,
        };

    private static RoundRecord Round(int number, string verdict) =>
        new("PlanReview", number, verdict, 1, "all 1 reviewers answered", DateTime.UtcNow)
        {
            StartedUtc = DateTime.UtcNow.AddMinutes(-4),
            Subject = $"plan round {number}",
            ReviewerStates = [new ReviewerState("codex", "Plan", ReviewerState.Done, 1)],
        };

    /// <summary>A session a build before policy auto left held: no <c>autonomous</c> member, the notice recorded (or nothing, when none is).</summary>
    private PersistedSession Held(IReadOnlyList<string> hold, IReadOnlyList<RoundRecord>? rounds = null) =>
        new(
            new SessionState(SessionId, Repo, Branch, new PanelConfig(roles: null, autonomous: null))
            {
                HumanGate = true,
                HoldQuestions = [.. hold],
                RoundsRunThisStage = 1,
            },
            [.. rounds ?? [Round(1, "call_human")]]);

    /// <summary>Held, planted, and its cards written: the first is the notice, the others <c>ask_human</c> questions.</summary>
    private PersistedSession Planted(params string[] hold)
    {
        var session = Held(hold);
        Store().Save(session);
        var escalations = new Escalations(_data.Path);
        foreach (var (id, index) in hold.Select((id, index) => (id, index)))
        {
            escalations.Notify(Card(id, index == 0 ? EscalationKinds.Notice : EscalationKinds.Question));
        }

        var loaded = Store().Load(Repo, Branch)!;
        loaded.State.Config.PredatesAuto.Should().BeTrue("the fixture is read as a file from before policy auto");

        return loaded;
    }

    private string SessionFile => Store().FileFor(Repo, Branch);

    private string FileText() => File.ReadAllText(SessionFile);

    private EscalationQuestion Read(string id) => new Escalations(_data.Path).Read(id)!;

    private void Answer(string id, string decision, string words = "my words")
    {
        File.WriteAllText(
            new Escalations(_data.Path).AnswerPath(id),
            JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["id"] = id,
                ["answer"] = words,
                ["decision"] = decision,
                ["answeredUtc"] = DateTime.UtcNow.ToString("O"),
            }));
    }

    private IReadOnlyList<string> Lines(LogEventLevel level) =>
        [.. _sink.Events.Zip(_sink.Lines).Where(e => e.First.Level == level).Select(e => e.Second)];

    private IReadOnlyList<string> Said(string text) => [.. _sink.Lines.Where(l => l.Contains(text, StringComparison.Ordinal))];

    private static Escalations FailingFor(string id, string path, Action<string>? seen = null) =>
        new(path) { Writer = (file, text) => Write(file, text, id, seen) };

    private static void Write(string file, string text, string failing, Action<string>? seen)
    {
        if (Path.GetFileName(file) == failing + ".json")
        {
            throw new IOException("the disk refused the write");
        }

        seen?.Invoke(file);
        AtomicJson.Write(file, text);
    }

    // ---------- T30, T31: the order, and a failure of either half ----------

    [Fact]
    public void TheNoticesAreWithdrawnBeforeTheSessionIsSaved_AndAFailedSaveIsRetried()
    {
        var session = Planted("n-1");
        var failing = new SessionStore(_data.Path) { FileWriter = (_, _) => throw new IOException("the disk is full") };

        var first = PolicyOver(failing, new Escalations(_data.Path)).Released(session);

        first.Session.Should().BeSameAs(session);
        first.Session.State.HumanGate.Should().BeTrue();
        first.Session.State.HoldQuestions.Should().Equal("n-1");
        first.NotWritten.Should().Be(LegacyHold.SessionNotSaved);
        var onDisk = Store().Load(Repo, Branch)!;
        onDisk.State.HumanGate.Should().BeTrue("the save failed, so the hold stands");
        onDisk.Rounds[^1].Note.Should().BeEmpty();
        var notice = Read("n-1");
        notice.Status.Should().Be(EscalationStatuses.Expired, "the withdrawal came BEFORE the save");
        var t1 = notice.ExpiredUtc;
        Lines(LogEventLevel.Warning).Should().ContainSingle(l => l.Contains("could not save the release", StringComparison.Ordinal));

        var second = Policy().Released(Store().Load(Repo, Branch)!);

        second.NotWritten.Should().BeEmpty();
        var released = Store().Load(Repo, Branch)!;
        released.State.HumanGate.Should().BeFalse();
        Read("n-1").ExpiredUtc.Should().Be(t1, "the second pass rewrites no card");
        Lines(LogEventLevel.Warning).Should().Contain(l => l.Contains("auto released the", StringComparison.Ordinal));
    }

    [Fact]
    public void ANoticeThatCannotBeWithdrawn_KeepsTheHold_AndTheNextLoadReleasesIt()
    {
        var session = Planted("n-1", "q-2");
        var bytes = FileText();

        var first = PolicyOver(Store(), FailingFor("n-1", _data.Path)).Released(session);

        first.Session.Should().BeSameAs(session);
        first.NotWritten.Should().Be(LegacyHold.NoticeNotWithdrawn);
        FileText().Should().Be(bytes, "nothing is saved");
        Store().Load(Repo, Branch)!.State.HoldQuestions.Should().Equal("n-1", "q-2");
        Read("n-1").Status.Should().Be(EscalationStatuses.Open);
        Read("q-2").Status.Should().Be(EscalationStatuses.Expired, "every notice is tried: no short circuit");
        var q2 = Read("q-2").ExpiredUtc;
        Lines(LogEventLevel.Warning).Should().ContainSingle(l => l.Contains("n-1", StringComparison.Ordinal) && l.Contains("could not withdraw", StringComparison.Ordinal));
        Said("auto released").Should().BeEmpty();

        var second = Policy().Released(Store().Load(Repo, Branch)!);

        second.NotWritten.Should().BeEmpty();
        Store().Load(Repo, Branch)!.State.HumanGate.Should().BeFalse();
        Read("n-1").Status.Should().Be(EscalationStatuses.Expired);
        Read("q-2").ExpiredUtc.Should().Be(q2, "an expired card is not rewritten");
    }

    // ---------- T32: any other session is left alone ----------

    [Fact]
    public void Released_WritesNothing_ForAnyOtherSession()
    {
        var beforeAutoUnderHuman = Planted("n-1");
        var humanSettings = new PanelSettings { DataDir = _data.Path, Rounds = PanelConfig.Uniform(1, 0, StagePolicy.Human) };
        var bytes = FileText();

        var human = Policy(humanSettings).Released(beforeAutoUnderHuman);

        human.Session.Should().BeSameAs(beforeAutoUnderHuman);
        human.NotWritten.Should().BeEmpty();

        var raisedHere = new PersistedSession(
            beforeAutoUnderHuman.State with { Config = PanelConfig.Uniform(1, 0, StagePolicy.Human) }, beforeAutoUnderHuman.Rounds);
        var thisBuilds = Policy().Released(raisedHere);

        thisBuilds.Session.Should().BeSameAs(raisedHere);
        thisBuilds.NotWritten.Should().BeEmpty();

        var notHeld = new PersistedSession(beforeAutoUnderHuman.State with { HumanGate = false, HoldQuestions = [] }, beforeAutoUnderHuman.Rounds);
        var idle = Policy().Released(notHeld);

        idle.Session.Should().BeSameAs(notHeld);
        idle.NotWritten.Should().BeEmpty();
        FileText().Should().Be(bytes);
        Read("n-1").Status.Should().Be(EscalationStatuses.Open);
        Said("auto released").Should().BeEmpty();
        Lines(LogEventLevel.Warning).Should().BeEmpty();
    }

    // ---------- T33: the claim ----------

    [Fact]
    public void CurrentUnclaimed_ReleasesUnderTheSessionsClaim_AndReadsAgainUnderIt()
    {
        var stale = Planted("n-1");
        var bytes = FileText();

        using (var claim = SessionClaim.TryTake(_data.Path, Repo, Branch))
        {
            claim.Should().NotBeNull();
            var busy = Policy().CurrentUnclaimed(stale, Repo, At)!.Value;

            busy.NotWritten.Should().Be(LegacyHold.ClaimBusy);
            busy.Session.State.HumanGate.Should().BeTrue();
            FileText().Should().Be(bytes, "a busy claim writes nothing");
            Read("n-1").Status.Should().Be(EscalationStatuses.Open);
        }

        var free = Policy().CurrentUnclaimed(stale, Repo, At)!.Value;

        free.NotWritten.Should().BeEmpty();
        free.Session.State.HumanGate.Should().BeFalse();
        Store().Load(Repo, Branch)!.State.HumanGate.Should().BeFalse();
        using (var afterwards = SessionClaim.TryTake(_data.Path, Repo, Branch))
        {
            afterwards.Should().NotBeNull("the claim taken for the release is let go");
        }

        // A STALE held copy, the file on disk already released — a resolve saved in between.
        var settled = FileText();
        var linesBefore = Said("auto released").Count;

        var again = Policy().CurrentUnclaimed(stale, Repo, At)!.Value;

        again.NotWritten.Should().BeEmpty();
        again.Session.State.HumanGate.Should().BeFalse("the disk's session came back, not the stale copy");
        FileText().Should().Be(settled, "the stale copy was not saved over what is on disk");
        Said("auto released").Count.Should().Be(linesBefore);
    }

    // ---------- T49: the person's answer wins ----------

    [Theory]
    [InlineData("n-1", "continue")]
    [InlineData("n-1", "fix")]
    [InlineData("n-1", "discuss")]
    [InlineData("n-1", "proceed")]
    [InlineData("n-1", "")]
    [InlineData("q-2", "")]
    public void AnAnswerOnFile_IsNotOverriddenByARelease(string answered, string decision)
    {
        var session = Planted("n-1", "q-2");
        Answer(answered, decision);
        var bytes = FileText();

        var released = Policy().Released(session);

        released.Session.Should().BeSameAs(session);
        released.NotWritten.Should().BeEmpty();
        AssertUntouched(bytes);

        // The unclaimed door decides the same before it takes any claim: a busy claim does not turn into a reason.
        using var claim = SessionClaim.TryTake(_data.Path, Repo, Branch);
        var unclaimed = Policy().CurrentUnclaimed(session, Repo, At)!.Value;

        unclaimed.NotWritten.Should().BeEmpty("it took no claim, so it cannot find one busy");
        unclaimed.Session.State.HumanGate.Should().BeTrue();
        AssertUntouched(bytes);
    }

    private void AssertUntouched(string bytes)
    {
        FileText().Should().Be(bytes, "no marker and no note");
        Read("n-1").Status.Should().Be(EscalationStatuses.Open);
        Read("q-2").Status.Should().Be(EscalationStatuses.Open);
        Said("auto released").Should().BeEmpty();
    }

    // ---------- T67: a restore that fails ----------

    [Fact]
    public void ARestoreThatFails_IsAWarning_AndTheNextLoadOffersTheNoticeAgain()
    {
        var session = Planted("n-1", "q-2", "q-0");
        var t0 = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        new Escalations(_data.Path).Expire("q-0", t0).Should().BeTrue();
        var q0 = Read("q-0").ExpiredUtc;
        var bytes = FileText();
        var writes = 0;
        var racing = new Escalations(_data.Path)
        {
            Writer = (file, text) =>
            {
                var number = Interlocked.Increment(ref writes);
                if (number > 2)
                {
                    throw new IOException("the disk refused the write");
                }

                AtomicJson.Write(file, text);
                if (number == 1)
                {
                    Answer("n-1", "discuss");
                }
            },
        };

        var first = PolicyOver(Store(), racing).Released(session);

        first.Session.Should().BeSameAs(session);
        first.NotWritten.Should().BeEmpty("the person's answer stands, so the release has nothing to say");
        Read("n-1").Status.Should().Be(EscalationStatuses.Expired);
        Read("q-2").Status.Should().Be(EscalationStatuses.Expired);
        Lines(LogEventLevel.Warning).Should().ContainSingle(l =>
            l.Contains("could not offer", StringComparison.Ordinal) && l.Contains("n-1", StringComparison.Ordinal) && l.Contains("q-2", StringComparison.Ordinal));
        FileText().Should().Be(bytes, "nothing is saved");

        var writesAfter = new List<string>();
        var working = new Escalations(_data.Path) { Writer = (file, text) => { writesAfter.Add(file); AtomicJson.Write(file, text); } };
        var policy = PolicyOver(Store(), working);

        policy.Released(session).NotWritten.Should().BeEmpty();

        Read("n-1").Status.Should().Be(EscalationStatuses.Open, "the next deciding load offers the notice again");
        Read("n-1").ExpiredUtc.Should().BeEmpty();
        Lines(LogEventLevel.Information).Should().ContainSingle(l => l.Contains("offered again", StringComparison.Ordinal) && !l.Contains("while policy auto", StringComparison.Ordinal));
        Read("q-2").Status.Should().Be(EscalationStatuses.Expired, "a question card is not retried");
        Read("q-0").ExpiredUtc.Should().Be(q0, "a card that ran out its own wait stays expired");
        FileText().Should().Be(bytes);
        Said("auto released").Should().BeEmpty();
        var firstRetry = writesAfter.Count;

        policy.Released(session).NotWritten.Should().BeEmpty();

        writesAfter.Count.Should().Be(firstRetry, "the third call writes nothing at all");
        Read("q-0").Status.Should().Be(EscalationStatuses.Expired);
    }

    // ---------- T51, T53: the note ----------

    private RoundsDb Db() => RoundsDb.Open(_data.Path, Serilog.Core.Logger.None)!;

    private Dictionary<string, string> RowOf(string stage, int number)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_data.Path, RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT * FROM rounds WHERE session_id = $s AND stage = $stage AND number = $n";
        read.Parameters.AddWithValue("$s", SessionId);
        read.Parameters.AddWithValue("$stage", stage);
        read.Parameters.AddWithValue("$n", number);
        using var reader = read.ExecuteReader();
        var row = new Dictionary<string, string>();
        if (reader.Read())
        {
            for (var column = 0; column < reader.FieldCount; column++)
            {
                row[reader.GetName(column)] = reader.IsDBNull(column) ? "<null>" : reader.GetValue(column).ToString() ?? string.Empty;
            }
        }

        return row;
    }

    private List<string> Scalars(string sql)
    {
        using var db = new SqliteConnection($"Data Source={Path.Combine(_data.Path, RoundsDb.FileName)}");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var reader = read.ExecuteReader();
        var found = new List<string>();
        while (reader.Read())
        {
            found.Add(reader.IsDBNull(0) ? "<null>" : reader.GetValue(0).ToString() ?? string.Empty);
        }

        return found;
    }

    [Fact]
    public void TheReleaseIsNotedOnTheHeldRound_InTheFileAndInTheDatabase()
    {
        var finding = new Finding(Severity.Blocking, Category.Reliability, "src/Panel.cs", 40, "the poll has no timeout", "a hung peer hangs it", "add one", ["codex"]);
        var rounds = new[] { Round(1, "revise"), Round(2, "call_human") };
        var held = Held(["n-1"], rounds);
        Store().Save(held);
        new Escalations(_data.Path).Notify(Card("n-1", EscalationKinds.Notice));
        using (var db = Db())
        {
            foreach (var round in rounds)
            {
                db.RecordRound(
                    held.State, round, round.Number == 2 ? [finding] : [],
                    new RoundContext(PlanText: "PLAN", HeadSha: $"sha{round.Number}", Caller: "claude"));
            }

            db.RecordDecisions(SessionId, "PlanReview", 2, [DecisionAt.Reject([finding], 0, "the timeout lives in the caller")!]);
        }

        var roundOneBefore = RowOf("PlanReview", 1);
        var roundTwoBefore = RowOf("PlanReview", 2);
        var reviewersBefore = Scalars("SELECT provider || role || status FROM reviewers ORDER BY round_id, provider, role");
        var resolutionBefore = Scalars("SELECT resolution || reason FROM findings");

        var released = Policy().Released(Store().Load(Repo, Branch)!);

        released.NotWritten.Should().BeEmpty();
        var onDisk = Store().Load(Repo, Branch)!;
        onDisk.Rounds[^1].Note.Should().Be(LegacyHold.Note);
        onDisk.Rounds[^1].Verdict.Should().Be("call_human", "the verdict is what it was");
        onDisk.Rounds[0].Note.Should().BeEmpty();
        RowOf("PlanReview", 2)["note"].Should().Be(LegacyHold.Note);
        RowOf("PlanReview", 1).Should().Equal(roundOneBefore, "round 1 is not touched");
        foreach (var (column, value) in roundTwoBefore.Where(c => c.Key != "note"))
        {
            RowOf("PlanReview", 2)[column].Should().Be(value, $"column {column} of the held round is what it was");
        }

        roundTwoBefore["plan_text"].Should().Be("PLAN", "the fixture really recorded a plan text, so its survival means something");
        roundTwoBefore["rejected"].Should().Be("1");
        Scalars("SELECT provider || role || status FROM reviewers ORDER BY round_id, provider, role").Should().Equal(reviewersBefore);
        Scalars("SELECT resolution || reason FROM findings").Should().Equal(resolutionBefore).And.NotBeEmpty();
    }

    [Fact]
    public void ARelease_NeedsNeitherARoundNorTheDatabase()
    {
        // (a) no round at all.
        var noRound = Held(["n-1"], []);
        Store().Save(noRound);
        new Escalations(_data.Path).Notify(Card("n-1", EscalationKinds.Notice));

        var first = Policy().Released(Store().Load(Repo, Branch)!);

        first.NotWritten.Should().BeEmpty();
        Store().Load(Repo, Branch)!.State.HumanGate.Should().BeFalse();

        // (b) a round, and the database's own name taken by a folder.
        Directory.CreateDirectory(Path.Combine(_data.Path, RoundsDb.FileName));
        Store().Save(Held(["n-1"]));
        new Escalations(_data.Path).Restore("n-1");
        _sink.Events.Count.Should().BeGreaterThan(0, "the first release logged");
        var before = Said("auto released").Count;

        var second = Policy().Released(Store().Load(Repo, Branch)!);

        second.NotWritten.Should().BeEmpty();
        var onDisk = Store().Load(Repo, Branch)!;
        onDisk.State.HumanGate.Should().BeFalse();
        onDisk.Rounds[^1].Note.Should().Be(LegacyHold.Note);
        Said("auto released").Count.Should().Be(before + 1, "the release's own Warning is written whatever the projection said");
    }

    // ---------- T63: a failed read ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFailedRead_ReleasesNothing(bool recorded)
    {
        var session = recorded ? Planted("n-1") : PlantedNone();
        var bytes = FileText();

        using (var claim = SessionClaim.TryTake(_data.Path, Repo, Branch))
        {
            var unread = Settings(unread: true);
            var direct = Policy(unread).Released(session);
            var unclaimed = Policy(unread).CurrentUnclaimed(session, Repo, At)!.Value;

            foreach (var release in new[] { direct, unclaimed })
            {
                release.Session.Should().BeSameAs(session);
                release.NotWritten.Should().Be(LegacyHold.SettingsUnread, "a claim this call never tried cannot be the reason");
            }
        }

        FileText().Should().Be(bytes);
        if (recorded)
        {
            Read("n-1").Status.Should().Be(EscalationStatuses.Open);
        }

        Lines(LogEventLevel.Warning).Should().OnlyContain(l => l.Contains("could not be read", StringComparison.Ordinal) && !l.Contains("auto released", StringComparison.Ordinal));
        Lines(LogEventLevel.Warning).Should().NotBeEmpty();

        Policy().Released(session).NotWritten.Should().BeEmpty();
        Store().Load(Repo, Branch)!.State.HumanGate.Should().BeFalse("the read succeeded, so the hold is released");
    }

    private PersistedSession PlantedNone()
    {
        Store().Save(Held([]));

        return Store().Load(Repo, Branch)!;
    }
}
