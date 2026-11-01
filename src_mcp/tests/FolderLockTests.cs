using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The one way a sweep removes a folder whose owner holds a lock file in it (<see cref="FolderLock"/>): only when the sweep can take
/// that lock itself. Two shapes — nothing to do first, and a step asked under the lock that may keep the folder.
/// </summary>
public sealed class FolderLockTests : IDisposable
{
    private const string LockName = ".lock";

    private readonly TempDir _dir = TempDir.For("testsToDelete-folder-lock-");

    public void Dispose() => _dir.Dispose();

    private string Folder()
    {
        var folder = Directory.CreateDirectory(_dir.At("owned")).FullName;
        File.WriteAllText(Path.Combine(folder, "deep.txt"), "x");

        return folder;
    }

    [Fact]
    public void AFolderWhoseLockIsFree_IsRemovedWithoutAnyStepFirst()
    {
        var folder = Folder();

        FolderLock.RemoveIfFree(folder, LockName, GitScratch.DeleteEntry).Should().BeTrue();

        Directory.Exists(folder).Should().BeFalse("the lock was free, and nothing else was asked");
    }

    [Fact]
    public void AFolderWhoseLockIsHeld_StaysWithEverythingInIt_AndTheStepIsNeverAsked()
    {
        var folder = Folder();
        var asked = false;
        using var held = (HeldFile.Take(Path.Combine(folder, LockName)) as Hold.Taken)!.Stream;

        FolderLock.RemoveIfFree(folder, LockName, GitScratch.DeleteEntry, () => asked = true).Should().BeFalse();

        asked.Should().BeFalse("the step is asked only under the lock");
        File.Exists(Path.Combine(folder, "deep.txt")).Should().BeTrue();
    }

    [Fact]
    public void AStepThatSaysNo_KeepsTheFolder_ThisTime()
    {
        var folder = Folder();

        FolderLock.RemoveIfFree(folder, LockName, GitScratch.DeleteEntry, static () => false).Should().BeFalse();

        File.Exists(Path.Combine(folder, "deep.txt")).Should().BeTrue("the step said the folder must stay, so nothing was deleted");
    }

    [Fact]
    public void AStepThatSaysYes_LetsTheFolderGo()
    {
        var folder = Folder();

        FolderLock.RemoveIfFree(folder, LockName, GitScratch.DeleteEntry, static () => true).Should().BeTrue();

        Directory.Exists(folder).Should().BeFalse();
    }
}
