using CoaiMcp.Runners.Files;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// DL6's network-filesystem paragraph (decided 2026-10-17 as a stated default, pending the owner — Q7 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>; E6): a sign-in folder on <c>nfs</c>, <c>nfs4</c>, <c>cifs</c>,
/// <c>smb3</c>, <c>fuse.sshfs</c>, <c>ceph</c> or <c>glusterfs</c> is refused with its cure by default, because a Kimi on ANOTHER machine sharing that home refreshes the same rotating token and no lock here can see it.
/// An explicit, default-off override turns the refusal into a one-line warning. Over a fake table, so it runs on every platform.
/// </summary>
public sealed class MountLocalityNetworkTests
{
    private const string Root = "523 504 8:96 / / rw,relatime - ext4 /dev/sdg rw,discard,errors=remount-ro,data=ordered\n";

    private const string Target = "/home/me/net/.kimi-code/credentials";

    private static string NetworkMount(string type) => $"620 523 0:55 / /home/me/net rw,relatime shared:300 - {type} srv:/export rw,vers=4.2,hard\n";

    private static MountVerdict Judge(string table, bool allowNetwork) =>
        MountLocality.Judge(Target, Target, () => table, _ => new MountIdentity.NotAsked(), allowNetwork);

    public static TheoryData<string> NetworkTypes => ["nfs", "nfs4", "cifs", "smb3", "fuse.sshfs", "ceph", "glusterfs"];

    [Theory]
    [MemberData(nameof(NetworkTypes))]
    public void ANetworkMount_IsNetwork(string type)
    {
        MountTable.Parse(NetworkMount(type)).Should().ContainSingle().Which.IsNetwork.Should().BeTrue($"{type} is a network filesystem");
    }

    [Theory]
    [InlineData("ext4")]
    [InlineData("xfs")]
    [InlineData("btrfs")]
    [InlineData("tmpfs")]
    [InlineData("overlay")]
    [InlineData("fuse.gocryptfs")]
    [InlineData("9p")]
    public void ALocalMount_IsNotNetwork(string type)
    {
        MountTable.Parse(NetworkMount(type)).Should().ContainSingle().Which.IsNetwork.Should().BeFalse($"{type} is not on the stated list of network types");
    }

    [Theory]
    [MemberData(nameof(NetworkTypes))]
    public void ASignInFolderOnNetworkStorage_IsRefused_ByDefault_WithItsCure(string type)
    {
        var table = Root + NetworkMount(type);

        var refusal = Judge(table, allowNetwork: false).Refusal;

        refusal.Should().StartWith($"your Kimi sign-in folder is on network storage ({type});", "a sign-in folder on an nfs mount was admitted")
            .And.Contain("a Kimi on another machine that shares this home can refresh the same sign-in and sign you out, and coai cannot see it")
            .And.Contain("Point `KIMI_CODE_HOME` at a folder on this machine's own disk (not under the network home) and sign in there once with `kimi login`")
            .And.Contain("or set `coai.kimiAllowNetworkSignIn` if you never run Kimi on another machine")
            .And.Contain(Target).And.Contain("DL6");
        MountLocality.Refusal(Target, Target, () => table, _ => new MountIdentity.NotAsked()).Should().Be(refusal, "the plain refusal is the default policy");
    }

    [Fact]
    public void WithTheOverride_ANetworkSignInFolder_IsAdmitted_WithAOneLineWarning()
    {
        var verdict = Judge(Root + NetworkMount("nfs4"), allowNetwork: true);

        verdict.Refusal.Should().BeEmpty("the override turns the refusal into a warning");
        verdict.Warning.Should().Contain("network storage (nfs4)").And.Contain("coai.kimiAllowNetworkSignIn").And.Contain("sign you out");
        verdict.Warning.Should().NotContain("\n", "one line on the card");
    }

    [Fact]
    public void AnExt4SignInFolder_IsAdmitted_Silently_WithOrWithoutTheOverride()
    {
        foreach (var allow in new[] { false, true })
        {
            var verdict = Judge(Root + NetworkMount("ext4"), allow);

            verdict.Should().Be(new MountVerdict(string.Empty, string.Empty), $"allowNetwork={allow}: a local disk is neither refused nor warned about");
        }
    }

    [Fact]
    public void ATargetOutsideTheNetworkMount_IsLocal_EvenWhenOneExists()
    {
        var table = Root + NetworkMount("nfs4");

        MountLocality.Judge("/home/me/local/credentials", "/home/me/local/credentials", () => table, _ => new MountIdentity.NotAsked(), allowNetwork: false)
            .Should().Be(new MountVerdict(string.Empty, string.Empty));
    }

    [Fact]
    public void TheOverride_NeverAdmitsAWindowsFilesystem_OrATableThatCannotBeRead()
    {
        const string drvfs = "600 523 0:210 / /home/me/net rw,relatime - 9p D: rw,aname=drvfs;path=D:;uid=1000\n";

        Judge(Root + drvfs, allowNetwork: true).Refusal.Should().Contain("is on a Windows filesystem", "the override is about network storage only");
        MountLocality.Judge(Target, Target, () => throw new IOException("denied"), _ => new MountIdentity.NotAsked(), allowNetwork: true).Refusal.Should().Contain("could not read");
    }
}
