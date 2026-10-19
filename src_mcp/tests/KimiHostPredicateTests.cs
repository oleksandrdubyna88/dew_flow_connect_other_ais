using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ONE answer to "is this a host where Kimi's lock family exists" (the coai code gate, E5): every site that asks it asks <see cref="HostKinds.HasKimiLockFamily"/>, so a host is never admitted by the sign-in link and
/// refused by the inheritable hold, or the other way round. And the ONE decision of supervision on the shim's hold.
/// </summary>
public sealed class KimiHostPredicateTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-host-predicate-");

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(HostKind.Windows, true)]
    [InlineData(HostKind.Linux, true)]
    [InlineData(HostKind.Wsl, true)]
    [InlineData(HostKind.MacOs, false)]
    [InlineData(HostKind.Other, false)]
    public void TheLockFamilyExists_OnWindowsLinuxAndWsl_AndNowhereElse(HostKind host, bool expected) =>
        HostKinds.HasKimiLockFamily(host).Should().Be(expected);

    [Theory]
    [InlineData(HostKind.Windows)]
    [InlineData(HostKind.Linux)]
    [InlineData(HostKind.Wsl)]
    [InlineData(HostKind.MacOs)]
    [InlineData(HostKind.Other)]
    public void TheSignInLinkAndTheInheritableHold_AdmitExactlyTheSameHosts(HostKind host)
    {
        var link = _dir.At("never", "made");
        var linkAdmitsIt = !(Junctions.Refusal(host, link, _dir.Path).Contains("a sign-in link is made only on a host that admits Kimi", StringComparison.Ordinal));

        linkAdmitsIt.Should().Be(HeldFile.InheritableSupported(host), "two predicates for one family drift: a host admitted by one and refused by the other");
        linkAdmitsIt.Should().Be(HostKinds.HasKimiLockFamily(host));
    }

    [Fact]
    public void TheShimsSupervisionAndTheTeardown_AskTheOneDecision() =>
        KimiTreeSupervisor.IsSupervisedHost().Should().Be(OperatingSystem.IsLinux(), "a subreaper, a pidfd and a survivor marker are Linux's — and WSL is Linux");
}
