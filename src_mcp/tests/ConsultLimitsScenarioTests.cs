using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The consultant's three limits, each held for every kind it applies to, and the idle close happening
/// while the server RUNS (<c>todo/PLAN_consult_limits_kinds_and_help.md</c>, story 1).
/// </summary>
/// <remarks>
/// <para>Turns per consultation and the idle close apply to every kind. Calls per session applies only to
/// a stuck consultation: an ordered one (a group of epics, a risky piece) is bounded by the gate instead.
/// Before this story the idle close ran only when a <see cref="PanelService"/> was BUILT, so a consultation
/// idle past its budget read <c>open</c> until the server restarted.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultLimitsScenarioTests : ConsultScenarioBase
{
    private static string Id(JsonElement reply)
    {
        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());

        return reply.GetProperty("consultationId").GetString()!;
    }

    /// <summary>Moves a record's last activity into the past, as the clock would.</summary>
    private static void Backdate(ConsultationStore store, string id, TimeSpan by)
    {
        var record = store.Read(id)!;
        store.Write(record with { UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow - by) });
    }

    [Fact]
    public async Task AFollowUpAfterTheIdleTime_IsRefusedAndSaysSo()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        var id = Id(await Consult(service, "the parser returns 3 where 4 is expected"));
        Backdate(service.Consultations, id, TimeSpan.FromMinutes(20));

        var follow = await Consult(service, "I checked the separator: it is counted", id);

        Refusal(follow).Should().Contain("sat idle for more than 15 minutes");
    }

    [Theory]
    [InlineData("cadence", "1-3")]
    [InlineData("risk", "5/5.2")]
    public async Task AnOrderedConsultation_IsRefusedAFollowUpPastItsTurnCap(string kind, string epics)
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(turns: 1);
        var id = Id(await ConsultFor(service, kind, epics));

        Answer("0198-second", Advice);
        var follow = await ConsultFor(service, kind, epics, "I checked it against the code: it holds", id);

        Refusal(follow).Should().Contain("all 1 of its turns are used", $"the turn cap applies to a {kind} consultation too");
    }

    [Theory]
    [InlineData("cadence", "1-3")]
    [InlineData("risk", "5/5.2")]
    public async Task AFollowUpOfAnOrderedConsultation_SpendsNoStuckBudget(string kind, string epics)
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(callsPerSession: 1);
        Id(await Consult(service, "the parser returns 3 where 4 is expected")); // the one stuck call the cap allows
        Answer("0198-second", Advice);
        var id = Id(await ConsultFor(service, kind, epics));

        Answer("0198-third", Advice);
        var follow = await ConsultFor(service, kind, epics, "I checked it against the code: it holds", id);

        follow.TryGetProperty("error", out _).Should().BeFalse($"a {kind} follow-up is not a stuck call: {follow}");
    }

    [Fact]
    public async Task AnIdleConsultation_LapsesInARunningServer_WithoutARestart()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        var id = Id(await Consult(service, "the parser returns 3 where 4 is expected"));
        Backdate(service.Consultations, id, TimeSpan.FromMinutes(20));

        service.SweepConsultations();

        var record = service.Consultations.Read(id)!;
        record.Status.Should().Be(ConsultationStatuses.Closed, "the idle close is a limit, not something a restart does");
        record.Outcome.Should().Be(ConsultationOutcomes.Lapsed);
    }
}

/// <summary>
/// The loop that runs the sweep while the server serves, through the real host — so a loop that never
/// starts, or stops at a settings reload, is a red test (the plan round, codex).
/// </summary>
public sealed class ConsultationSweeperTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("coai-sweeper-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private PanelServiceHost Host()
    {
        var env = new Dictionary<string, string?> { ["COAI_DATA_DIR"] = _dir, ["COAI_PROVIDERS"] = "codex" };

        return new PanelServiceHost(
            name => env.GetValueOrDefault(name),
            VaultKeys.None("no vault in this test"),
            default,
            new Runners.Processes.ProcessLauncher(),
            Logger.None,
            Noticing.None);
    }

    /// <summary>An open consultation whose last activity was <paramref name="minutesAgo"/> minutes ago.</summary>
    private static string Idle(ConsultationStore store, int minutesAgo)
    {
        var record = new ConsultationRecord(ConsultationStore.NewId(), "caller-1", CallerIdentity.Claude, "no-session",
            Path.Combine(Path.GetTempPath(), "coai-no-such-repo"), "main", "0123abc", "codex", "gpt-5.6", "codex", "vendorRemembers", 5,
            ConsultationStore.Stamp(DateTime.UtcNow.AddMinutes(-minutesAgo)))
        {
            Status = ConsultationStatuses.Open,
            UpdatedUtc = ConsultationStore.Stamp(DateTime.UtcNow.AddMinutes(-minutesAgo)),
        };
        store.Write(record);

        return record.Id;
    }

    private static async Task Lapses(Func<ConsultationStore> store, string id)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < deadline; await Task.Delay(50, TestContext.Current.CancellationToken))
        {
            if (store().Read(id)?.Status == ConsultationStatuses.Closed)
            {
                return;
            }
        }

        store().Read(id)!.Status.Should().Be(ConsultationStatuses.Closed, "the loop sweeps while the server serves");
    }

    [Fact]
    public async Task TheLoop_LapsesAnIdleConsultation_AndKeepsDoingSoAfterASettingsReload()
    {
        var host = Host();
        var first = Idle(host.Current.Consultations, minutesAgo: 20);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var loop = ConsultationSweeper.RunAsync(() => host.Current, TimeSpan.FromMilliseconds(50), Logger.None, stop.Token);

        await Lapses(() => host.Current.Consultations, first);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), """{ "COAI_CONSULT_TURNS": "4" }""");
        host.Current.Settings.ConsultTurns.Should().Be(4, "the reload happened");
        var second = Idle(host.Current.Consultations, minutesAgo: 20);
        await Lapses(() => host.Current.Consultations, second);

        await stop.CancelAsync();
        await loop;
    }

    [Fact]
    public async Task TheLoop_OutlivesASweepThatThrows()
    {
        var calls = 0;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var host = Host();

        var loop = ConsultationSweeper.RunAsync(
            () => ++calls == 1 ? throw new IOException("the share went away") : host.Current,
            TimeSpan.FromMilliseconds(20), Logger.None, stop.Token);
        for (var deadline = DateTime.UtcNow.AddSeconds(10); calls < 3 && DateTime.UtcNow < deadline; await Task.Delay(20, TestContext.Current.CancellationToken)) { }

        calls.Should().BeGreaterThanOrEqualTo(3, "one failed sweep is not the last one");
        await stop.CancelAsync();
        await loop;
    }
}
