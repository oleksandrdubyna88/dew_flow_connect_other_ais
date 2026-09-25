using System.Text.Json;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A consultation that is FOR something — a group of epics, a risky piece — over the whole flow and the
/// fake CLI (<c>todo/PLAN_consult_on_a_cadence.md</c>, epic 2, story 2.4).
/// </summary>
/// <remarks>
/// <para>The cadence makes consultations the gate ORDERS; the stuck budget was sized for the ones an
/// agent asks for itself (decision 11). So an ordered one spends none of it — and, so that "none" is not
/// "unlimited", a group or an item already satisfied cannot be consulted on again (D5).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultKindsScenarioTests : ConsultScenarioBase
{
    private const string Plan = "todo/PLAN_x.md";

    private async Task<JsonElement> ConsultFor(PanelService service, string kind, string epics, string problem = "is this group right?", string id = "", string plan = Plan) =>
        JsonDocument.Parse(await service.ConsultAsync(_repo, problem, "[]", id, kind, plan, epics, TestContext.Current.CancellationToken)).RootElement;

    private static string Id(JsonElement reply)
    {
        reply.TryGetProperty("error", out _).Should().BeFalse(reply.ToString());

        return reply.GetProperty("consultationId").GetString()!;
    }

    [Fact]
    public async Task ACadenceConsultation_SpendsNoStuckBudget_AndAStuckOneStillDoes()
    {
        var service = Service(callsPerSession: 1);
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");

        Id(await ConsultFor(service, "cadence", "1-3"));
        Answer("0198-second", Advice);
        Id(await ConsultFor(service, "cadence", "4-6"));
        Answer("0198-third", Advice);
        Id(await ConsultFor(service, "risk", "5/5.2", "is the migration right?"));

        Answer("0198-fourth", Advice);
        Id(await Consult(service, "the parser returns 3 where 4 is expected"));
        Answer("0198-fifth", Advice);
        Refusal(await Consult(service, "and now the lock")).Should().Contain("consult calls, the cap",
            "the one stuck call the cap allows was the fourth; the three ordered ones never counted");
    }

    [Theory]
    [InlineData("cadence", "", "no epics")]
    [InlineData("cadence", "5-4", "'5-4'")]
    [InlineData("urgent", "1-3", "'urgent'")]
    public async Task ABadAim_IsRefusedBeforeAnythingRuns(string kind, string epics, string named)
    {
        var reply = await ConsultFor(Service(), kind, epics);

        Refusal(reply).Should().Contain(named);
        Directory.Exists(Path.Combine(_data, "consultations")).Should().BeFalse("nothing was recorded, nothing launched");
    }

    [Fact]
    public async Task TheRecordSaysWhatTheConsultationWasFor()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();

        var id = Id(await ConsultFor(service, "cadence", "04-06", plan: @"todo\PLAN_x.md"));

        var record = service.Consultations.Read(id)!;
        record.Kind.Should().Be("cadence");
        record.Plan.Should().Be("todo/PLAN_x.md");
        record.Epics.Should().Be("4-6", "the canonical range the gate matches on");
        record.RepoId.Should().NotBeEmpty().And.EndWith("/.git");
    }

    [Fact]
    public async Task AGroupAlreadyConsultedAndClosed_IsNotConsultedAgain()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        var id = Id(await ConsultFor(service, "cadence", "1-3"));
        (await service.CloseConsultAsync(_repo, id, "solved", "took two of three", TestContext.Current.CancellationToken)).Should().NotContain("error");

        Answer("0198-again", Advice);
        var again = await ConsultFor(service, "cadence", "1-3", plan: "research/PLAN_x.md");

        Refusal(again).Should().Contain(id).And.Contain("solved").And.Contain("already");
    }

    [Fact]
    public async Task AGroupWithAnOpenConsultation_IsToldToFollowThatOneUp()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service();
        var id = Id(await ConsultFor(service, "cadence", "1-3"));

        Answer("0198-again", Advice);
        var again = await ConsultFor(service, "cadence", "1-3");

        Refusal(again).Should().Contain(id).And.Contain("consultationId").And.Contain("close_consult");
    }

    [Fact]
    public async Task ALapsedGroupConsultation_DoesNotBlockANewOne()
    {
        // Lapsed is the clock's word, not a verdict: nothing was learned that the gate may count, so the
        // group may be consulted on again — or the lapsed one given its outcome with close_consult.
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(turns: 1);
        Id(await ConsultFor(service, "cadence", "1-3"));

        Answer("0198-again", Advice);
        Id(await ConsultFor(service, "cadence", "1-3"));
    }

    [Theory]
    [InlineData("stuck", "", "stuck")]
    [InlineData("cadence", "1-3", "is this group of epics right")]
    [InlineData("risk", "5", "risky piece")]
    public async Task EachKind_IsAskedWithItsOwnPrompt(string kind, string epics, string heard)
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var recorded = Directory.CreateTempSubdirectory("coai-consult-kind-").FullName;
        Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", recorded);
        try
        {
            Id(await ConsultFor(Service(), kind, epics));

            var prompt = Directory.EnumerateFiles(recorded, "*.argv").Select(File.ReadAllText).Single().Split('\0')[^1];
            prompt.Should().ContainEquivalentOf(heard);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKECLI_RECORD_DIR", null);
            Directory.Delete(recorded, recursive: true);
        }
    }

    [Fact]
    public async Task AFollowUpKeepsTheKindItWasOpenedWith()
    {
        using var calling = CallingAs("CLAUDE_CODE_SESSION_ID");
        var service = Service(callsPerSession: 1);
        var id = Id(await ConsultFor(service, "cadence", "1-3"));

        Answer("0198f2c1-first", Advice);
        var follow = await Consult(service, "I checked the second epic's migration: it holds", id);

        follow.TryGetProperty("error", out _).Should().BeFalse(follow.ToString());
        service.Consultations.Read(id)!.Kind.Should().Be("cadence");
    }

    [Fact]
    public void ARecordWrittenBeforeKindsExisted_ReadsAsStuck()
    {
        var dir = Directory.CreateDirectory(Path.Combine(_data, "consultations")).FullName;
        var id = new string('a', 32);
        File.WriteAllText(Path.Combine(dir, id + ".json"), $$"""
            { "id": "{{id}}", "caller": "c", "callerKind": "claude", "sessionId": "no-session", "repoPath": "x",
              "branch": "main", "headSha": "", "vendor": "codex", "model": "", "runtime": "codex",
              "memory": "vendor", "maxTurns": 5, "startedUtc": "2026-09-01T00:00:00Z", "status": "closed" }
            """);

        var record = Service().Consultations.Read(id)!;

        record.Kind.Should().Be("stuck");
        record.Plan.Should().BeEmpty();
        record.Epics.Should().BeEmpty();
        record.RepoId.Should().BeEmpty();
    }
}
