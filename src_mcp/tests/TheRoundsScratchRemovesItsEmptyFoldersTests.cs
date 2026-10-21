using CoaiMcp.Runners.Snapshots;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round's scratch removes the always-empty folders its round made — <c>coai-repair-</c> and <c>coai-noworkspace-</c> — when it is disposed (D4 of
/// <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>). By the PATH it was given, never a name pattern; NON-recursive, so only an
/// EMPTY folder goes; a folder that cannot go is said once and left to the six-hour sweep; nothing is ever thrown.
/// </summary>
/// <remarks>The round-level twins (a real code round, the repair launch, a surviving descendant) are in <c>PanelServiceTests.EmptyFolders.cs</c> (S7).</remarks>
public sealed class TheRoundsScratchRemovesItsEmptyFoldersTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-roundscratch-empty-");
    private readonly List<string> _notes = [];

    public void Dispose() => _dir.Dispose();

    private RoundScratch Scratch() => RoundScratch.At(_dir.At("round"), _notes.Add);

    private string Folder(string name)
    {
        var folder = _dir.At(name);
        Directory.CreateDirectory(folder);

        return folder;
    }

    [Fact]
    public void AnEmptyFolderTheScratchOwns_IsRemovedWhenTheScratchEnds()
    {
        var scratch = Scratch();
        var repair = Folder("coai-repair-aaaa");
        var workspace = Folder("coai-noworkspace-bbbb");
        scratch.Owns(repair);
        scratch.Owns(workspace);

        scratch.Dispose();

        Directory.Exists(repair).Should().BeFalse("the round's empty repair folder went with the round");
        Directory.Exists(workspace).Should().BeFalse("the round's empty workspace folder went with the round");
        _notes.Should().BeEmpty("a removal that worked says nothing");
    }

    [Fact]
    public void AnOwnedFolder_IsRemovedByItsRecordedPath_AndASiblingWithTheSamePrefixIsNotTouched()
    {
        var scratch = Scratch();
        var mine = Folder("coai-repair-mine");
        var another = Folder("coai-repair-another-round");
        scratch.Owns(mine);

        scratch.Dispose();

        Directory.Exists(mine).Should().BeFalse();
        Directory.Exists(another).Should().BeTrue("another round's folder is not this round's: it is not recorded, and a name pattern is never used");
    }

    [Fact]
    public void AFolderThatIsNotEmpty_IsKeptWithItsFile_AndWarnedAboutOnce_AndNothingIsThrown()
    {
        var scratch = Scratch();
        var repair = Folder("coai-repair-full");
        var planted = Path.Combine(repair, "somebody-wrote-this.txt");
        File.WriteAllText(planted, "x");
        scratch.Owns(repair);

        var act = scratch.Dispose;

        act.Should().NotThrow("a failed removal never fails the round");
        File.Exists(planted).Should().BeTrue("the removal is NOT recursive: what is in the folder is not the round's to delete");
        _notes.Should().ContainSingle().Which.Should().Contain(repair).And.Contain("six-hour");
    }

    [Fact]
    public void AFolderThatIsAlreadyGone_IsNothing_AndNothingIsSaid()
    {
        var scratch = Scratch();
        scratch.Owns(_dir.At("coai-repair-gone"));

        var act = scratch.Dispose;

        act.Should().NotThrow();
        _notes.Should().BeEmpty();
    }

    [Fact]
    public void AFolderThatCannotBeRemoved_IsWarnedAboutOnce_AndNothingIsThrown()
    {
        Assert.SkipUnless(FolderHolds.CanMakeOneUndeletable, "a privileged Unix process removes what the mode forbids");
        var scratch = Scratch();
        var repair = Folder("coai-repair-held");
        scratch.Owns(repair);

        Action act;
        using (FolderHolds.Undeletable(repair))
        {
            act = scratch.Dispose;
            act.Should().NotThrow("a failed removal never fails the round");
        }

        Directory.Exists(repair).Should().BeTrue("it could not be removed, so the six-hour sweep takes it");
        _notes.Should().ContainSingle().Which.Should().Contain(repair).And.Contain("six-hour");
    }

    [Fact]
    public void TheScratchThatIsNone_TakesNoRegistration_ItIsOneObjectEveryScratchlessBuildShares()
    {
        var folder = Folder("coai-repair-of-a-scratchless-build");

        RoundScratch.None.Owns(folder);
        RoundScratch.None.Dispose();

        RoundScratch.None.OwnedFolders.Should().BeEmpty("a list on the shared object would grow for ever and delete across rounds");
        Directory.Exists(folder).Should().BeTrue();
    }

    [Fact]
    public void TheRegistry_IsCarriedBy_With_SoTheScratchTheRoundEndsRemovesWhatWasRegisteredBeforeIt()
    {
        var before = Scratch();
        var repair = Folder("coai-repair-registered-early");
        before.Owns(repair);
        var theRounds = before.With(new SnapshotOutcome.Refused("no snapshot in this test"));
        var late = Folder("coai-noworkspace-registered-late");
        theRounds.Owns(late);

        theRounds.Dispose();

        Directory.Exists(repair).Should().BeFalse("registered on the scratch the round was prepared with, removed by the one the round holds");
        Directory.Exists(late).Should().BeFalse();
    }
}
