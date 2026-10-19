using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The default-off override of DL6 (<c>coai.kimiAllowNetworkSignIn</c>, E6 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>) and how it reaches the launch: the extension writes <c>COAI_KIMI_ALLOW_NETWORK_SIGNIN</c> into the data
/// directory's <c>settings.json</c> like every other <c>COAI_*</c> setting; <see cref="PanelSettings"/> reads it through <c>Flag</c> (an unreadable value stays OFF); the roster puts it on the row's
/// <see cref="ReviewerSettings"/>; <see cref="KimiRuntime.Build"/> binds the invocation's adapter to it.
/// </summary>
public sealed class KimiNetworkSignInSettingTests : IDisposable
{
    private readonly KimiFixture _person = new();

    public void Dispose() => _person.Dispose();

    private static bool Read(string? value) => PanelSettings.FromEnvironment(name => name == "COAI_KIMI_ALLOW_NETWORK_SIGNIN" ? value : null).KimiAllowNetworkSignIn;

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("True")]
    public void TheSettingIsOn_ForTheSpellingsFlagReads(string value) => Read(value).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("yes please")]
    public void TheSettingIsOff_ByDefault_AndForAnythingElse(string? value) => Read(value).Should().BeFalse("an unreadable value stays on the safe side of a rotating sign-in");

    [Fact]
    public void TheSettingsFile_CarriesIt_AsABooleanOrAString_AndTheEnvironmentWins()
    {
        using var dir = TempDir.For("testsToDelete-kimi-netsetting-");
        File.WriteAllText(Path.Combine(dir.Path, SettingsFile.Name), """{"COAI_KIMI_ALLOW_NETWORK_SIGNIN": true}""");

        PanelSettings.FromEnvironment(SettingsFile.Layer(dir.Path, _ => null, _ => { })).KimiAllowNetworkSignIn.Should().BeTrue("the extension's file turned it on");
        PanelSettings.FromEnvironment(SettingsFile.Layer(dir.Path, name => name == "COAI_KIMI_ALLOW_NETWORK_SIGNIN" ? "0" : null, _ => { })).KimiAllowNetworkSignIn.Should().BeFalse("a variable in the client's config is more specific than the file");
    }

    private KimiRuntime RuntimeOn(HostKind host) => new("kimi", new KimiHost(host, WslGeneration.Wsl2), new KimiSeams(_person.PlacesAt, () => ("coai-mcp", [])));

    /// <summary>The card: with the override on, a network sign-in folder is a one-line warning on the row (never the refusal's wall of text), and a local one says nothing.</summary>
    [Fact]
    public void TheCardWarning_IsTheOneLine_OnNetworkStorage_AndNothingOnALocalDisk()
    {
        using var standIn = WindowsStandIn.Begin();
        try
        {
            Junctions.StandInMountInfo(() => "25 1 0:50 / / rw - nfs4 srv:/home rw\n", _ => new MountIdentity.NotAsked());
            var warning = RuntimeOn(HostKind.Wsl).NetworkSignInWarning();
            warning.Should().Contain("network storage (nfs4)").And.Contain("coai.kimiAllowNetworkSignIn").And.NotContain("\n");
            RuntimeOn(HostKind.Windows).NetworkSignInWarning().Should().BeEmpty("a Windows host has no mount table");

            Junctions.StandInMountInfo(() => "25 1 8:1 / / rw - ext4 /dev/sda1 rw\n", _ => new MountIdentity.NotAsked());
            RuntimeOn(HostKind.Wsl).NetworkSignInWarning().Should().BeEmpty("an ext4 home is admitted silently");
        }
        finally
        {
            Junctions.StandInMountInfo(null);
        }
    }

    private KimiRuntime Runtime() => new("kimi", HostKind.Windows, new KimiSeams(_person.PlacesAt, () => ("coai-mcp", [])));

    private ReviewerInvocation Invocation(KimiRuntime runtime, bool allow) =>
        runtime.Build(
            RoleCatalog.ArchitectureRole, "review this", Directory.CreateDirectory(Path.Combine(_person.Folder, "worktree")).FullName, Path.Combine(_person.Folder, "s.json"), _person.Folder,
            new ReviewerSettings("kimi") { Model = "kimi-code/k3", OwnedScratch = Path.Combine(_person.Folder, "round"), KimiAllowNetworkSignIn = allow });

    [Fact]
    public void TheInvocation_IsBoundToTheRowsAnswer_SoTheLaunchControlAsksTheSettingNotAGlobal()
    {
        var runtime = Runtime();

        Invocation(runtime, allow: false).Adapter.Should().BeSameAs(runtime, "nothing differs: the adapter is itself");
        var bound = Invocation(runtime, allow: true).Adapter;
        bound.Should().BeOfType<KimiRuntime>().And.NotBeSameAs(runtime, "a launch with the override carries its own adapter");
        Invocation(runtime, allow: false).Adapter.Should().BeSameAs(runtime, "and the next launch without it is back to the plain one: nothing leaked");
    }
}
