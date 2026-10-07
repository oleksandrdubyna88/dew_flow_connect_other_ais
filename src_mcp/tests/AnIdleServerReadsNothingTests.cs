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
        var before = service.Store.HeldQuestionReads; // the constructor's startup sweep, whatever it read
        for (var beat = 0; beat < 10; beat++)
        {
            service.SweepEscalations();
        }

        service.Store.HeldQuestionReads.Should().Be(before, "no card is due, so no beat needs to know what the sessions hold");
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
        var before = service.Store.HeldQuestionReads;

        for (var beat = 0; beat < 10; beat++)
        {
            service.SweepEscalations();
        }

        File.Exists(escalations.QuestionPath("kept")).Should().BeTrue("a live session's hold is bound to this id");
        service.Store.HeldQuestionReads.Should().Be(before, "the sessions did not change, so what they hold is known already");
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

    [Fact]
    public void AnUnreadableSession_MakesTheSetIncomplete_AndItIsNotCached()
    {
        var store = new SessionStore(_data);
        Saved(store, "b1", holding: "h1");
        File.WriteAllText(Path.Combine(_data, "sessions", "session-torn.json"), "{ half");

        var held = store.HeldQuestions();
        store.HeldQuestions();

        held.Complete.Should().BeFalse("a session that cannot be read may hold anything");
        held.MayHold("anything").Should().BeTrue();
        store.HeldQuestionReads.Should().Be(2, "an incomplete answer is not kept: the next beat tries again");
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
