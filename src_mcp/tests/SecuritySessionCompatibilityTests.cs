using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

public sealed class SecuritySessionCompatibilityTests
{
    private static Finding Defect => new(Severity.Major, Category.Security, "Query.cs", 10,
        "Recorded finding", "Recorded mechanism", "Recorded fix", ["codex"]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_session_with_pre_lane_findings_can_be_saved_without_losing_pending_or_rejected_findings(bool nullCap)
    {
        using var directory = TempDir.For("coai-security-old-session-");
        var store = new SessionStore(directory);
        var session = new PersistedSession(new SessionState("old", "D:/fixture", "main", new PanelConfig())
        { Rejections = [new(Defect, "Verified rejection")] }, [])
        { Pending = [Defect] };
        var json = JsonNode.Parse(JsonSerializer.Serialize(session, ServerJsonContext.Default.PersistedSession))!;
        foreach (var finding in new[] { json["pending"]![0]!, json["state"]!["rejections"]![0]!["finding"]! })
        {
            finding.AsObject().Remove("alsoSeenBy");
            finding.AsObject().Remove("reproduction");
            finding.AsObject().Remove("attackEvidence");
            finding.AsObject().Remove("capReason");
            if (nullCap) finding["capReason"] = null;
        }
        var file = store.FileFor("D:/fixture", "main");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, json.ToJsonString());

        var loaded = store.Load("D:/fixture", "main")!;
        var save = () => store.Save(loaded);
        save.Should().NotThrow("old findings have no security sightings, not an unserializable default array");
        var read = store.Load("D:/fixture", "main")!;
        read.Pending.Should().ContainSingle().Which.Title.Should().Be(Defect.Title);
        read.State.Rejections.Should().ContainSingle().Which.Reason.Should().Be("Verified rejection");
        foreach (var finding in new[] { read.Pending.Single(), read.State.Rejections.Single().Finding })
        {
            finding.AlsoSeenBy.IsDefault.Should().BeFalse();
            finding.AlsoSeenBy.Should().BeEmpty();
            finding.CapReason.Should().BeEmpty();
            SecurityFindingStore.Write(finding).Should().BeEmpty("an old ordinary finding has no security evidence to project");
        }
    }

    [Fact]
    public void Nonempty_security_sightings_and_caps_survive_the_same_store_roundtrip()
    {
        using var directory = TempDir.For("coai-security-new-session-");
        var store = new SessionStore(directory);
        var finding = SecurityEvidence.Attribute(Defect, "local-security", "redteam-sql");
        var session = new PersistedSession(new SessionState("new", "D:/fixture", "main", new PanelConfig()), [])
        { Pending = [finding] };

        store.Save(session);
        var read = store.Load("D:/fixture", "main")!.Pending.Single();

        read.CapReason.Should().Be(finding.CapReason).And.NotBeEmpty();
        read.AlsoSeenBy.Should().ContainSingle().Which.Should().Be(finding.AlsoSeenBy.Single());
    }
}
