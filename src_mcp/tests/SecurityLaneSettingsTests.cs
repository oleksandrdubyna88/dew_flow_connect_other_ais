using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecurityLaneSettingsTests
{
    private static readonly ProviderSettings[] Providers = [new("qwen") { Runtime = "local", Model = "fixture" }];

    [Theory]
    [InlineData("{\"enabled\":true,\"prompts\":{}}")]
    [InlineData("{\"enabled\":true,\"threshold\":-1}")]
    [InlineData("{\"enabled\":true,\"enabled\":false}")]
    [InlineData("{\"enabled\":true,\"futureMode\":true}")]
    public void Invalid_root_never_enables_the_lane(string json)
    {
        var lane = SecurityLaneSetting.Parse(json, Providers);
        lane.Enabled.Should().BeFalse();
        lane.Complaints.Should().NotBeEmpty();
    }

    [Fact]
    public void Unknown_trigger_refuses_its_pair_while_unknown_focus_is_ignored()
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":true,"prompts":[{"id":"redteam-sql","triggers":["future"],"focus":["future"]}],
            "runs":[{"vendor":"QWEN","prompt":"redteam-sql"}]}
            """, Providers);
        lane.Prompts.Single(p => p.Id == "redteam-sql").Refusal.Should().NotBeEmpty();
        lane.Prompts.Single(p => p.Id == "redteam-sql").Focus.Should().BeEmpty();
        lane.Runs.Single().Vendor.Should().Be("qwen");
        lane.Runs.Single().Context.Should().Be("slice");
        lane.Runs.Single().ContextTokens.Should().Be(24000);
    }

    [Fact]
    public void Duplicate_pairs_are_not_launched_twice_and_unknown_run_fields_refuse()
    {
        var lane = SecurityLaneSetting.Parse("""
            {"enabled":true,"prompts":[{"id":"redteam-general"}],"runs":[{"vendor":"qwen","prompt":"redteam-general","future":1},
            {"vendor":"QWEN","prompt":"redteam-general"}]}
            """, Providers);
        lane.Runs.Should().ContainSingle().Which.Refusal.Should().NotBeEmpty();
        lane.Complaints.Should().Contain(c => c.Contains("duplicate pair"));
    }

    [Fact]
    public void Malformed_security_configuration_is_reported_instead_of_silently_ignored()
    {
        var settings = PanelSettings.FromEnvironment(key => key == "COAI_SECURITY_LANE" ? "{" : null);
        settings.Unrecognised.Should().Contain(message => message.Contains("COAI_SECURITY_LANE"));
    }
}
