using System.Runtime.Versioning;
using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ported file status and its <c>statx</c> read (DL7 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): a folder is named by its device and inode, never by its
/// spelling. The layout half is pure and runs on every platform; the half that asks the kernel runs on Linux.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class FileStatusTests
{
    private const uint TypeAndInode = 0x101;

    /// <summary>A <c>struct statx</c> as the kernel lays it out: mask at 0, uid at 20, mode at 28, inode at 32, device at 136 / 140.</summary>
    private static byte[] Statx(uint mask, uint uid, ushort mode, ulong inode, uint major, uint minor, ulong mountId = 0)
    {
        var statx = new byte[256];
        BitConverter.TryWriteBytes(statx.AsSpan(144), mountId);
        BitConverter.TryWriteBytes(statx.AsSpan(0), mask);
        BitConverter.TryWriteBytes(statx.AsSpan(20), uid);
        BitConverter.TryWriteBytes(statx.AsSpan(28), mode);
        BitConverter.TryWriteBytes(statx.AsSpan(32), inode);
        BitConverter.TryWriteBytes(statx.AsSpan(136), major);
        BitConverter.TryWriteBytes(statx.AsSpan(140), minor);

        return statx;
    }

    [Fact]
    public void TheStatxLayout_IsReadAtTheKernelsOffsets()
    {
        var status = LinuxStatx.Status(Statx(mask: 0x7FF, uid: 1000, mode: 0x41ED, inode: 0x1122334455667788, major: 8, minor: 96));

        status.Should().Be(new FileStatus(Type: 0x4000, Permissions: 0x1ED, OwnerUid: 1000, Inode: 0x1122334455667788, DeviceMajor: 8, DeviceMinor: 96));
        status.IsDirectory.Should().BeTrue();
        status.IsRegular.Should().BeFalse();
        status.IsSymbolicLink.Should().BeFalse();
    }

    [Theory]
    [InlineData(0x8000, true, false, false)]
    [InlineData(0x4000, false, true, false)]
    [InlineData(0xA000, false, false, true)]
    [InlineData(0x1000, false, false, false)]
    public void TheTypeBits_NameTheKindOfFile(int mode, bool regular, bool directory, bool link)
    {
        var status = LinuxStatx.Status(Statx(0x7FF, 0, (ushort)mode, 1, 0, 1));

        (status.IsRegular, status.IsDirectory, status.IsSymbolicLink).Should().Be((regular, directory, link));
    }

    [Fact]
    public void TheSameFile_IsOneDeviceAndOneInode_WhateverElseDiffers()
    {
        var file = new FileStatus(0x8000, 0x1A4, 1000, 77, 8, 1);

        file.SameFileAs(file with { OwnerUid = 0, Permissions = 0, Type = 0x4000 }).Should().BeTrue("owner, mode and type are not identity");
        file.SameFileAs(file with { Inode = 78 }).Should().BeFalse("another inode is another file");
        file.SameFileAs(file with { DeviceMajor = 9 }).Should().BeFalse("the same inode number on another device is another file");
        file.SameFileAs(file with { DeviceMinor = 2 }).Should().BeFalse();
    }

    [Fact]
    public void AnAnswerWithoutTheInodeItWasAskedFor_IsAFailure_NeverAnIdentityOfZero()
    {
        LinuxStatx.Answered(Statx(mask: 0x7FF, 0, 0x41ED, 5, 8, 1), TypeAndInode).Should().BeOfType<StatxRead.Read>();
        LinuxStatx.Answered(Statx(mask: 0x7FF & ~0x100u, 0, 0x41ED, 0, 8, 1), TypeAndInode)
            .Should().Be(new StatxRead.Failed(0), "a filesystem that did not fill stx_ino has no identity to key on, and a zero inode must not become one");
        LinuxStatx.Answered(Statx(mask: LinuxStatx.StatxUid, 1000, 0x41ED, 0, 8, 1), LinuxStatx.StatxUid).Should().BeOfType<StatxRead.Read>("an inode nobody asked for is not required");
    }

    [Theory]
    [InlineData(LinuxStatx.StatxType | LinuxStatx.StatxIno, LinuxStatx.StatxIno, "the type")]
    [InlineData(LinuxStatx.StatxType | LinuxStatx.StatxIno, LinuxStatx.StatxType, "the inode")]
    [InlineData(LinuxStatx.StatxUid, 0u, "the owner")]
    [InlineData(LinuxStatx.StatxType | LinuxStatx.StatxIno | LinuxStatx.StatxMntId, LinuxStatx.StatxType | LinuxStatx.StatxIno, "the mount id")]
    public void AnAnswerWithoutAnyTrustedFieldItWasAskedFor_IsAFailure(uint asked, uint filled, string missing) =>
        LinuxStatx.Answered(Statx(filled, 1000, 0x41ED, 5, 8, 1, mountId: 77), asked)
            .Should().Be(new StatxRead.Failed(0), $"the kernel did not fill {missing}, so its zero is not a value (only the inode was checked before)");

    [Fact]
    public void TheMountId_IsReadOnlyWhenItWasAskedForAndFilled()
    {
        var asked = LinuxStatx.StatxType | LinuxStatx.StatxIno | LinuxStatx.StatxMntId;

        LinuxStatx.Answered(Statx(asked, 0, 0x41ED, 5, 8, 1, mountId: 700), asked).Should().BeOfType<StatxRead.Read>().Which.Status.MountId.Should().Be(700);
        LinuxStatx.Answered(Statx(asked, 0, 0x41ED, 5, 8, 1, mountId: 700), LinuxStatx.StatxType | LinuxStatx.StatxIno).Should().BeOfType<StatxRead.Read>().Which.Status.MountId.Should().Be(0, "not asked, not read");
    }

    // ---------- the real call: Linux only ----------

    private static FileStatus Found(StatxRead result) => result.Should().BeOfType<StatxRead.Read>().Subject.Status;

    private static FileStatus Of(string path) => Found(LinuxStatx.OfPath(path, 0, TypeAndInode));

    [Fact]
    public void AFileAndAFolder_AreReadWithTheirKind_AndAMissingPathIsEnoent()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx is a Linux call");
        using var dir = TempDir.For("testsToDelete-statx-");
        var file = dir.At("f.txt");
        File.WriteAllText(file, "x");

        Of(file).IsRegular.Should().BeTrue();
        Of(dir.Path).IsDirectory.Should().BeTrue();
        LinuxStatx.OfPath(dir.At("never-made"), 0, TypeAndInode).Should().Be(new StatxRead.Failed(LinuxStatx.NoEntry), "a deleted folder has no identity");
        Found(LinuxStatx.OfPath(file, 0, LinuxStatx.StatxBasic)).Permissions.Should().Be((int)File.GetUnixFileMode(file));
    }

    [Fact]
    public void ASymlinkedSpelling_IsTheSameFolder_WhenFollowed_AndItsOwnFile_WhenNot()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx is a Linux call");
        using var dir = TempDir.For("testsToDelete-statx-");
        var real = Directory.CreateDirectory(dir.At("persons", "credentials")).FullName;
        var alias = dir.At("alias");
        Directory.CreateSymbolicLink(alias, dir.At("persons"));
        var viaAlias = Path.Combine(alias, "credentials");

        Of(viaAlias).SameFileAs(Of(real)).Should().BeTrue("two spellings of one folder are one identity (S-K3)");
        Of(alias).SameFileAs(Of(dir.At("persons"))).Should().BeTrue("a final link is followed");
        var link = Found(LinuxStatx.OfPath(alias, LinuxStatx.AtSymlinkNoFollow, TypeAndInode));
        link.IsSymbolicLink.Should().BeTrue("AT_SYMLINK_NOFOLLOW describes the link itself");
        link.SameFileAs(Of(dir.At("persons"))).Should().BeFalse();
        Of(real).SameFileAs(Of(dir.Path)).Should().BeFalse("two folders are two identities");
    }

    [Fact]
    public void ProcSelfCwd_IsTheSameFolderAsItsRealPath()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx and /proc are Linux's");

        Of("/proc/self/cwd").SameFileAs(Of(Directory.GetCurrentDirectory())).Should().BeTrue("a /proc/self/cwd spelling is the same folder (DL7's alias set)");
    }

    [Fact]
    public void AnOpenDescriptor_IsTheSameFileAsItsPath_AndANewFileAtThePath_IsNot()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx is a Linux call");
        using var dir = TempDir.For("testsToDelete-statx-");
        var path = dir.At("lock");
        using var held = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        var viaHandle = Found(LinuxStatx.OfHandle(held.SafeFileHandle, LinuxStatx.StatxBasic));
        viaHandle.SameFileAs(Found(LinuxStatx.OfPath(path, LinuxStatx.AtSymlinkNoFollow, LinuxStatx.StatxBasic))).Should().BeTrue("the descriptor names the file the path names");

        File.Delete(path);
        File.WriteAllText(path, "replaced");

        viaHandle.SameFileAs(Found(LinuxStatx.OfPath(path, LinuxStatx.AtSymlinkNoFollow, LinuxStatx.StatxBasic))).Should().BeFalse("a file unlinked and made again is another file: the verify-after-take comparison");
    }
}
