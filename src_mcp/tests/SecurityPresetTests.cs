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
