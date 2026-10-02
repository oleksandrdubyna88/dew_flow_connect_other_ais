using CoaiMcp.Core.Context;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecurityPresetTests
{
    [Fact]
    public void Twelve_shipped_presets_have_conditions_and_general_stays_custom()
    {
        SecurityCatalog.Prompts.Should().HaveCount(12);
        SecurityCatalog.Prompts.Should().OnlyContain(p => p.Triggers.Count > 0 && p.Id != "redteam-general");
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
        SecurityRound.Clause([], work).Should().Contain("skipped").And.NotContain("coverage was not established");
    }
}
