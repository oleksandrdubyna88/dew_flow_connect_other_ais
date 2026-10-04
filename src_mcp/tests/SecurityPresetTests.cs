using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecurityPresetTests
{
    private const string General = "redteam-general";
    private const string IgnoredConditions = "redteam-general runs on every change; the conditions stored for it are ignored";

    /// <summary>
    /// The operator's ruling of 2026-10-04 (research/PLAN_the_security_tab_reads_at_a_glance.md, D1): general is a
    /// SHIPPED prompt, first in the list, and only on or off — it carries no conditions of its own. The twelve
    /// conditional presets keep theirs.
    /// </summary>
    [Fact]
    public void Twelve_conditional_presets_and_general_always_runs_first()
    {
        SecurityCatalog.Prompts.Should().HaveCount(13);
        SecurityCatalog.Prompts[0].Id.Should().Be(General);
        SecurityCatalog.IsAlways(General).Should().BeTrue();
        SecurityCatalog.Prompts[0].Triggers.Should().BeEmpty();
        SecurityCatalog.Prompts.Skip(1).Should().OnlyContain(p => p.Triggers.Count > 0 && !SecurityCatalog.IsAlways(p.Id));
    }

    /// <summary>"On all code": any change with a code file is enough, signals or not; prose alone is not code.</summary>
    [Fact]
    public void General_runs_on_any_code_change_and_not_on_prose_alone()
    {
        var general = SecurityCatalog.Prompts.Single(p => p.Id == General);
        SecuritySignals.Triggered(general, SecuritySignals.Classify([new("sample.cs", "+return 42;")])).Should().BeTrue();
        SecuritySignals.Triggered(general, SecuritySignals.Classify([new("CHANGELOG.md", "+fixed"), new("sample.cs", "+return 42;")]))
            .Should().BeTrue();
        SecuritySignals.Triggered(general, SecuritySignals.Classify([new("CHANGELOG.md", "+fixed a credential leak")])).Should().BeFalse();
    }

    /// <summary>
    /// "Code" is a path that is not prose AND a diff a reviewer can read. Test, docs and research FOLDERS hold code
    /// too — supporting-material folders are a ranking hint, never a reason to skip — while a binary or a
    /// credential file is withheld from every prompt, so a change of only those leaves general nothing to read.
    /// </summary>
    [Theory]
    [InlineData("tests/AuthTests.cs", false, true)]
    [InlineData("research/example.cs", false, true)]
    [InlineData("docs/api/login.ts", false, true)]
    [InlineData("logo.png", true, false)]
    public void General_is_due_on_readable_code_wherever_it_lives(string path, bool binary, bool due)
    {
        var general = SecurityCatalog.Prompts.Single(p => p.Id == General);
        var file = new FileDiff(path, binary ? string.Empty : "+return 42;", binary, binary ? 1024 : 0);
        SecuritySignals.Triggered(general, SecuritySignals.Classify([file])).Should().Be(due);
    }

    [Fact]
    public void Unknown_members_on_general_are_still_refused_while_malformed_triggers_only_complain()
    {
        var future = SecurityLaneSetting.Parse("""{"enabled":true,"prompts":[{"id":"redteam-general","future":1}]}""", []);
        future.Prompts.Single(p => p.Id == General).Refusal.Should().Contain("unknown members future");

        var claimed = SecurityLaneSetting.Parse("""{"enabled":true,"prompts":[{"id":"redteam-general","always":true}]}""", []);
        claimed.Prompts.Single(p => p.Id == General).Refusal.Should().Contain("unknown members always");

        var malformed = SecurityLaneSetting.Parse("""{"enabled":true,"prompts":[{"id":"redteam-general","triggers":"sql"}]}""", []);
        malformed.Prompts.Single(p => p.Id == General).Refusal.Should().BeEmpty();
        malformed.Complaints.Should().ContainSingle().Which.Should().EndWith(IgnoredConditions);
    }

    /// <summary>
    /// A general registered by hand before it shipped may carry conditions. They no longer mean anything, and an
    /// old leftover must never stop general running: the prompt is kept, its stored conditions are replaced by
    /// the shipped ones, and the person is told once.
    /// </summary>
    [Fact]
    public void Stored_conditions_on_general_are_ignored_with_one_complaint_and_never_refuse_it()
    {
        var lane = SecurityLaneSetting.Parse(
            """{"enabled":true,"prompts":[{"id":"redteam-general","triggers":["sql"],"focus":["xss"]}]}""", []);

        var general = lane.Prompts.Single(p => p.Id == General);
        general.Refusal.Should().BeEmpty();
        general.Triggers.Should().BeEmpty();
        general.Focus.Should().Equal(SecurityCatalog.Prompts[0].Focus);
        lane.Complaints.Should().ContainSingle().Which.Should().EndWith(IgnoredConditions);
        SecuritySignals.Triggered(general, SecuritySignals.Classify([new("sample.cs", "+return 42;")])).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"enabled":true,"prompts":[{"id":"redteam-general"}]}""")]
    [InlineData("""{"enabled":true,"prompts":[{"id":"redteam-general","triggers":[]}]}""")]
    [InlineData("""{"enabled":true,"prompts":[{"id":"redteam-general","triggers":[],"focus":["entry-point"]}]}""")]
    [InlineData("""{"enabled":true,"prompts":[{"id":"redteam-general","triggers":[],"focus":[]}]}""")]
    [InlineData("""{"enabled":true,"prompts":[{"id":"redteam-general","triggers":[],"focus":["sql","xss"]}]}""")]
    public void General_as_the_extension_sends_it_raises_no_complaint(string json)
    {
        var lane = SecurityLaneSetting.Parse(json, []);
        lane.Prompts.Single(p => p.Id == General).Focus.Should().Equal(SecurityCatalog.Prompts[0].Focus,
            "a stored focus on an always prompt is replaced by the shipped one, silently");
        lane.Complaints.Should().BeEmpty();
        lane.Prompts.Single(p => p.Id == General).Refusal.Should().BeEmpty();
    }

    [Theory]
    [InlineData("auth-tokens", "services.AddOpenIdConnect(options => options.UsePkce = true);")]
    [InlineData("sql", "database.Query(sql, parameters);")]
    [InlineData("authz", "app.MapGet(\"/invoices/{id}\", ReadInvoice);")]
    [InlineData("ssrf", "await httpClient.GetAsync(address);")]
    [InlineData("command", "Process.Start(startInfo);")]
    [InlineData("files", "File.ReadAllText(path);")]
    [InlineData("files", "IFormFile uploadedFile;")]
    [InlineData("concurrency", "using var transaction = new TransactionScope();")]
    [InlineData("webhooks", "CryptographicOperations.FixedTimeEquals(received, expected);")]
    [InlineData("prompt-injection", "IChatClient client;")]
    [InlineData("deserialize", "pickle.loads(data)")]
    [InlineData("xss", "element.innerHTML = value;")]
    [InlineData("secrets", "logger.LogInformation(credential);")]
    public void Each_preset_routes_added_and_removed_code_but_not_an_unrelated_change(string id, string code)
    {
        var prompt = SecurityCatalog.Prompts.Single(p => p.Id == "redteam-" + id);
        foreach (var prefix in new[] { "+", "-" })
            SecuritySignals.Triggered(prompt, SecuritySignals.Classify([new("sample.cs", prefix + code)]))
                .Should().BeTrue("removed controls require review too");
        SecuritySignals.Triggered(prompt, SecuritySignals.Classify([new("sample.cs", "+return 42;")]))
            .Should().BeFalse();
    }

    /// <summary>
    /// "Always" is a catalogue fact the server never takes from settings: a 0.42 server refuses the member, and a
    /// conditional preset that arrives claiming it must not get past its triggers.
    /// </summary>
    [Fact]
    public void An_always_member_in_settings_never_makes_a_preset_unconditional()
    {
        var lane = SecurityLaneSetting.Parse("""{"enabled":true,"prompts":[{"id":"redteam-sql","always":true,"triggers":["sql"]}]}""", []);
        var sql = lane.Prompts.Single(p => p.Id == "redteam-sql");
        sql.Refusal.Should().NotBeEmpty();
        SecuritySignals.Triggered(sql, SecuritySignals.Classify([new("sample.cs", "+return 42;")])).Should().BeFalse();
    }

    [Fact]
    public void An_empty_preset_trigger_list_never_becomes_unconditional()
    {
        SecuritySignals.Triggered(new("redteam-sql", [], []), []).Should().BeFalse();
        var setting = SecurityLaneSetting.Parse("""{"enabled":true,"prompts":[{"id":"redteam-sql","triggers":[]}]}""", []);
        setting.Prompts.Single(p => p.Id == "redteam-sql").Refusal.Should().NotBeEmpty();
        SecuritySignals.Triggered(new("redteam-custom", [], []), []).Should().BeTrue();
    }

    [Theory]
    [InlineData("CHANGELOG.md")]
    [InlineData("prompts/redteam-sql.markdown")]
    [InlineData("docs/security.rst")]
    public void Prose_alone_does_not_activate_application_security_checks(string path)
    {
        var files = SecuritySignals.Classify([new(path,
            "+The endpoint uses a credential, SQL queries and Process.Start; fetch(url) reads a file.")]);
        foreach (var prompt in SecurityCatalog.Prompts)
            SecuritySignals.Triggered(prompt, files).Should().BeFalse("a description is not an implementation");
    }

    [Theory]
    [InlineData("+/** Where the list came from, in each of its four states. */")]
    [InlineData("+// vendor run / install / update — and nothing for a hosted API")]
    [InlineData("+document.querySelector('#models');")]
    [InlineData("+commands.executeCommand('refresh');")]
    [InlineData(" // read back out of the DOM: what a section CONTAINS is HTML from the database")]
    [InlineData("+// Open sections are held in memory; the new id needs no migration.")]
    public void Sql_routing_requires_a_database_shape_instead_of_generic_words(string patch)
    {
        var files = SecuritySignals.Classify([new("panel.ts", patch)]);
        SecuritySignals.Triggered(SecurityCatalog.Prompts.Single(p => p.Id == "redteam-sql"), files).Should().BeFalse();
    }

    [Theory]
    [InlineData("src/Query.cs", "-connection.Query<Invoice>(command, parameters);")]
    [InlineData("src/Store.cs", "+connection.QuerySingleOrDefaultAsync<Invoice>(command);")]
    [InlineData("src/Store.cs", "+connection.ExecuteAsync(command);")]
    [InlineData("src/Store.cs", "+command.ExecuteNonQuery();")]
    [InlineData("src/Store.cs", "+DbConnection connection;")]
    [InlineData("src/Store.cs", "+DbCommand command;")]
    [InlineData("src/Changes.cs", "+void Up(MigrationBuilder builder) => builder.AddColumn<int>(name, table);")]
    [InlineData("research/example.cs", "+database.Query(command);")]
    [InlineData("migrations/change.sql", "+UPDATE invoices SET tenant_id = 3;")]
    [InlineData("config/database.json", "+\"query\": \"SELECT id FROM invoices WHERE tenant_id = 3\"")]
    public void Sql_source_and_configuration_still_route_including_removed_calls(string path, string patch)
    {
        var files = SecuritySignals.Classify([new(path, patch)]);
        SecuritySignals.Triggered(SecurityCatalog.Prompts.Single(p => p.Id == "redteam-sql"), files).Should().BeTrue();
    }

    [Fact]
    public void A_derived_slice_round_reserves_source_collection_once_but_an_explicit_deadline_wins()
    {
        var settings = new PanelSettings
        {
            Providers = [new("qwen") { Runtime = "local" }],
            ReviewerTimeout = TimeSpan.FromMinutes(2),
            SecurityLane = new() { Enabled = true, Runs = [new("qwen", "redteam-sql", "slice", 24000, ["code"])] },
        };
        RoundDeadline.For(settings, Stage.CodeReview, 2, 0, Serilog.Core.Logger.None).Should().Be(TimeSpan.FromSeconds(390));
        RoundDeadline.For(settings with { RoundTimeout = TimeSpan.FromSeconds(10) }, Stage.CodeReview, 2, 0,
            Serilog.Core.Logger.None).Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_legitimate_skip_is_not_reported_as_a_failed_security_attempt()
    {
        var work = new RoundWork([], [new("qwen/redteam-sql", "security lane round budget spent")]) { SecurityActive = true };
        SecurityRound.Clause([], work).Should().StartWith("Security lane skipped").And.NotContain("incomplete");
    }

    [Fact]
    public void A_pairing_that_could_not_run_is_reported_as_incomplete_not_skipped()
    {
        // The positive control for the test above, with the same skip present: an EXCLUDED security
        // pairing (here, oversized diffs left the trigger check unfinished) is a failed attempt.
        var work = new RoundWork([], [new("qwen/redteam-sql", "security lane round budget spent")],
            [new("qwen", "redteam-authz", "trigger coverage incomplete: 1 oversized diffs and 0 files beyond the detector limit were not inspected")])
        { SecurityActive = true };
        SecurityRound.Clause([], work).Should().StartWith("Security lane incomplete: configured pairings could not run")
            .And.NotContain("skipped");
    }
}
