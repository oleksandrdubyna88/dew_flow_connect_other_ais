using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// DL6's rule (S-K12, S-K12b of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): a sign-in folder on a Windows filesystem is refused by the TYPE of the mount
/// that holds its real path, never by a prefix of the path. Over a fake table, so it runs on every platform.
/// </summary>
public sealed class MountLocalityTests
{
    private const string Root = "523 504 8:96 / / rw,relatime - ext4 /dev/sdg rw,discard,errors=remount-ro,data=ordered\n";

    private const string MntC =
        @"487 523 0:159 / /mnt/c rw,noatime - 9p C:\134 rw,aname=drvfs;path=C:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6" + "\n";

    /// <summary>A drvfs mount at a point NOT under <c>/mnt/</c>: what <c>mount -t drvfs D: /data/win</c> writes.</summary>
    private const string DataWin =
        @"600 523 0:210 / /data/win rw,relatime - 9p D:\134 rw,aname=drvfs;path=D:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6" + "\n";

    /// <summary>The text rule alone: no kernel answer (the paths here do not exist).</summary>
    private static string Refusal(string table, string target) => MountLocality.Refusal(target, target, () => table, _ => new MountIdentity.NotAsked());

    private static string Refusal(string table, string target, MountIdentity kernel) => MountLocality.Refusal(target, target, () => table, _ => kernel);

    private const string Ext4Linux = "601 523 8:97 / /data/linux rw,relatime - ext4 /dev/sdh rw\n";

    /// <summary>A drvfs mounted LATER over <c>/data</c>: it hides the ext4 at <c>/data/linux</c>.</summary>
    private const string DrvfsOverData =
        @"700 523 0:230 / /data rw,relatime - 9p D:\134 rw,aname=drvfs;path=D:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6" + "\n";

    [Fact]
    public void ADrvfsMountedOverAnExt4One_IsStillRefused_TheKernelNamesTheMountThatReallyHoldsThePath()
    {
        var table = Root + Ext4Linux + DrvfsOverData;
        var path = "/data/linux/kimi/credentials";

        Refusal(table, path).Should().BeEmpty("the TEXT rule picks the hidden ext4 — the fail-OPEN this test pins");
        Refusal(table, path, new MountIdentity.Mounted(700, 0, 230)).Should().Contain("is on a Windows filesystem").And.Contain("9p mounted at /data", "the kernel says the file is on mount 700, the drvfs");
        Refusal(table, path, new MountIdentity.Mounted(601, 8, 97)).Should().BeEmpty("the ext4 is the mount that really holds it when nothing is laid over it");
    }

    [Fact]
    public void AMountTheTableDoesNotList_OrAKernelThatCannotAnswer_Refuses()
    {
        Refusal(Root + Ext4Linux, "/data/linux/x", new MountIdentity.Mounted(9999, 8, 97)).Should().Contain("cannot prove which mount holds").And.Contain("mount 9999").And.Contain("refused");
        Refusal(Root + Ext4Linux, "/data/linux/x", new MountIdentity.Unreadable("statx answered errno 13")).Should().Contain("cannot prove which mount holds").And.Contain("errno 13").And.Contain("refused");
    }

    [Fact]
    public void WithoutAMountId_TheDeviceMustAgreeWithTheTextRule_ElseRefused()
    {
        var table = Root + Ext4Linux + DrvfsOverData;

        Refusal(table, "/data/linux/x", new MountIdentity.Device(0, 230)).Should().Contain("cannot prove which mount holds").And.Contain("0:230").And.Contain("refused", "the device is the drvfs's, the text names the ext4");
        Refusal(table, "/data/linux/x", new MountIdentity.Device(8, 97)).Should().BeEmpty("the device agrees with the ext4 the text names");
    }

    [Fact]
    public void TheRealKernel_NamesTheMountOfARealFolder_AndItIsInTheRealTable()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "statx and /proc/self/mountinfo are Linux's");
        using var dir = TempDir.For("testsToDelete-mountid-");

        var identity = MountLocality.Identify(dir.Path);
        var held = MountTable.Find(MountTable.Parse(File.ReadAllText(MountLocality.MountInfoPath)), dir.Path, identity);

        identity.Should().BeOfType<MountIdentity.Mounted>("every kernel from 5.8 fills stx_mnt_id; an older one answers Device");
        held.Should().BeOfType<MountHolder.Found>().Which.Entry.Id.Should().Be(((MountIdentity.Mounted)identity).MountId, "the id statx names is a line of the real mountinfo");
        MountLocality.Identify(dir.At("never-made")).Should().BeOfType<MountIdentity.Unreadable>("a path that does not exist proves no mount");
    }

    [Fact]
    public void ADrvfsMountOutsideMnt_IsRefused_AndTheExt4Root_IsAdmitted()
    {
        var table = Root + MntC + DataWin;

        Refusal(table, "/data/win/kimi/credentials").Should().Contain("is on a Windows filesystem")
            .And.Contain("9p mounted at /data/win").And.Contain("DL6", "a sign-in folder on a drvfs mount outside /mnt/ was admitted");
        Refusal(table, "/home/me/.kimi-code/credentials").Should().BeEmpty("the same table, a target on the ext4 root: a Linux filesystem, which a Linux lock can see");
    }

    [Fact]
    public void TheMountType_NotTheMntPrefix_IsTheRule()
    {
        Refusal(Root + MntC, "/mnt/c/Users/me/.kimi-code").Should().Contain("Windows filesystem", "the 9p/drvfs mount of the system drive holds it");
        Refusal(Root, "/mnt/c/Users/me/.kimi-code").Should().BeEmpty("with no drvfs mount in the table, /mnt/c is a plain folder on the root: the rule is the mount type");
    }

    [Theory]
    [InlineData("40 30 0:42 / /data/w rw,noatime - drvfs D:\\134 rw,uid=1000\n")]
    [InlineData("40 30 0:42 / /data/w rw,noatime - virtiofs drvfsTag rw\n")]
    public void WslOnesDrvfs_AndVirtiofs_AreWindowsFilesystemsToo(string line) =>
        Refusal(Root + line, "/data/w/x").Should().Contain("Windows filesystem");

    [Fact]
    public void ALinuxFilesystemMountedInsideADrvfsOne_IsLinux()
    {
        var inside = "601 600 8:97 / /data/win/linux rw,relatime - ext4 /dev/sdh rw\n";

        Refusal(Root + DataWin + inside, "/data/win/linux/credentials").Should().BeEmpty("the deepest mount that holds the path decides");
        Refusal(Root + DataWin + inside, "/data/win/other").Should().Contain("Windows filesystem");
    }

    [Fact]
    public void ARealPathThatIsNotTheTarget_IsWhatIsJudged()
    {
        var table = Root + DataWin;

        MountLocality.Refusal("/home/me/link", "/data/win/real", () => table, _ => new MountIdentity.NotAsked()).Should().StartWith("/home/me/link is on a Windows filesystem", "the sentence names the target the person gave; the mount is the real path's");
        MountLocality.Refusal("/data/win/link", "/home/me/real", () => table, _ => new MountIdentity.NotAsked()).Should().BeEmpty("a link under a drvfs mount whose real folder is on ext4 is on ext4");
    }

    [Fact]
    public void AnUnreadableTable_Refuses_NamingTheFailure()
    {
        var refusal = MountLocality.Refusal("/home/me/credentials", "/home/me/credentials", () => throw new UnauthorizedAccessException("denied"));

        refusal.Should().Contain("could not read /proc/self/mountinfo").And.Contain("UnauthorizedAccessException").And.Contain("refused", "a mountinfo coai could not read was read as 'local'");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a mountinfo line at all\n1 2 3\n")]
    [InlineData(MntC)]
    public void ATableNoMountOfWhichHoldsThePath_Refuses(string table) =>
        Refusal(table, "/home/me/credentials").Should().Contain("no mount in /proc/self/mountinfo holds /home/me/credentials").And.Contain("refused", "a table without / holds nothing, and nothing is not 'local'");
}
