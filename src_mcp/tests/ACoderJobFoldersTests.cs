using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2, the retention of what a coder job leaves beside its record (<see cref="CoderJobFolders"/>): the mounts at the end of the work, the job's own folder with its record's retention — and nothing that is not a coder job's folder by name.
/// </summary>
public sealed class ACoderJobFoldersTests : IDisposable
{
    private const string Job = "r20261125223442-ad0c362a";

    private const string Other = "r20261125223442-bbbbbbbb";

    private readonly TempDir _jobs = TempDir.For("coai-coder-jobs-");

    private readonly List<string> _failures = [];

    public void Dispose() => _jobs.Dispose();

    private string Folder(string name) => Path.Combine(_jobs.Path, name);

    private string Make(string name, string file = "marker.txt")
    {
        var folder = Folder(name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file), "x\n");

        return folder;
    }

    private BoundaryJob BoundaryOf(string id) =>
        new(id, new CoderTree(Path.Combine(_jobs.Path, "wt-" + id), Path.Combine(_jobs.Path, "main"), "gitdir: x\n"), Folder(id), NetworkChoice.None("t"), NetworkChoice.None("t"));

    private Func<string, string?> Settings => name => name == CoderOsLayer.JobsVariable ? _jobs.Path : null;

    [Fact]
    public void ThePreflightsStandInOfAnotherJob_ALeftoverOfAKilledRunner_IsRemovedWithTheJob_AndAnotherJobsIsNot()
    {
        Make(Job);
        Make("coai-preflight-other-" + Job, "canary.txt");
        Make("coai-preflight-other-" + Other, "canary.txt");

        CoderJobFolders.RemoveOf(Settings, Job, _failures.AsFailureSink()).Should().BeTrue(string.Join("; ", _failures));

        Directory.Exists(Folder(Job)).Should().BeFalse();
        Directory.Exists(Folder("coai-preflight-other-" + Job)).Should().BeFalse("the stand-in a killed runner left is litter of this job and goes with it");
        Directory.Exists(Folder("coai-preflight-other-" + Other)).Should().BeTrue("another job's stand-in is not this job's to remove");
    }

    [Fact]
    public void TheMountsOfAJob_AreRemoved_ReadOnlyFilesIncluded_AndItsOwnFolderAndAnotherJobsMountsAreNot()
    {
        Make(Job);
        Make(Job + ".mounts");
        var source = Path.Combine(Folder(Job + ".mounts"), "m0", "rules");
        Directory.CreateDirectory(source);
        var file = Path.Combine(source, "rule.md");
        File.WriteAllText(file, "rule\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead);
            File.SetUnixFileMode(source, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        Make(Other + ".mounts");

        CoderJobFolders.RemoveMounts(BoundaryOf(Job), Job, _failures.AsFailureSink()).Should().BeTrue();

        Directory.Exists(Folder(Job + ".mounts")).Should().BeFalse();
        Directory.Exists(Folder(Job)).Should().BeTrue("the job's own folder is the job's retention's, not the end of its work's");
        Directory.Exists(Folder(Other + ".mounts")).Should().BeTrue("another job's mounts are not this job's");
        _failures.Should().BeEmpty();
    }

    [Fact]
    public void AJobWhoseFolderIsNotNamedForItsId_HasNothingRemoved()
    {
        Make("somebody-elses-folder");
        Make("somebody-elses-folder.mounts");

        CoderJobFolders.RemoveMounts(BoundaryOf("somebody-elses-folder"), Job, _failures.AsFailureSink()).Should().BeFalse("the folder is not named for the job id, so it is not a recorded coder job folder");
        CoderJobFolders.RemoveMounts(BoundaryOf("Not A Job Id"), "Not A Job Id", _failures.AsFailureSink()).Should().BeFalse("a name that is no job id is never spelled into a path");

        Directory.Exists(Folder("somebody-elses-folder.mounts")).Should().BeTrue();
    }

    [Fact]
    public void AMountsFolderThatIsALink_IsLeftAsItIs_NotFollowedAndNotRemoved()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege on Windows");
        using var elsewhere = TempDir.For("coai-elsewhere-");
        File.WriteAllText(Path.Combine(elsewhere.Path, "precious.txt"), "keep\n");
        Directory.CreateSymbolicLink(Folder(Job + ".mounts"), elsewhere.Path);

        CoderJobFolders.RemoveMounts(BoundaryOf(Job), Job, _failures.AsFailureSink()).Should().BeFalse("a link is not a folder of coai's: it is not followed and not removed");

        File.ReadAllText(Path.Combine(elsewhere.Path, "precious.txt")).Should().Be("keep\n");
        new DirectoryInfo(Folder(Job + ".mounts")).LinkTarget.Should().NotBeNull("the link itself is where it was: it is not a folder of coai's");
        CoderJobFolders.RemoveOf(Settings, Job, _failures.AsFailureSink()).Should().BeTrue("a link in the place of a folder is nothing of coai's to remove, and it must not keep a job's record for ever");
    }

    [Fact]
    public void ALinkInsideAJobFolder_IsRemovedByItsOwnName_AndItsTargetIsNeverRead()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege on Windows");
        using var elsewhere = TempDir.For("coai-elsewhere-");
        File.WriteAllText(Path.Combine(elsewhere.Path, "precious.txt"), "keep\n");
        var home = Path.Combine(Make(Job), "coder", "claude", "home");
        Directory.CreateDirectory(home);
        Directory.CreateSymbolicLink(Path.Combine(home, "to-a-folder"), elsewhere.Path);
        Directory.CreateSymbolicLink(Path.Combine(home, "to-proc"), "/proc");
        var read = new List<string>();
        GitScratch.Reading = read.Add;
        try
        {
            CoderJobFolders.RemoveOf(Settings, Job, _failures.AsFailureSink()).Should().BeTrue(string.Join("; ", _failures));
        }
        finally
        {
            GitScratch.Reading = null;
        }

        Directory.Exists(Folder(Job)).Should().BeFalse("a link made by a check, to /proc or to anywhere, does not stop the job folder's removal");
        read.Should().BeEmpty("the target of a link in a job folder is not listed, before or after: it may be /proc or a dropped share");
        File.ReadAllText(Path.Combine(elsewhere.Path, "precious.txt")).Should().Be("keep\n");
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void AFolderOfModeZeroMadeByACheck_IsRemoved_NotKeptWithItsRecordForEver()
    {
        Assert.SkipUnless(FolderHolds.CanMakeOneUndeletable, "a privileged Unix process ignores the mode");
        var locked = Path.Combine(Make(Job), "coder", "claude", "home", "locked");
        Directory.CreateDirectory(Path.Combine(locked, "inner"));
        File.WriteAllText(Path.Combine(locked, "inner", "f.txt"), "x\n");
        File.SetUnixFileMode(Path.Combine(locked, "inner"), UnixFileMode.None);
        File.SetUnixFileMode(locked, UnixFileMode.None);

        try
        {
            CoderJobFolders.RemoveOf(Settings, Job, _failures.AsFailureSink()).Should().BeTrue(string.Join("; ", _failures));
        }
        finally
        {
            if (Directory.Exists(locked))
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Exists(Folder(Job)).Should().BeFalse();
    }

    [Fact]
    public void AFolderThatCannotBeRemoved_StaysAndIsSaid_AndIsRemovedWhenItCan()
    {
        Assert.SkipUnless(FolderHolds.CanMakeOneUndeletable, "a privileged Unix process ignores the mode that makes a folder undeletable");
        Make(Job);

        using (FolderHolds.Undeletable(Folder(Job)))
        {
            CoderJobFolders.RemoveOf(Settings, Job, _failures.AsFailureSink()).Should().BeFalse("the job's record must stay while its folder does");
            _failures.Should().NotBeEmpty();
        }

        Make(Job);
        CoderJobFolders.RemoveOf(Settings, Job, _failures.AsFailureSink()).Should().BeTrue();
        Directory.Exists(Folder(Job)).Should().BeFalse();
    }

    [Fact]
    public void ARetainedCoderJob_TakesItsFolderAndItsMountsWithIt_AndNothingElseUnderTheCoderJobsFolder()
    {
        Make(Job);
        Make(Job + ".mounts");
        Make(Other);
        Make(Other + ".mounts");
        Make("notes-of-the-person");

        CoderJobFolders.RemoveOf(Settings, Job, _failures.AsFailureSink());

        Directory.Exists(Folder(Job)).Should().BeFalse();
        Directory.Exists(Folder(Job + ".mounts")).Should().BeFalse();
        Directory.Exists(Folder(Other)).Should().BeTrue();
        Directory.Exists(Folder(Other + ".mounts")).Should().BeTrue();
        Directory.Exists(Folder("notes-of-the-person")).Should().BeTrue();
        _failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../x")]
    [InlineData("")]
    [InlineData("A B")]
    public void AnIdThatIsNoJobId_RemovesNothing(string id)
    {
        Make(Job);
        var parent = Path.GetDirectoryName(_jobs.Path)!;

        CoderJobFolders.RemoveOf(Settings, id, _failures.AsFailureSink());

        Directory.Exists(Folder(Job)).Should().BeTrue();
        Directory.Exists(parent).Should().BeTrue();
        Directory.Exists(_jobs.Path).Should().BeTrue();
    }
}

internal static class FailureSink
{
    /// <summary>An <see cref="Action{T}"/> that writes the failure's message into <paramref name="list"/>.</summary>
    public static Action<Exception> AsFailureSink(this List<string> list) => e => list.Add(e.Message);
}
