using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// DL6's rule (S-K12, S-K12b of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): a sign-in folder on a Windows filesystem is refused by the TYPE of the mount
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

    private static string Refusal(string table, string target) => MountLocality.Refusal(target, target, () => table);

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

        MountLocality.Refusal("/home/me/link", "/data/win/real", () => table).Should().StartWith("/home/me/link is on a Windows filesystem", "the sentence names the target the person gave; the mount is the real path's");
        MountLocality.Refusal("/data/win/link", "/home/me/real", () => table).Should().BeEmpty("a link under a drvfs mount whose real folder is on ext4 is on ext4");
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
