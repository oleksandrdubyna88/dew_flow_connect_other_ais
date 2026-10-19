using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ported mount table (DL6 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): which mount holds a path, and what a mount is. The refusal built on it is
/// <see cref="MountLocality"/> (<c>MountLocalityTests</c>). Pure text — these run on every platform.
/// </summary>
/// <remarks>
/// wsl_care has no test file for the parser (only its callers' tests exercise it), so these are written fresh, over kernel-format lines — several are the
/// lines observed on the owner's WSL 2.7.10 machine, 2026-10-04, as wsl_care's own tests quote them.
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

    [Theory]
    [InlineData("/data/win", "/data/win")]
    [InlineData("/data/win/", "/data/win")]
    [InlineData("/data/win/x", "/data/win")]
    [InlineData("/data/winter", "/")]
    [InlineData("/data", "/")]
    public void AMountHoldsItsOwnPointAndWhatIsBelowIt_NeverAFolderThatOnlySharesItsPrefix(string path, string holder) =>
        MountTable.Holding(Table(Root, CustomDrvfs), path)!.MountPoint.Should().Be(holder, path);

    private const string Ext4Linux = "601 523 8:97 / /data/linux rw,relatime - ext4 /dev/sdh rw\n";

    /// <summary>A drvfs mounted LATER over <c>/data</c>, which hides the ext4 at <c>/data/linux</c> while the table still lists it.</summary>
    private const string DrvfsOverData =
        @"700 523 0:230 / /data rw,relatime - 9p D:\134 rw,aname=drvfs;path=D:\;uid=1000;gid=1000;symlinkroot=/mnt/,cache=0x5,access=client,msize=65536,trans=fd,rfd=6,wfd=6" + "\n";

    [Fact]
    public void TheMountId_IsTheFirstFieldOfTheLine()
    {
        var table = Table(Root, ObservedC, Ext4Linux);

        table.Select(e => e.Id).Should().Equal([523ul, 487ul, 601ul], "stx_mnt_id names a mount by this number");
        Table("x 523 8:96 / / rw - ext4 /dev/sdg rw\n").Should().BeEmpty("a line whose mount id is not a number is not a mountinfo line");
    }

    [Fact]
    public void AMountLaidOverAnother_DoesNotHideItFromTheKernelsAnswer_TheMountIsFoundByIdentity()
    {
        var table = Table(Root, Ext4Linux, DrvfsOverData);

        MountTable.Holding(table, "/data/linux/credentials")!.Type.Should().Be("ext4", "the TEXT rule still names the hidden ext4 — the reason the kernel is asked");

        var found = MountTable.Find(table, "/data/linux/credentials", new MountIdentity.Mounted(700, 0, 230)).Should().BeOfType<MountHolder.Found>().Subject.Entry;

        found.IsDrvfs.Should().BeTrue("the path really lies on the drvfs mounted over /data: fail OPEN was the defect");
        found.Id.Should().Be(700);
    }

    [Fact]
    public void AMountIdTheTableDoesNotList_IsUnproven_NeverGuessed() =>
        MountTable.Find(Table(Root, Ext4Linux), "/data/linux/x", new MountIdentity.Mounted(9999, 8, 97)).Should().BeOfType<MountHolder.Unproven>().Which.Why.Should().Contain("mount 9999");

    [Fact]
    public void WithoutAMountId_TheTextRuleStands_OnlyWhenTheDeviceAgrees()
    {
        var table = Table(Root, Ext4Linux, DrvfsOverData);

        MountTable.Find(table, "/data/linux/x", new MountIdentity.Device(0, 230)).Should().BeOfType<MountHolder.Unproven>("the text names ext4 8:97, the kernel says the file is on 0:230: a mount hides it")
            .Which.Why.Should().Contain("0:230").And.Contain("/data/linux");
        MountTable.Find(table, "/data/linux/x", new MountIdentity.Device(8, 97)).Should().BeOfType<MountHolder.Found>().Which.Entry.Type.Should().Be("ext4");
        MountTable.Find(Table(Root), "/home/me", new MountIdentity.Device(8, 96)).Should().BeOfType<MountHolder.Found>();
        MountTable.Find(Table(ObservedC), "/home/me", new MountIdentity.Device(8, 96)).Should().BeOfType<MountHolder.NoMount>("no mount holds it: nothing to compare");
    }

    [Fact]
    public void AnUnreadableKernelAnswer_ProvesNothing()
    {
        MountTable.Find(Table(Root), "/home/me", new MountIdentity.Unreadable("statx answered errno 13")).Should().BeOfType<MountHolder.Unproven>().Which.Why.Should().Contain("errno 13");
        MountTable.Find(Table(Root), "/home/me", new MountIdentity.NotAsked()).Should().BeOfType<MountHolder.Found>("nobody asked: the text rule, as before");
    }

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
    public void ATableWithoutARoot_HoldsNothing()
    {
        var table = Table(ObservedC);

        MountTable.Holding(table, "/home/me").Should().BeNull();
        MountTable.Holding([], "/data/win").Should().BeNull("an empty table holds nothing");
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
        MountTable.Holding(Table(Root, spaced), "/my drives/c/Users")!.IsDrvfs.Should().BeTrue();

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
