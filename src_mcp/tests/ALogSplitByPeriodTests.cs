using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Server;
using CoaiMcp.Store;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What it keeps missing, counted over a PERIOD — `--log --since` (operator, 2026-09-25).
/// </summary>
/// <remarks>
/// <para>The Review rounds page gained a Today / Week / Month / Year / All switch on the tabs beside
/// its spending tab. The blind spots and the defended list are aggregates over every finding, so only
/// this server can count them over a period: the extension sends the period's first instant and the
/// groups count only the findings of rounds that started at or after it.</para>
/// <para>The answer ECHOES the instant it applied. That echo is how the extension knows: a coai-mcp
/// 0.36.0 given `--since` was measured to ignore it and exit 0 with the all-time answer, so an exit
/// code could never have told the two apart.</para>
/// </remarks>
public sealed class ALogSplitByPeriodTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-period-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly Serilog.ILogger _log = Serilog.Core.Logger.None;

    private static readonly SessionState Session =
        new("s1", "D:/repo", "feat/x", new PanelConfig()) { Stage = Stage.CodeReview };

    private static readonly DateTime Now = DateTime.UtcNow;

    private static Finding Found(string title, Category category, string provider = "codex") =>
        new(Severity.Major, category, "src/Panel.cs", 40, title, title + " — because", "do this", [provider])
        {
            Role = "SecurityReliability",
        };

    private static RoundRecord Round(int number, DateTime started) =>
        new("CodeReview", number, "proceed", 2, "all 3 reviewers answered", started)
        {
            StartedUtc = started,
            Subject = "SCOPE — something",
            ReviewerStates = [new ReviewerState("codex", "Architecture", ReviewerState.Done, 2, "", 23.4)],
        };

    /// <summary>A round three days old that accepted a Security finding, and one a minute old that accepted a Ux one.</summary>
    private void TwoRoundsDaysApart()
    {
        using var db = RoundsDb.Open(_dir, _log)!;
        Finding[] old = [Found("old", Category.Security, "gemini")];
        Finding[] recent = [Found("recent", Category.Ux, "codex")];
        db.RecordRound(Session, Round(1, Now.AddDays(-3)), old);
        db.RecordDecisions("s1", "CodeReview", 1, [Decisions.Accept(old, 0)]);
        db.RecordRound(Session, Round(2, Now.AddMinutes(-1)), recent);
        db.RecordDecisions("s1", "CodeReview", 2, [Decisions.Accept(recent, 0)]);
    }

    [Fact]
    public void WhatItKeepsMissing_CountsOnlyTheRoundsSinceTheInstant()
    {
        TwoRoundsDaysApart();
        var since = Now.AddHours(-1).ToString("O");

        var log = RoundsQuery.Read(_dir, since: since);

        log.BlindSpots.Should().Contain(s => s.Kind == "category" && s.Name == "Ux" && s.Accepted == 1);
        log.BlindSpots.Should().NotContain(s => s.Name == "Security", "a round three days old is outside the last hour");
        log.BlindSpots.Should().NotContain(s => s.Kind == "providers" && s.Name == "gemini");
        log.Since.Should().Be(since, "the instant applied is echoed, which is how the extension knows it was applied");
    }

    [Fact]
    public void WithNoInstant_ItIsAllTime_AndEchoesNothing()
    {
        TwoRoundsDaysApart();

        var log = RoundsQuery.Read(_dir);

        log.BlindSpots.Should().Contain(s => s.Name == "Security").And.Contain(s => s.Name == "Ux");
        log.Since.Should().BeEmpty("nothing was applied, and an empty echo says so");
    }

    [Fact]
    public void TheDefendedList_IsSplitByTheSamePeriod()
    {
        using (var db = RoundsDb.Open(_dir, _log)!)
        {
            var standing = Found("session file opened without FileShare", Category.Reliability);
            Finding[] raised = [standing];
            db.RecordRound(Session, Round(2, Now.AddDays(-3)), raised,
                new RoundContext("SCOPE", "7133c2f", "claude-code", [standing]));
            db.RecordDecisions("s1", "CodeReview", 2, [Decisions.Reject(raised, 0, "still no")]);
        }

        RoundsQuery.Read(_dir).Defended.Should().ContainSingle("all time still lists it");
        RoundsQuery.Read(_dir, since: Now.AddHours(-1).ToString("O")).Defended
            .Should().BeEmpty("the disagreement was defended three days ago, not in the last hour");
    }

    [Fact]
    public void TheRoundsAndTheConsultations_AreNotSplit()
    {
        TwoRoundsDaysApart();

        RoundsQuery.Read(_dir, since: Now.AddHours(-1).ToString("O")).Rounds
            .Should().HaveCount(2, "the period is the blind spots' and the defended list's alone");
    }

    [Theory]
    [InlineData("2026-09-25T00:00:00Z", "2026-09-25T00:00:00.0000000Z")]
    [InlineData("2026-09-24T22:00:00.000+02:00", "2026-09-24T20:00:00.0000000Z")]
    [InlineData("2026-09-25T07:31:05.123Z", "2026-09-25T07:31:05.1230000Z")]
    public void AnInstantIsNormalised_ToTheWayStartedUtcIsStored(string given, string stored) =>
        Program.SinceOf(["--log", "--paged", "--since", given]).Should().Be(stored,
            "started_utc is compared as text, so the instant must be written exactly the way it is");

    [Theory]
    [InlineData("yesterday")]
    [InlineData("")]
    [InlineData("2026-13-40T00:00:00Z")]
    public void AnInstantThatIsNotOne_IsRefused_NotIgnored(string given) =>
        Program.SinceOf(["--log", "--since", given]).Should().BeNull(
            "a period that silently became all time is the failure this flag must never have");

    [Fact]
    public void NoInstantAtAll_IsAllTime()
    {
        Program.SinceOf(["--log", "--paged"]).Should().BeEmpty();
        Program.SinceOf(["--log", "--since"]).Should().BeNull("a flag with no value is a malformed request");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
