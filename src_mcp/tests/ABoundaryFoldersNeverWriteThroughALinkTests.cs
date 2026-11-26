using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Runners.Worktrees;
using FluentAssertions;
using System.Runtime.Versioning;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 follow-up, verify M15: <see cref="BoundaryFolders.Prepare"/> runs on the HOST, before every coder launch and every round of checks, and writes coai's own files at fixed names in folders that a unit can write — the marker
/// <c>.coai-job-&lt;id&gt;</c> in the unit's two temp folders and in the worktree, and the copy <c>.coai-preflight.exe</c> in the worktree. A coder with a shell, or a test file that a check runs, can leave a link at such a name. The host must never follow it:
/// the link's target keeps its bytes and its mode, and the name holds a regular file of coai's own afterwards.
/// </summary>
/// <remarks>
/// Made-up files in a temp folder only; never the real HOME. A link is a symbolic link, so the tests need a machine that can make one.
/// </remarks>
[UnsupportedOSPlatform("windows")]
public sealed class ABoundaryFoldersNeverWriteThroughALinkTests : IDisposable
{
    private const string Id = "r20261126101010-ab12cd34";

    private static readonly byte[] Precious = "precious bytes of a host file\n"u8.ToArray();

    private static readonly byte[] Program = "MZ made-up windows program\n"u8.ToArray();

    private readonly TempDir _bed = TempDir.For("coai-boundary-folders-");

    private string Outside => Path.Combine(_bed.Path, "outside");

    private string Worktree => Path.Combine(_bed.Path, "wt");

    private string ProgramFile => Path.Combine(_bed.Path, "windows-program.exe");

    public void Dispose() => _bed.Dispose();

    public static TheoryData<string> Markers => ["temp", "vartmp", "worktree"];

    public static TheoryData<string> Names => ["temp", "vartmp", "worktree", "copy"];

    private BoundaryJob Job()
    {
        Directory.CreateDirectory(Worktree);
        Directory.CreateDirectory(Outside);
        File.WriteAllBytes(ProgramFile, Program);

        return new BoundaryJob(Id, new CoderTree(Worktree, Path.Combine(_bed.Path, "main"), "gitdir: x\n"), Path.Combine(_bed.Path, "job"), NetworkChoice.None("t"), NetworkChoice.None("t"));
    }

    private BoundaryHost Host() =>
        new(BoundaryPlatform.Linux, Path.Combine(_bed.Path, "no-home"), string.Empty, string.Empty, "systemd-run", "systemctl", ProgramFile, string.Empty);

    private static string PathOf(BoundaryJob job, string place) => place switch
    {
        "temp" => Path.Combine(job.TempFolder, BoundaryPreflight.MarkerName(job)),
        "vartmp" => Path.Combine(job.VarTempFolder, BoundaryPreflight.MarkerName(job)),
        "worktree" => Path.Combine(job.Tree.Root, BoundaryPreflight.MarkerName(job)),
        _ => Path.Combine(job.Tree.Root, BoundaryPreflight.ProgramCopyName),
    };

    private (string Sentence, string Copy) Prepare(BoundaryJob job) =>
        BoundaryFolders.Prepare(Host(), job, job.Folders, job.TempFolder, job.VarTempFolder);

    private static void NeedLinks() => Assert.SkipWhen(OperatingSystem.IsWindows(), "a symbolic link needs a privilege on Windows");

    private string MadeUpTarget()
    {
        var target = Path.Combine(Outside, "a-host-file.txt");
        File.WriteAllBytes(target, Precious);
        File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);

        return target;
    }

    private static void PlantLink(string name, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(name)!);
        File.CreateSymbolicLink(name, target);
    }

    [Fact]
    public void WithNoLink_TheMarkersAreEmptyRegularFiles_AndTheCopyHoldsTheProgramWithMode0700()
    {
        NeedLinks();
        var job = Job();

        var (sentence, copy) = Prepare(job);

        sentence.Should().BeEmpty();
        foreach (var place in new[] { "temp", "vartmp", "worktree" })
        {
            RealPath.Lstat(PathOf(job, place)).Kind.Should().Be(EntryKind.File, place);
            new FileInfo(PathOf(job, place)).Length.Should().Be(0, place);
        }

        copy.Should().Be(PathOf(job, "copy"));
        File.ReadAllBytes(copy).Should().Equal(Program);
        File.GetUnixFileMode(copy).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void ALinkAtTheName_ToAFileOutsideTheWorktree_IsNeverFollowed_TheTargetKeepsItsBytesAndItsMode_AndTheNameHoldsAFileOfCoai(string place)
    {
        NeedLinks();
        var job = Job();
        var target = MadeUpTarget();
        var modeBefore = File.GetUnixFileMode(target);
        PlantLink(PathOf(job, place), target);

        var (sentence, copy) = Prepare(job);

        sentence.Should().BeEmpty();
        File.ReadAllBytes(target).Should().Equal(Precious, "the host file behind the link is never written");
        File.GetUnixFileMode(target).Should().Be(modeBefore, "its mode is never changed");
        RealPath.Lstat(PathOf(job, place)).Kind.Should().Be(EntryKind.File, "the link is replaced by a regular file of coai's own");
        if (place == "copy")
        {
            File.ReadAllBytes(PathOf(job, place)).Should().Equal(Program);
            copy.Should().Be(PathOf(job, place));
        }
        else
        {
            new FileInfo(PathOf(job, place)).Length.Should().Be(0);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void ALinkAtTheName_ToAFileThatIsNotThere_DoesNotMakeTheTarget(string place)
    {
        NeedLinks();
        var job = Job();
        var missing = Path.Combine(Outside, "not-there.lock");
        PlantLink(PathOf(job, place), missing);

        var (sentence, _) = Prepare(job);

        sentence.Should().BeEmpty();
        File.Exists(missing).Should().BeFalse("a link to a name that is not there makes nothing at that name");
        RealPath.Lstat(PathOf(job, place)).Kind.Should().Be(EntryKind.File);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void AFolderAtTheName_IsAStop_NotADeleteOfIt_AndNoProgramIsCopied(string place)
    {
        var job = Job();
        var folder = PathOf(job, place);
        Directory.CreateDirectory(folder);
        var inside = Path.Combine(folder, "keep.txt");
        File.WriteAllBytes(inside, Precious);

        var (sentence, copy) = Prepare(job);

        sentence.Should().NotBeEmpty("a folder at the name of coai's own file is a refusal, not something to write over");
        copy.Should().BeEmpty();
        File.ReadAllBytes(inside).Should().Equal(Precious, "the folder and what it holds are left as they were");
    }
}
