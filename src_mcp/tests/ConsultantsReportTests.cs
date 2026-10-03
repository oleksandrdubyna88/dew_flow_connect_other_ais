using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// How one caller kind's facts become its <c>--consultants</c> row — per platform, by an injected host, so the Linux and
/// WSL rows are asserted on a Windows machine too.
/// </summary>
/// <remarks>
/// E4.1 of PLAN_the_consultant_works_on_every_vendor.md; requirements 8, 12 and 13: a claude row's limitation follows
/// what its installed CLI SAID; agy's allow-rule snippet appears on Linux/WSL only, with its warning; a failure is shown
/// as current only when it is later than the last answer (<see cref="ConsultHealth.Current"/>, the one rule).
/// </remarks>
public sealed class ConsultantsReportTests
{
    private static readonly ConsultPreflight Available = new(true, string.Empty);

    /// <summary>What one probed claude-kind row is built from, as the tests vary it.</summary>
    private sealed record Given(ProviderSettings Vendor, ClaudeCapability? Claude, ConsultHealthAnswer? Answer, ConsultHealthFailure? Failure);

    private static Given Facts(
        ProviderSettings vendor,
        ClaudeCapability? claude = null,
        ConsultHealthAnswer? answer = null,
        ConsultHealthFailure? failure = null) => new(vendor, claude, answer, failure);

    private static HealthOnDisk<T> OnDisk<T>(T? value) where T : class =>
        value is null ? new HealthOnDisk<T>.None() : new HealthOnDisk<T>.Found(value);

    private static ConsultantRowReport Row(Given given, HostKind host) =>
        ConsultantsReport.Row(
            new SurveyedConsultant.Probed("claude", Available, given.Vendor, new VendorHealth(true, true, "1.2.15", "own auth", "fine"),
                given.Claude is null ? new CliCapability.NotApplicable() : new CliCapability.Claude(given.Claude)),
            new ConsultantOutcomes(OnDisk(given.Answer), OnDisk(given.Failure), new CheckOnDisk.None()),
            host,
            path => "<home>" + path);

    private static readonly ProviderSettings Agy = new("antigravity") { Runtime = "antigravity" };

    private static readonly ProviderSettings Claude = new("claude") { Runtime = "claude" };

    [Theory]
    [InlineData(HostKind.Linux, true)]
    [InlineData(HostKind.Wsl, true)]
    [InlineData(HostKind.Windows, false)]
    [InlineData(HostKind.MacOs, false)]
    public void TheAllowRuleSnippet_IsOfferedOnLinuxAndWslOnly_WithItsWarning(HostKind host, bool offered)
    {
        var agy = Row(Facts(Agy), host).Agy;

        agy.Should().NotBeNull("an antigravity row always says where agy reads its rules");
        if (offered)
        {
            agy!.Snippet.Should().Contain("command(git grep)");
            agy.SnippetWarning.Should().Contain("not read-only");
        }
        else
        {
            agy!.Snippet.Should().BeEmpty($"a prefix rule was refused on Windows and never measured on {host}");
            agy.SnippetWarning.Should().BeEmpty();
        }
    }

    [Fact]
    public void TheAgySettingsPath_IsThisSidesOwn_WithItsHomeFilledIn()
    {
        Row(Facts(Agy), HostKind.Windows).Agy!.SettingsPath.Should().Be(@"<home>%USERPROFILE%\.gemini\antigravity-cli\settings.json");
        Row(Facts(Agy), HostKind.Wsl).Agy!.SettingsPath.Should().Be("<home>~/.gemini/antigravity-cli/settings.json");
        Row(Facts(Claude, ClaudeCapability.WithRestricted), HostKind.Wsl).Agy.Should().BeNull("only antigravity reads that file");
    }

    [Fact]
    public void AClaudeRowsLimitation_FollowsWhatItsHelpSaid()
    {
        var confined = Row(Facts(Claude, ClaudeCapability.WithRestricted), HostKind.Windows);
        var unconfined = Row(Facts(Claude, ClaudeCapability.NoRestricted), HostKind.Windows);
        var unknown = Row(Facts(Claude, ClaudeCapability.Unknown("asked twice: claude --help exited 1")), HostKind.Windows);

        confined.Capability.Should().Be("restricted");
        confined.Limitation!.Standing.Should().Be("confined");
        confined.Limitation.Evidence.Should().Be("measured");
        unconfined.Capability.Should().Be("no-restricted");
        unconfined.Limitation!.Standing.Should().Be("unconfined", "a claude without --restricted is not confined on any platform");
        unknown.Capability.Should().Be("unknown");
        unknown.Limitation!.Standing.Should().Be("unmeasured", "a claude nobody could probe matches no claude row");
    }

    [Fact]
    public void AFailureLaterThanTheLastAnswer_IsCurrent_AndOneSupersededByALaterAnswer_IsNot()
    {
        var answer = new ConsultHealthAnswer { Utc = "2026-10-03T10:00:00.0000000Z", CallerKind = "claude", Vendor = "claude" };
        var earlier = new ConsultHealthFailure { Utc = "2026-10-03T09:00:00.0000000Z", CallerKind = "claude", Vendor = "claude", Kind = "empty" };
        var later = earlier with { Utc = "2026-10-03T11:00:00.0000000Z" };

        Row(Facts(Claude, ClaudeCapability.WithRestricted, answer, later), HostKind.Windows).FailureCurrent.Should().BeTrue();
        var superseded = Row(Facts(Claude, ClaudeCapability.WithRestricted, answer, earlier), HostKind.Windows);
        superseded.FailureCurrent.Should().BeFalse("a later answer supersedes it");
        superseded.LastFailure.Should().NotBeNull("nothing is deleted — the reader decides");
    }

    [Fact]
    public void AFailureOfAnotherConsultant_IsNotCurrentForTheRowThatNamesThisOne()
    {
        // The whole-branch review, A3: the failure file is per CALLER KIND, so a row that moved from codex to claude
        // inherited codex's quota as its own current failure until something answered.
        var codexFailed = new ConsultHealthFailure { Utc = "2026-10-03T11:00:00.0000000Z", CallerKind = "claude", Vendor = "codex", Model = "gpt-5.6", Kind = "quota" };

        Row(Facts(Claude, ClaudeCapability.WithRestricted, failure: codexFailed), HostKind.Windows).FailureCurrent
            .Should().BeFalse("the row names claude now, and the failure is codex's");
    }

    [Fact]
    public void AnAnswerOfAnotherConsultant_DoesNotHideThisConsultantsFailure()
    {
        var claudeFailed = new ConsultHealthFailure { Utc = "2026-10-03T10:00:00.0000000Z", CallerKind = "claude", Vendor = "claude", Kind = "empty" };
        var codexAnswered = new ConsultHealthAnswer { Utc = "2026-10-03T11:00:00.0000000Z", CallerKind = "claude", Vendor = "codex", Model = "gpt-5.6" };
        var claudeAnswered = codexAnswered with { Vendor = "claude", Model = string.Empty };

        Row(Facts(Claude, ClaudeCapability.WithRestricted, codexAnswered, claudeFailed), HostKind.Windows).FailureCurrent
            .Should().BeTrue("codex answering says nothing about whether claude works");
        Row(Facts(Claude, ClaudeCapability.WithRestricted, claudeAnswered, claudeFailed), HostKind.Windows).FailureCurrent
            .Should().BeFalse("claude itself answered since");
    }

    [Fact]
    public void AnEntryThatResolvesToNothing_IsARowWithItsReason_AndNothingProbed()
    {
        var row = ConsultantsReport.Row(
            new SurveyedConsultant.Unresolved("gemini", new ConsultPreflight(false, "no reviewer or runtime is called 'nobody'")),
            ConsultantOutcomes.Nothing,
            HostKind.Windows,
            path => path);

        row.Available.Should().BeFalse();
        row.Reason.Should().Contain("nobody");
        row.Vendor.Should().BeEmpty();
        row.Cli.Probed.Should().BeFalse();
        row.Limitation.Should().BeNull();
    }

    [Fact]
    public void TheCliIsReportedAsItsAuthSource_NeverAsASignIn()
    {
        var cli = Row(Facts(Agy), HostKind.Windows).Cli;

        cli.Should().Be(new ConsultantCliReport(true, true, "1.2.15", "own auth", "fine"));
    }
}
