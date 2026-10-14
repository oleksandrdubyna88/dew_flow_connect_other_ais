using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>The Kimi profile: how the CLI is found, what it is launched with, what it refuses, and which sentences it names.</summary>
public sealed class KimiAcpProfileTests
{
    private readonly KimiAcpProfile _profile = new();

    [Fact]
    public void TheLaunchIsAcpMode_AndTheModelIsTheModelOption()
    {
        _profile.Name.Should().Be("kimi");
        _profile.Arguments.Should().Equal(["acp"], "prompt mode cannot take stdin (RESULTS §1); ACP is the only launch");
        _profile.ModelOptionId.Should().Be("model");
    }

    [Fact]
    public void TheExclusionsAreBothSpellingsOfTheInstructionFile_AndTheSnapshotsOwn()
    {
        _profile.ExcludedNames.Should().BeEquivalentTo(["AGENTS.md", "agents.md", ".kimi-code", ".vibe", ".agents/skills", ".git"]);
    }

    [Fact]
    public void OnWindows_TheInstallersPathIsTakenWhenItIsThere_ElseTheBareName()
    {
        var home = @"C:\Users\someone";
        var installed = @"C:\Users\someone\.kimi-code\bin\kimi.exe";

        KimiAcpProfile.InstalledExecutableFor(HostKind.Windows, home, null, path => path == installed).Should().Be(installed);
        KimiAcpProfile.InstalledExecutableFor(HostKind.Windows, home, null, _ => false).Should().Be("kimi",
            "a bare name is what ExecutableResolver walks the PATH for at launch");
    }

    [Theory]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Other)]
    public void ElsewhereNothingIsGuessed(HostKind host)
    {
        KimiAcpProfile.InstalledExecutableFor(host, "/home/someone", "/usr/bin", _ => true).Should().Be("kimi",
            "nothing has run Kimi there yet, and a path nobody observed is not one to start a process from");
    }

    [Fact]
    public void TheSubscription401IsNamedBeforeTheSignInSentenceItBeginsWith()
    {
        var line = "Authentication required: 401 Your current subscription does not have access to kimi-for-coding-highspeed. Upgrade to higher-tier Kimi Code plans.";

        var diagnosis = AcpDiagnosis.Of(line, _profile.KnownFailures);

        diagnosis.Name.Should().Be("subscription_model", "the specific sentence is listed first (RESULTS §2)");
        diagnosis.Kind.Should().Be(AcpFailureKind.Refused);
        diagnosis.Cure.Should().Contain("kimi-code/k3-256k");
    }

    [Fact]
    public void ASignInRefusal_ASpentBalance_AndAnythingElse()
    {
        AcpDiagnosis.Of("Authentication required: 401 token expired", _profile.KnownFailures).Name.Should().Be("not_signed_in");
        AcpDiagnosis.Of("error 429: Insufficient Balance", _profile.KnownFailures).Should().Match<AcpDiagnosis>(d => d.Name == "balance_spent" && d.Kind == AcpFailureKind.Spent);
        AcpDiagnosis.Of("the agent stopped with `refusal`", _profile.KnownFailures).Should().Be(AcpDiagnosis.None);
        AcpDiagnosis.None.Known.Should().BeFalse();
    }

    [Fact]
    public void TheCatalogFindsByNameCaseInsensitively_AndRefusesWhatItLacks()
    {
        AcpProfiles.Find(AcpProfiles.All, "KIMI").Should().BeOfType<KimiAcpProfile>();
        AcpProfiles.Find(AcpProfiles.All, "vibe").Should().BeNull("Vibe is not an ACP profile — its CLI flags plus the snapshot are its blocks (§8 of the plan)");
        AcpProfiles.Names(AcpProfiles.All).Should().Equal(["kimi"]);
    }
}
