using CoaiMcp.Runners.Files;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A wrong <c>COAI_CONSULTANT_HOMES</c> is said at startup, with the stored value, as a structured setting problem the Settings page
/// marks (D7 and D8 of <c>todo/PLAN_vibe_live_round_fixes.md</c>): the extension takes the server's word for this setting, so a
/// server that said nothing left a missing folder unmarked until a Vibe launch was refused.
/// </summary>
public sealed class TheVibeHomesRootIsJudgedAtStartupTests : IDisposable
{
    private const string Key = "COAI_CONSULTANT_HOMES";

    private readonly TempDir _dir = TempDir.For("testsToDelete-vh-startup-");

    public void Dispose() => _dir.Dispose();

    private static PanelSettings With(string? homes) =>
        PanelSettings.FromEnvironment(name => name == Key ? homes : null);

    [Fact]
    public void AFolderThatDoesNotExist_IsAMissingFolderProblem_WithItsStoredValueAndTheLaunchSentence()
    {
        var gone = _dir.At("not-made");

        var problem = With(gone).SettingProblems.Should().ContainSingle().Subject;

        problem.Key.Should().Be(Key);
        problem.Kind.Should().Be(SettingProblemKinds.MissingFolder);
        problem.Value.Should().Be(gone);
        problem.Sentence.Should().Be(PrivateFolder.RootRefusal(gone), "the same words a launch would be refused with");
        With(gone).Unrecognised.Should().Contain(PrivateFolder.RootRefusal(gone));
    }

    [Fact]
    public void ARootThatIsNotAFullLocalPath_IsAnUnreadableFolderProblem_NotAMissingOne()
    {
        var problem = With(OperatingSystem.IsWindows() ? @"\\server\share\homes" : "relative/homes").SettingProblems.Should().ContainSingle().Subject;

        problem.Kind.Should().Be(SettingProblemKinds.UnreadableFolder, "a mark saying 'missing' on a path that cannot be a local folder would lie");
    }

    [Fact]
    public void AFolderThatExists_AnUnsetRoot_AndAnEmptyOne_AreNoProblem()
    {
        With(_dir.Path).SettingProblems.Should().BeEmpty();
        With(null).SettingProblems.Should().BeEmpty("unset means the local application data, which is the server's own default");
        With(string.Empty).SettingProblems.Should().BeEmpty();
    }
}
