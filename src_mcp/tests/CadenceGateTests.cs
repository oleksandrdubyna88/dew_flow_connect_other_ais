using CoaiMcp.Core.Cadence;
using CoaiMcp.Core.Commands;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Whether a group of epics has had its consultation — and what happens when one cannot be had
/// (<c>research/PLAN_consult_on_a_cadence.md</c>, epic 2, stories 2.1 and 2.5).
/// </summary>
/// <remarks>
/// <para>Only a consultation CLOSED WITH AN OUTCOME is evidence (decision 10). Lapsed and empty are not
/// verdicts, which is why 13 of the 15 consultations this machine had on 2026-09-25 would count for
/// nothing: they idled out and nobody said whether the advice held.</para>
/// <para>And a consultant that cannot be had never deadlocks the work (decision 12): the gate stands down
/// and says why, in the words the consult tool itself would have used.</para>
/// </remarks>
public sealed class CadenceGateTests : IDisposable
{
    private const string RepoId = "d:/work/repo/.git";
    private const string Plan = "todo/PLAN_x.md";

    private readonly string _data = Directory.CreateTempSubdirectory("coai-gate-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private ConsultationStore Store() => new(_data, _ => { }, null);

    private ConsultationRecord Written(string kind, string epics, string status, string outcome, string repoId = RepoId, string plan = Plan)
    {
        var record = new ConsultationRecord(
            ConsultationStore.NewId(), "caller", "claude", "no-session", "d:/work/repo", "main", "", "codex", "", "codex",
            ConsultationMemories.VendorRemembers, 5, "2026-09-25T10:00:00Z")
        {
            Status = status,
            Outcome = outcome,
            Kind = kind,
            Plan = plan,
            Epics = epics,
            RepoId = repoId,
        };
        Store().Write(record);

        return record;
    }

    private static CadenceFacts Facts(int epic, params RiskItem[] risk) => new()
    {
        Mode = CadenceMode.Require,
        RepoPath = "d:/work/repo",
        Plan = Plan,
        Epic = epic,
        Outline = PlanOutlineReader.Of("### Epic 1 — a\n### Epic 2 — b\n### Epic 3 — c\n### Epic 4 — d\n### Epic 5 — e\n### Epic 6 — f\n"),
        RiskAnswered = true,
        RiskItems = risk,
    };

    private CadenceGate Gate(ConsultPreflight? preflight = null) =>
        new(Store(), () => preflight ?? new ConsultPreflight(true, string.Empty));

    [Fact]
    public void AGroupWithNoConsultation_IsBlocked_AndTheRefusalCarriesTheCall()
    {
        var check = Gate().Check(RepoId, Facts(epic: 5), CommandTexts.Shipped);

        var blocked = check.Should().BeOfType<CadenceCheck.Blocked>().Subject;
        blocked.Sentence.Should().Contain("4-6").And.Contain("mcp__coai__consult({").And.Contain("\"kind\": \"cadence\"");
        blocked.Sentence.Should().EndWith("Nothing was reviewed.");
    }

    [Theory]
    [InlineData("solved")]
    [InlineData("not_solved")]
    [InlineData("abandoned")]
    public void AConsultationClosedWithAnyVerdict_SatisfiesItsGroup(string outcome)
    {
        Written("cadence", "4-6", ConsultationStatuses.Closed, outcome);

        Gate().Check(RepoId, Facts(epic: 5), CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Satisfied>();
    }

    [Theory]
    [InlineData("closed", "lapsed")]
    [InlineData("closed", "")]
    [InlineData("open", "")]
    [InlineData("failed", "")]
    public void ALapsedOpenOrFailedConsultation_DoesNotSatisfyItsGroup(string status, string outcome)
    {
        Written("cadence", "4-6", status, outcome);

        Gate().Check(RepoId, Facts(epic: 5), CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Blocked>();
    }

    [Fact]
    public void AnotherPlansOrAnotherRepositorysConsultation_DoesNotSatisfyThisOne()
    {
        Written("cadence", "4-6", ConsultationStatuses.Closed, "solved", plan: "todo/PLAN_y.md");
        Written("cadence", "4-6", ConsultationStatuses.Closed, "solved", repoId: "d:/other/.git");
        Written("stuck", "", ConsultationStatuses.Closed, "solved");

        Gate().Check(RepoId, Facts(epic: 5), CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Blocked>();
    }

    [Fact]
    public void APromotedPlansConsultation_StillCounts()
    {
        Written("cadence", "4-6", ConsultationStatuses.Closed, "solved", plan: "research/PLAN_x.md");

        Gate().Check(RepoId, Facts(epic: 6), CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Satisfied>();
    }

    [Fact]
    public void ARiskyEpic_NeedsItsOwnConsultation_AfterItsGroupHasOne()
    {
        Written("cadence", "4-6", ConsultationStatuses.Closed, "solved");
        var facts = Facts(epic: 5, new RiskItem(5, "5.2", "moves the data"));

        var blocked = Gate().Check(RepoId, facts, CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Blocked>().Subject;
        blocked.Sentence.Should().Contain("\"kind\": \"risk\"").And.Contain("\"epics\": \"5/5.2\"");

        Written("risk", "5/5.2", ConsultationStatuses.Closed, "not_solved");
        Gate().Check(RepoId, facts, CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Satisfied>();
    }

    [Fact]
    public void AnUnavailableConsultant_StandsTheGateDown_WithTheConsultToolsOwnReason()
    {
        var check = Gate(new ConsultPreflight(false, "consulting another vendor is switched off")).Check(RepoId, Facts(epic: 5), CommandTexts.Shipped);

        check.Should().BeOfType<CadenceCheck.StoodDown>().Which.Reason.Should().Be("consulting another vendor is switched off");
    }

    [Fact]
    public void ASatisfiedGroup_NeverAsksThePreflight()
    {
        Written("cadence", "4-6", ConsultationStatuses.Closed, "solved");
        var asked = false;

        new CadenceGate(Store(), () => { asked = true; return new ConsultPreflight(false, "off"); })
            .Check(RepoId, Facts(epic: 5), CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Satisfied>();
        asked.Should().BeFalse("nothing is owed, so nothing needs a consultant");
    }

    [Fact]
    public void AGroupConsultedWeeksAgo_IsStillConsulted_AfterTheSweep()
    {
        // Found while answering epic 2's code round: the sweep deletes a terminal record seven days after
        // it ended, and email-service's fourteen epics ran for weeks. A cadence or risk consultation closed
        // with a verdict is the gate's EVIDENCE, so it is kept; a stuck one still goes.
        var cadence = Written("cadence", "4-6", ConsultationStatuses.Closed, "solved") with { EndedUtc = "2026-08-01T00:00:00Z" };
        var stuck = Written("stuck", "", ConsultationStatuses.Closed, "solved") with { EndedUtc = "2026-08-01T00:00:00Z" };
        Store().Write(cadence);
        Store().Write(stuck);

        Store().Sweep(_ => false, new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(15), ConsultationStore.Retention);

        Gate().Check(RepoId, Facts(epic: 5), CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Satisfied>();
        Store().Read(stuck.Id).Should().BeNull("a stuck consultation past retention is still reaped");
    }

    [Fact]
    public void ALapsedCadenceConsultation_IsStillReaped_ItIsNoEvidence()
    {
        var lapsed = Written("cadence", "4-6", ConsultationStatuses.Closed, "lapsed") with { EndedUtc = "2026-08-01T00:00:00Z" };
        Store().Write(lapsed);

        Store().Sweep(_ => false, new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(15), ConsultationStore.Retention);

        Store().Read(lapsed.Id).Should().BeNull();
    }

    [Fact]
    public void ATornConsultationRecord_CountsAsNoConsultation_NotAsACrash()
    {
        // Epic 2's plan round (codex): the store skips a file it cannot read, so the group is simply
        // not satisfied and the caller is asked to consult — fail closed, and never a deadlock,
        // because a consultation is still possible.
        var dir = Directory.CreateDirectory(Path.Combine(_data, "consultations")).FullName;
        File.WriteAllText(Path.Combine(dir, new string('b', 32) + ".json"), "{ torn");

        Gate().Check(RepoId, Facts(epic: 5), CommandTexts.Shipped).Should().BeOfType<CadenceCheck.Blocked>();
    }
}

/// <summary>
/// The one place the consult tool and the cadence gate ask whether a consultant can be had
/// (<c>research/PLAN_consult_on_a_cadence.md</c>, epic 2, story 2.1).
/// </summary>
/// <remarks>
/// Extracted from <c>AskAsync</c> so the gate stands down on EVERY reason the tool would refuse on —
/// the epic-1-3 consultation found the first design missed two — and so both say it in one sentence.
/// </remarks>
[Collection("fakecli-env")]
public sealed class ConsultPreflightTests : IDisposable
{
    private readonly string _data = Directory.CreateTempSubdirectory("coai-preflight-").FullName;
    private readonly string _repo = Directory.CreateTempSubdirectory("coai-preflight-repo-").FullName;

    /// <summary>A real checkout: the vendor-row refusals come after the tool has resolved the repository.</summary>
    public ConsultPreflightTests() =>
        new CoaiMcp.Runners.Processes.ProcessLauncher()
            .RunAsync(new CoaiMcp.Runners.Processes.ProcessRequest("git", ["init", "-b", "main"], _repo))
            .GetAwaiter().GetResult().ExitCode.Should().Be(0);

    public void Dispose()
    {
        foreach (var dir in (string[])[_data, _repo])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private PanelService Service(
        bool enabled = true,
        bool unreadable = false,
        IReadOnlyList<ProviderSettings>? providers = null,
        IReadOnlyDictionary<string, ConsultantChoice>? consultants = null) => new(
        new PanelSettings
        {
            Providers = providers ?? [new("codex") { ExecutablePath = FakeCliExe }],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = _data,
            ConsultEnabled = enabled,
            ConsultantsUnreadable = unreadable,
            Consultants = consultants ?? ConsultantRouting.Shipped,
        },
        VaultKeys.None("no vault in tests"),
        default,
        new CoaiMcp.Runners.Processes.ProcessLauncher(),
        Logger.None, Noticing.None);

    [Fact]
    public void ARunnableConsultant_IsAvailable() =>
        Service().ConsultPreflight().Should().Be(new ConsultPreflight(true, string.Empty));

    public static TheoryData<string> Reasons => ["off", "unreadable", "no-runtime", "no-row"];

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task EachReason_IsTheSentenceTheConsultToolRefusesWith(string reason)
    {
        var service = reason switch
        {
            "off" => Service(enabled: false),
            "unreadable" => Service(unreadable: true),
            "no-runtime" => Service(providers: [new("codex") { ExecutablePath = FakeCliExe, BaseUrl = "http://localhost:9" }]),
            // Every caller kind routed to a name that is neither a reviewer nor a runtime: nothing resolves.
            _ => Service(consultants: ConsultantRouting.Shipped.Keys.ToDictionary(kind => kind, _ => new ConsultantChoice("nobody", ""))),
        };

        var preflight = service.ConsultPreflight();
        var refused = System.Text.Json.JsonDocument.Parse(
            await service.ConsultAsync(_repo, "stuck on something", "[]", string.Empty, TestContext.Current.CancellationToken))
            .RootElement.GetProperty("error").GetString();

        preflight.Available.Should().BeFalse();
        preflight.Reason.Should().NotBeEmpty();
        refused.Should().Be(preflight.Reason, "one sentence, not two copies that drift");
    }

    [Fact]
    public void TheSwitchedOffSentence_IsByteForByteWhatItWasBeforeTheExtraction() =>
        Service(enabled: false).ConsultPreflight().Reason.Should().Be(
            "consulting another vendor is switched off in this installation (COAI_CONSULT_ENABLED) — the Consultant "
            + "section of the ConnectOtherAIs panel turns it back on. Nothing was sent anywhere; carry on with the person instead");
}
