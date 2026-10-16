using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ported mount table (DL6 and S-K12 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): a sign-in folder on a Windows filesystem is
/// refused by the TYPE of the mount that holds it, never by a prefix of its path. Pure text — these run on every platform.
/// </summary>
/// <remarks>
/// The vectors are wsl_care's own (its <c>WindowsSystemDriveTests</c> table, observed on the owner's WSL 2.7.10 machine,
/// 2026-10-04, unedited; wsl_care has no test file for the parser itself, so the vectors are what is re-homed here, with the
/// cases its retro rounds added: the escapes decoded, the super-options never decoded, a stacked mount, a short line).
/// </remarks>
public sealed class MountTableTests
{
    private const string Root = "523 504 8:96 / / rw,relatime - ext4 /dev/sdg rw,discard,errors=remount-ro,data=ordered\n";

    private const string DriversShare =
        "520 523 0:36 / /usr/lib/wsl/drivers ro,nosuid,nodev,noatime - 9p drivers ro,aname=drivers;fmask=222;dmask=222,cache=0x5,access=client,msize=65536,trans=fd,rfd=8,wfd=8\n";

    private const string ObservedC =
        @"487 523 0:159 / /mnt/c rw,noatime - 9p C:\134 rw,aname=drvfs;path=C:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6" + "\n";

    private const string Docker =
        @"1560 523 0:190 / /Docker/host rw,noatime - 9p C:\134Program\040Files\134Docker\134Docker\134resources rw,aname=drvfs;path=C:\Program Files\Docker\Docker\resources;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=3,wfd=3" + "\n";

    /// <summary>A drvfs mount at a point that is NOT under <c>/mnt/</c>: what <c>mount -t drvfs D: /data/win</c> writes.</summary>
    private const string CustomDrvfs =
        @"600 523 0:210 / /data/win rw,relatime - 9p D:\134 rw,aname=drvfs;path=D:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6" + "\n";

    private static IReadOnlyList<MountEntry> Table(params string[] lines) => MountTable.Parse(string.Concat(lines));

    [Fact]
    public void ADrvfsMountOutsideMnt_IsRefused_AndTheExt4Root_IsAdmitted()
    {
        var table = Table(Root, ObservedC, CustomDrvfs);

        MountTable.OnWindowsFilesystem(table, "/data/win/kimi/credentials")
            .Should().BeTrue("a sign-in folder on a drvfs mount outside /mnt/ must be refused, and it was admitted");
        MountTable.OnWindowsFilesystem(table, "/home/me/.kimi-code/credentials")
            .Should().BeFalse("the same table, a target on the ext4 root: a Linux filesystem, which a Linux lock can see");
    }

    [Fact]
    public void TheMntPrefixIsNotTheRule_AMountTypeIs()
    {
        var table = Table(Root, ObservedC);

        MountTable.OnWindowsFilesystem(table, "/mnt/c/Users/me/.kimi-code").Should().BeTrue("the 9p/drvfs mount of the system drive holds it");
        MountTable.OnWindowsFilesystem(Table(Root), "/mnt/c/Users/me/.kimi-code")
            .Should().BeFalse("with no drvfs mount in the table, /mnt/c is a plain folder on the root: the rule is the mount type, not the spelling");
    }

    [Theory]
    [InlineData("/data/win", true)]
    [InlineData("/data/win/", true)]
    [InlineData("/data/win/x", true)]
    [InlineData("/data/winter", false)]
    [InlineData("/data", false)]
    public void AMountHoldsItsOwnPointAndWhatIsBelowIt_NeverAFolderThatOnlySharesItsPrefix(string path, bool windows) =>
        MountTable.OnWindowsFilesystem(Table(Root, CustomDrvfs), path).Should().Be(windows, path);

    [Fact]
    public void TheDeepestMountThatHoldsAPathAnswers_AndTheLastLineOfAStackedMountWins()
    {
        var ext4Inside = "601 600 8:97 / /data/win/linux rw,relatime - ext4 /dev/sdh rw\n";
        var table = Table(Root, CustomDrvfs, ext4Inside);

        MountTable.Holding(table, "/data/win/linux/x")!.Type.Should().Be("ext4", "a Linux filesystem mounted inside a drvfs one is Linux");
        MountTable.Holding(table, "/data/win/other")!.IsDrvfs.Should().BeTrue();

        var stacked = Table(Root, ObservedC, ObservedC.Replace("487 523 0:159", "900 487 0:200", StringComparison.Ordinal));
        MountTable.Holding(stacked, "/mnt/c/x")!.Minor.Should().Be(200, "the line on top is the one a path reaches");
    }

    [Fact]
    public void ATableWithoutARoot_HoldsNothing_AndIsNotJudgedWindows()
    {
        var table = Table(ObservedC);

        MountTable.Holding(table, "/home/me").Should().BeNull();
        MountTable.OnWindowsFilesystem(table, "/home/me").Should().BeFalse();
        MountTable.OnWindowsFilesystem([], "/data/win").Should().BeFalse("an empty table holds nothing");
    }

    [Theory]
    [InlineData("9p", "rw,aname=drvfs;path=C:\\", true)]
    [InlineData("9p", "ro,aname=drivers;fmask=222", false)]
    [InlineData("drvfs", "rw,uid=1000", true)]
    [InlineData("virtiofs", "rw", true)]
    [InlineData("ext4", "rw,discard", false)]
    [InlineData("overlay", "rw,aname=drvfs", false)]
    public void ADrvfsMount_IsWslTwosNinePWithTheDrvfsAname_WslOnesDrvfs_Or_Virtiofs(string type, string options, bool drvfs) =>
        new MountEntry("/", "/x", 0, 1, type, "src", options.Split([',', ';'])).IsDrvfs.Should().Be(drvfs);

    [Fact]
    public void TheKernelsOctalEscapes_AreDecoded_InTheRootThePointAndTheSource()
    {
        var spaced = ObservedC.Replace(" /mnt/c ", @" /my\040drives/c ", StringComparison.Ordinal);

        var entry = Table(spaced).Should().ContainSingle().Subject;

        entry.MountPoint.Should().Be("/my drives/c");
        MountTable.OnWindowsFilesystem(Table(Root, spaced), "/my drives/c/Users").Should().BeTrue();

        var docker = Table(Docker).Should().ContainSingle().Subject;
        docker.Source.Should().Be(@"C:\Program Files\Docker\Docker\resources", @"\134 is a backslash and \040 a space");
        docker.MountPoint.Should().Be("/Docker/host");
    }

    [Fact]
    public void TheSuperOptions_AreNeverDecoded()
    {
        var line = @"503 523 0:173 / /mnt/c rw,noatime - 9p C:\134134 rw,aname=drvfs;path=C:\134;uid=1000" + "\n";

        var entry = Table(line).Should().ContainSingle().Subject;

        entry.Option("path").Should().Be(@"C:\134", @"a drvfs mount of a folder named C:\134 must not read as C:\, the whole drive");
        entry.Source.Should().Be(@"C:\134", @"the SOURCE is escaped by the kernel, so it is decoded once: the escape \134 is a backslash, and the 134 after it is text");
        entry.Option("absent").Should().BeEmpty("no such option is an empty value, never null");
    }

    [Fact]
    public void ALineWithoutItsFields_IsSkipped_AndTheRestIsRead()
    {
        var table = Table("not a mountinfo line at all\n", "1 2 3\n", "\n", Root, "523 504 8:96 / / rw - ext4\n", "1 2 x:y / /a rw - ext4 /dev/x rw\n", ObservedC);

        table.Select(e => e.MountPoint).Should().Equal("/", "/mnt/c");
        MountTable.Parse(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void TheObservedWsl2Table_ReadsEveryLine_WithItsDeviceAndType()
    {
        var table = Table(Root, DriversShare, ObservedC, CustomDrvfs);

        table.Should().HaveCount(4);
        table[0].Should().BeEquivalentTo(new { Root = "/", MountPoint = "/", Major = 8u, Minor = 96u, Type = "ext4", Source = "/dev/sdg" });
        table[1].IsDrvfs.Should().BeFalse("WSL's driver share is 9p but not drvfs: it holds no Windows folder of the person's");
        table[2].Should().BeEquivalentTo(new { MountPoint = "/mnt/c", Major = 0u, Minor = 159u, Type = "9p" });
        table[2].Options.Should().Contain("aname=drvfs").And.Contain(@"path=C:\");
    }
}
