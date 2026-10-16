using System.Runtime.Versioning;
using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ported file status and its <c>statx</c> read (DL7 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): a folder is named by its device and
/// inode, never by its spelling. The layout half is pure and runs on every platform; the half that asks the kernel runs on Linux.
/// </summary>
public sealed class FileStatusTests
{
    /// <summary>A <c>struct statx</c> as the kernel lays it out: mask at 0, uid at 20, mode at 28, inode at 32, device at 136 / 140.</summary>
    private static byte[] Statx(uint mask, uint uid, ushort mode, ulong inode, uint major, uint minor)
    {
        var statx = new byte[256];
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
        var status = FileStatus.FromStatx(Statx(mask: 0x7FF, uid: 1000, mode: 0x41C0 | 0x1ED, inode: 0x1122334455667788, major: 8, minor: 96));

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
        var status = FileStatus.FromStatx(Statx(0x7FF, 0, (ushort)mode, 1, 0, 1));

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
    public void AnAnswerWithoutAnInode_IsAFailure_NeverAnIdentityOfZero()
    {
        StatxFile.Answered(Statx(mask: 0x7FF, 0, 0x41ED, 5, 8, 1)).Should().BeOfType<StatResult.Found>();
        StatxFile.Answered(Statx(mask: 0x7FF & ~0x100u, 0, 0x41ED, 0, 8, 1))
            .Should().Be(new StatResult.Failed(0), "a filesystem that did not fill stx_ino has no identity to key on, and a zero inode must not become one");
    }

    // ---------- the real call: Linux only ----------

    [SupportedOSPlatform("linux")]
    private static FileStatus Found(StatResult result) =>
        result.Should().BeOfType<StatResult.Found>().Subject.Status;

    [Fact]
    [SupportedOSPlatform("linux")]
    public void AFileAndAFolder_AreReadWithTheirKind_AndAMissingPathIsEnoent()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx is a Linux call");
        using var dir = TempDir.For("testsToDelete-statx-");
        var file = dir.At("f.txt");
        File.WriteAllText(file, "x");

        Found(StatxFile.Of(file)).IsRegular.Should().BeTrue();
        Found(StatxFile.Of(dir.Path)).IsDirectory.Should().BeTrue();
        StatxFile.Of(dir.At("never-made")).Should().Be(new StatResult.Failed(StatxFile.NoEntry), "a deleted folder has no identity");
        Found(StatxFile.Of(file)).Permissions.Should().Be((int)File.GetUnixFileMode(file));
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void ASymlinkedSpelling_IsTheSameFolder_WhenFollowed_AndItsOwnFile_WhenNot()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx is a Linux call");
        using var dir = TempDir.For("testsToDelete-statx-");
        var real = Directory.CreateDirectory(dir.At("persons", "credentials")).FullName;
        var alias = dir.At("alias");
        Directory.CreateSymbolicLink(alias, dir.At("persons"));
        var viaAlias = Path.Combine(alias, "credentials");

        Found(StatxFile.Of(viaAlias)).SameFileAs(Found(StatxFile.Of(real))).Should().BeTrue("two spellings of one folder are one identity (S-K3)");
        Found(StatxFile.Of(alias)).SameFileAs(Found(StatxFile.Of(dir.At("persons")))).Should().BeTrue("a final link is followed");
        Found(StatxFile.OfLink(alias)).IsSymbolicLink.Should().BeTrue("OfLink describes the link itself");
        Found(StatxFile.OfLink(alias)).SameFileAs(Found(StatxFile.Of(dir.At("persons")))).Should().BeFalse();
        Found(StatxFile.Of(real)).SameFileAs(Found(StatxFile.Of(dir.Path))).Should().BeFalse("two folders are two identities");
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void ProcSelfCwd_IsTheSameFolderAsItsRealPath()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx and /proc are Linux's");

        Found(StatxFile.Of("/proc/self/cwd")).SameFileAs(Found(StatxFile.Of(Directory.GetCurrentDirectory())))
            .Should().BeTrue("a /proc/self/cwd spelling is the same folder (DL7's alias set)");
    }
}