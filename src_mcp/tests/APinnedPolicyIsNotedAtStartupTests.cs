using CoaiMcp.Core.Notices;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A client <c>env</c> block that still pins a policy other than <c>auto</c> wins over the panel (<c>SettingsFile.Over</c>), so the migration
/// cannot reach it: the server says so at startup, naming where to remove it (<c>todo/PLAN_auto_never_asks_a_person_E1.md</c> § 9.3, the
/// parent's § 4.6). Only the PROCESS environment counts — the same value in the settings file is the panel's own choice.
/// </summary>
public sealed class APinnedPolicyIsNotedAtStartupTests
{
    private static Func<string, string?> Env(string? value) => key => key == "COAI_ON_EXHAUSTED" ? value : null;

    [Theory]
    [InlineData("human")]
    [InlineData("continue")]
    [InlineData("escalate")]
    [InlineData("good_enough")]
    [InlineData("goodenough")]
    [InlineData("HUMAN")]
    public void AKnownPolicyOtherThanAuto_InTheProcessEnvironment_IsOneNote(string value)
    {
        var notes = PanelSettings.PinnedPolicy(Env(value));

        var note = notes.Should().ContainSingle().Subject;
        note.Key.Should().Be("COAI_ON_EXHAUSTED");
        note.Sentence.Should().Contain($"'{value}'").And.Contain("this server's own environment").And.Contain("your MCP client's `env` block")
            .And.Contain("overrides the panel").And.Contain("Auto is the default now").And.Contain("Remove it from that `env` block to follow the panel.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("nonsense")]
    public void AutoAbsentEmptyAndAnUnknownWord_AreNoNote(string? value) =>
        PanelSettings.PinnedPolicy(Env(value)).Should().BeEmpty("an unknown word is left to the note that says this server does not know it");

    [Fact]
    public void TheNoteIsWrittenThroughTheExistingCode_AtWarning()
    {
        var offered = new List<ServerNotice>();
        var sink = new ListSink();
        var log = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        StartupNotices.PinnedPolicy(PanelSettings.PinnedPolicy(Env("human")), new Noticing(notice => { offered.Add(notice); return true; }, log), log);

        var notice = offered.Should().ContainSingle().Subject;
        notice.Code.Should().Be(ServerNoticeCodes.UnrecognisedSetting, "the extension renders the codes it knows; no new code");
        notice.Subject.Should().Be("COAI_ON_EXHAUSTED");
        sink.Lines.Should().ContainSingle().Which.Should().Contain("this server's own environment");
    }

    [Fact]
    public void AnUnreadableFileAndAPinnedPolicy_EachWriteTheirOwnNote()
    {
        var offered = new List<ServerNotice>();
        var log = new Serilog.LoggerConfiguration().CreateLogger();
        var noticing = new Noticing(notice => { offered.Add(notice); return true; }, log);

        StartupNotices.SettingsUnread("the settings file could not be read", "/data/settings.json", noticing, log);
        StartupNotices.PinnedPolicy(PanelSettings.PinnedPolicy(Env("human")), noticing, log);

        offered.Select(n => n.Subject).Should().HaveCount(2).And.Contain("COAI_ON_EXHAUSTED");
        offered.Select(n => n.Subject).Distinct().Should().HaveCount(2, "another fact, on another subject");
    }

    /// <summary>The startup block is the one place that runs on the PROCESS environment — the only one a client's env block reaches — so the call is pinned where it is made.</summary>
    [Fact]
    public void TheStartupBlock_ReadsTheProcessEnvironment_AfterTheOtherNotes() =>
        ProductionSources.CodeOf("src_mcp/src/Program.cs").Should().Contain(
            "StartupNotices.PinnedPolicy(PanelSettings.PinnedPolicy(Environment.GetEnvironmentVariable), noticing, log);",
            "the process environment, never the layered settings: the panel's own value is not a pin");
}
