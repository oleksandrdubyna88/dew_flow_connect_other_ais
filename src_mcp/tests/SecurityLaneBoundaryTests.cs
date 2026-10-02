using System.Collections.Immutable;
using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Where the lane meets its neighbours: the decision summary, the context budget, the schema, the settings.</summary>
public sealed class SecurityLaneBoundaryTests
{
    private static ReviewerWork Ordinary() =>
        new(new("codex", RoleCatalog.ArchitectureRole, new ProcessRequest("fixture", [], ".")), PromptBytes: 10);

    [Fact]
    public void The_decision_summary_still_names_who_could_not_run_and_who_was_not_asked()
    {
        var work = Ordinary();
        (ReviewerInvocation, ReviewerOutcome)[] results = [(work.Invocation, new ReviewerOutcome.NonZeroExit(7, "fixture failure"))];
        var all = ReviewerSummaryFactory.From(results, ["gemini: credentials unavailable"],
            [new SkippedRole("conventions", "no written rules")]) with { EndedByDeadline = TimeSpan.FromMinutes(5) };

        var summary = SecurityRound.DecisionSummary(results, new RoundWork([work], []) { OrdinaryDue = true }, all);

        summary.Answered.Should().Be(0);
        summary.Sentence.Should().Contain("1 enabled reviewer could not run: gemini: credentials unavailable")
            .And.Contain("conventions was not asked: no written rules")
            .And.Contain("5 minute limit");
    }

    [Fact]
    public void A_patch_that_fits_is_kept_when_its_source_does_not()
    {
        var files = SecuritySignals.Classify([new("src/Orders.cs", "+ database.Query(value);")]);
        var sources = new Dictionary<string, string> { ["src/Orders.cs"] = new string('s', 200_000) };

        var pack = SecurityContext.Compose("Operator test instructions", new("redteam-sql", ["sql"], ["sql"]), files,
            SecurityContextModes.Slice, new(24000), sources);

        pack.Refusal.Should().BeEmpty();
        pack.Text.Should().Contain("database.Query(value)").And.NotContain(new string('s', 100));
        pack.Omitted.Should().Contain("src/Orders.cs (source omitted for budget; patch only)");
    }

    [Fact]
    public void Evidence_at_the_schema_maximum_is_accepted_by_the_validator()
    {
        using var schema = JsonDocument.Parse(SecuritySchema.Json);
        var fields = schema.RootElement.GetProperty("properties").GetProperty("findings").GetProperty("items").GetProperty("properties");
        string AtMaximum(string name) => new('x', fields.GetProperty(name).GetProperty("maxLength").GetInt32());
        var finding = new Finding(Severity.Minor, Category.Security, "Orders.cs", 1, "Fixture", "Evidence", "Correction", ["qwen"])
        {
            AttackEvidence = new(AtMaximum("trigger"), AtMaximum("mechanism"), AtMaximum("consequence")),
        };
        var lane = new ReviewerWork(new("qwen", "redteam-sql", new ProcessRequest("fixture", [], ".")), PromptBytes: 10) { IsSecurity = true };
        var answer = new ReviewerOutcome.Ok(new([finding], []) { SecurityStatus = "FINDINGS" }, false);

        SecurityAnswerLimit.Apply(lane, answer).Should().BeOfType<ReviewerOutcome.Ok>(
            "an answer the declared schema admits must not be refused as unparseable");
    }

    [Fact]
    public void A_configuration_the_extension_preserved_as_malformed_is_named_as_such()
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":false,"threshold":0,"maxRounds":2,"prompts":[],"runs":[],"invalidConfiguration":{"enabled":"yes"}}
            """, [new("codex")]);

        lane.Enabled.Should().BeFalse();
        lane.Complaints.Should().ContainSingle().Which.Should()
            .Contain("the extension found the security lane configuration malformed; the lane is off until coai.securityLane is corrected")
            .And.NotContain("update this server");
    }

    [Fact]
    public void The_extensions_wrapped_malformed_value_keeps_the_lane_off_and_reaches_the_unrecognised_list()
    {
        // The extension's wire shape: the lane off, no runs, and the whole stored value nested untouched.
        var settings = SecurityLaneSetting.Apply(new PanelSettings { Providers = [new("codex")] }, """
            {"enabled":false,"runs":[],"invalidConfiguration":{"enabled":"yes","runs":[{"vendor":"codex","prompt":"redteam-sql",
            "stages":["code",{"nested":[1,2,3]}]}],"futureSwitch":true}}
            """);

        settings.SecurityLane.Enabled.Should().BeFalse();
        settings.SecurityLane.Runs.Should().BeEmpty();
        settings.Rounds.SecurityLane.Enabled.Should().BeFalse();
        settings.UnrecognisedSettings.Should().ContainSingle(u => u.Key == SecurityLaneSetting.Key).Which.Sentence.Should()
            .Contain("coai.securityLane").And.NotContain("update this server");
    }

    [Fact]
    public void An_unknown_member_still_asks_for_a_newer_server()
    {
        var lane = SecurityLaneSetting.Parse("""{"enabled":true,"futureSwitch":1}""", [new("codex")]);
        lane.Complaints.Should().ContainSingle().Which.Should().Contain("unknown members futureSwitch; update this server");
    }

    [Fact]
    public void Production_files_rank_ahead_of_supporting_material_and_then_by_focus()
    {
        var files = SecuritySignals.Classify([
            new("docs/sql.md", "+ database.Query(value);"),
            new("src/Plain.cs", "+ return 1;"),
            new("src/Orders.cs", "+ database.Query(value);"),
        ]);
        SecuritySignals.Rank(files, ["sql"]).Select(f => f.Diff.Path).Should().Equal("src/Orders.cs", "src/Plain.cs", "docs/sql.md");
    }

    [Fact]
    public void Evidence_stored_before_the_damage_marker_reads_as_undamaged()
    {
        var older = JsonSerializer.Deserialize("""{"capReason":"fixture","alsoSeenBy":[]}""",
            ServerJsonContext.Default.SecurityFindingDetails)!;
        older.Unreadable.Should().BeEmpty();
        older.CapReason.Should().Be("fixture");
    }

    [Fact]
    public void Damaged_security_evidence_is_named_without_losing_the_finding()
    {
        var data = Directory.CreateTempSubdirectory("coai-security-store-").FullName;
        try
        {
            using (var db = Store.RoundsDb.Open(data, Serilog.Core.Logger.None)!)
            {
                var found = SecurityEvidence.Attribute(new Finding(Severity.Major, Category.Security, "Query.cs", 1,
                    "Fixture", "Evidence", "Correction", ["codex"]), "codex", "redteam-general");
                db.RecordRound(new("s", data, "main", new()), new("CodeReview", 1, "proceed", 0, "fixture", DateTime.UtcNow), [found]);
            }
            using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(data, Store.RoundsDb.FileName)}"))
            {
                raw.Open();
                using var damage = raw.CreateCommand();
                damage.CommandText = "UPDATE findings SET security_evidence = '{\"capReason\": '";
                damage.ExecuteNonQuery().Should().Be(1, "the fixture must damage the one stored projection");
            }

            var read = Store.RoundsQuery.FindingsOf(data, "s", "CodeReview", 1).Findings.Single();

            read.Title.Should().Be("Fixture", "a damaged projection must not prevent reading the round");
            read.SecurityEvidence.Should().NotBeNull("damage must not read as a finding that never carried evidence");
            read.SecurityEvidence!.Unreadable.Should().Contain("could not be read");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(data, true);
        }
    }
}
