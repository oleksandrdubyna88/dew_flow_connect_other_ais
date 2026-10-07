using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The idle-CPU defect of 2026-10-06 (<c>todo/PLAN_an_idle_server_is_idle_and_starts_at_once.md</c>, D1), at the layer
/// a server's one-minute beat actually runs: <see cref="PanelService.SweepEscalations"/> over a data directory with
/// sessions and question cards. The signal is how many times the session files were READ — deterministic, where a
/// CPU number would not be.
/// </summary>
public sealed class AnIdleServerReadsNothingTests : IDisposable
{
    private static readonly TimeSpan Past = EscalationRetention.Retention + TimeSpan.FromHours(1);

    private readonly string _data = Directory.CreateTempSubdirectory("coai-idle-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException) { }
    }

    [Fact]
    public void TenIdleBeats_ReadNoSessionFile_WhenNoCardIsDue()
    {
        Sessions(30);
        for (var i = 0; i < 20; i++)
        {
            Asked($"young{i}", TimeSpan.FromHours(i + 1));
        }

        var service = Service();
        var before = service.Store.HeldQuestionParses; // the constructor's startup sweep, whatever it read
        for (var beat = 0; beat < 10; beat++)
        {
            service.SweepEscalations();
        }

        service.Store.HeldQuestionParses.Should().Be(before, "no card is due, so no beat needs to know what the sessions hold");
    }

    /// <summary>
    /// A question held past its seven days is due on EVERY beat for as long as the hold stands — the case the own plan
    /// review named: without the directory stamp each of those beats would read all sessions again.
    /// </summary>
    [Fact]
    public void AHeldCardPastItsSevenDays_ReadsTheSessionsOnce_NotOncePerBeat_AndStaysKept()
    {
        Sessions(30, holding: "kept");
        Asked("kept", Past);
        Asked("gone", Past);

        var service = Service(); // its constructor sweeps once: "gone" goes there, "kept" stays
        var escalations = new Escalations(_data);
        File.Exists(escalations.QuestionPath("gone")).Should().BeFalse("nobody holds it and it is past its seven days");
        var before = service.Store.HeldQuestionParses;

        for (var beat = 0; beat < 10; beat++)
        {
            service.SweepEscalations();
        }

        File.Exists(escalations.QuestionPath("kept")).Should().BeTrue("a live session's hold is bound to this id");
        service.Store.HeldQuestionParses.Should().Be(before, "the sessions did not change, so what they hold is known already");
    }

    [Fact]
    public void ASessionThatChanges_IsReadAgain_AndItsReleasedHoldLetsTheCardGo()
    {
        var store = new SessionStore(_data);
        var holding = Saved(store, "b-hold", holding: "q1");
        Asked("q1", Past);
        var service = Service();
        var escalations = new Escalations(_data);
        File.Exists(escalations.QuestionPath("q1")).Should().BeTrue();

        store.Save(holding with { State = holding.State with { HoldQuestions = [] } });
        service.SweepEscalations();

        File.Exists(escalations.QuestionPath("q1")).Should().BeFalse("the hold was released, so the card goes on the next beat");
    }

    [Fact]
    public void TheHeldSet_CarriesHoldsAndRequests_FromEverySession()
    {
        var store = new SessionStore(_data);
        Saved(store, "b1", holding: "h1");
        store.Save(new PersistedSession(
            new SessionState("s-req", "D:/repo", "b2", PanelConfig.Uniform(3, 2)) { RequestQuestions = ["r1"] }, []));

        var held = store.HeldQuestions();

        held.Complete.Should().BeTrue();
        held.Ids.Should().BeEquivalentTo(["h1", "r1"]);
        held.MayHold("other").Should().BeFalse();
    }

    /// <summary>
    /// The code round's finding (gemini, 2026-10-07): one active session — written on every round — moved the whole
    /// directory's stamp, and every beat with a due card then parsed EVERY session again. Each file is now cached under
    /// its own write time and length, and only a file that changed is parsed.
    /// </summary>
    [Fact]
    public void OneSessionThatChanges_IsTheOnlyOneParsedAgain()
    {
        var store = new SessionStore(_data);
        Sessions(30, holding: "kept");
        var active = Saved(store, "b-active", holding: string.Empty);
        Asked("kept", Past);
        var service = Service();
        var before = service.Store.HeldQuestionParses;

        store.Save(active with { State = active.State with { Branch = "b-active", SessionId = "s-active-2" } });
        service.SweepEscalations();

        service.Store.HeldQuestionParses.Should().Be(before + 1, "one session changed, so one is parsed — not all thirty-one");
        File.Exists(new Escalations(_data).QuestionPath("kept")).Should().BeTrue();
    }

    /// <summary>
    /// A session that cannot be read may hold any id, so the set stays incomplete (fail closed) — but a torn file that
    /// STAYS torn is not parsed again on every beat (own review of the branch, 2026-10-07); it is read again when it changes.
    /// </summary>
    [Fact]
    public void AnUnreadableSession_KeepsTheSetIncomplete_UntilItChanges_WithoutBeingParsedEveryBeat()
    {
        var store = new SessionStore(_data);
        Saved(store, "b1", holding: "h1");
        var torn = Path.Combine(_data, "sessions", "session-torn.json");
        File.WriteAllText(torn, "{ half");

        var first = store.HeldQuestions();
        var second = store.HeldQuestions();

        first.Complete.Should().BeFalse("a session that cannot be read may hold anything");
        second.Complete.Should().BeFalse();
        second.MayHold("anything").Should().BeTrue();
        store.HeldQuestionParses.Should().Be(2, "both files were parsed once; nothing changed, so nothing is parsed again");

        File.Delete(torn);
        store.HeldQuestions().Complete.Should().BeTrue("the unreadable session is gone, so what is held is known again");
    }

    /// <summary>
    /// The cadence consultation (codex, 2026-10-07): a cached entry is trusted for as long as its file's write time and
    /// length stay put, and a save that keeps both — same length, inside one timestamp tick — would leave a NEW hold
    /// unseen for good; seven days later its card could go. Every cached entry is therefore parsed again once it is older
    /// than SessionHolds.Recheck, whatever its stamp says.
    /// </summary>
    [Fact]
    public void ACachedSession_IsReadAgainAfterTheRecheck_EvenWhenItsStampDidNotMove()
    {
        var store = new SessionStore(_data);
        var session = Saved(store, "b1", holding: "q1");
        var file = Directory.EnumerateFiles(Path.Combine(_data, "sessions"), "session-*.json").Single();
        var clock = new MovableClock(DateTimeOffset.UtcNow);
        var holds = new SessionHolds(Path.Combine(_data, "sessions"), store.TryReadForTests, clock);
        holds.Read().Ids.Should().BeEquivalentTo(["q1"]);

        var stamp = File.GetLastWriteTimeUtc(file);
        store.Save(session with { State = session.State with { HoldQuestions = ["q2"] } }); // same length: q1 -> q2
        File.SetLastWriteTimeUtc(file, stamp); // and the same write time: the collision a stat cannot see

        holds.Read().Ids.Should().BeEquivalentTo(["q1"], "inside the recheck the cache is trusted");
        clock.Advance(SessionHolds.Recheck + TimeSpan.FromSeconds(1));
        holds.Read().Ids.Should().BeEquivalentTo(["q2"], "past the recheck every session is parsed again, so a missed hold is seen");
    }

    private PanelService Service()
    {
        var settings = new PanelSettings
        {
            DataDir = _data,
            CodeWorkspace = "none",
            Providers = [new ProviderSettings("local") { Enabled = true, Runtime = "local", Model = "m" }],
        };

        return new PanelService(settings, VaultKeys.None("no vault"), default,
            new Runners.Processes.ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);
    }

    private void Sessions(int count, string holding = "")
    {
        var store = new SessionStore(_data);
        for (var i = 0; i < count; i++)
        {
            Saved(store, $"b{i}", i == count / 2 ? holding : string.Empty);
        }
    }

    private static PersistedSession Saved(SessionStore store, string branch, string holding)
    {
        var session = new PersistedSession(
            new SessionState($"s-{branch}", "D:/repo", branch, PanelConfig.Uniform(3, 2))
            {
                HoldQuestions = holding.Length > 0 ? [holding] : [],
            },
            []);
        store.Save(session);

        return session;
    }

    private void Asked(string id, TimeSpan ago) => new Escalations(_data).Notify(new EscalationQuestion(
        id, "s-1", "D:/repo", "main", "Ship?", "Ship?", "en", string.Empty, [], (DateTime.UtcNow - ago).ToString("O")));
}
