using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 s6, the recorded change: the shared-instance tests of the key store skip when the process cannot set the setgid bit, decided by TRYING the chmod (<see cref="SetgidSupport"/>), not by an environment variable. Inside a check unit the chmod is refused with
/// "Operation not permitted" (found by the dry run: five tests red, none of them a fault of the key store). The seam is the chmod and the read-back, so each machine's answer is made here without needing the machine.
/// </summary>
public sealed class TheSetgidProbeTests
{
    private const UnixFileMode Shared = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.SetGroup;

    [Fact]
    public void AChmodTheKernelRefuses_IsNotAThrow_ItIsAFalseWithTheReason()
    {
        var probe = new SetgidSupport((_, _) => throw new IOException("Operation not permitted"), _ => Shared);

        var can = probe.TryApply("/any", Shared, out var why);

        can.Should().BeFalse();
        why.Should().Contain("Operation not permitted").And.Contain("setgid");
    }

    [Fact]
    public void AnUnauthorizedAccess_IsTheSameAnswer()
    {
        var probe = new SetgidSupport((_, _) => throw new UnauthorizedAccessException("denied"), _ => Shared);

        probe.TryApply("/any", Shared, out var why).Should().BeFalse();
        why.Should().Contain("denied");
    }

    [Fact]
    public void AFileSystemThatAcceptsTheCallAndKeepsNoBit_IsFalseToo()
    {
        var probe = new SetgidSupport((_, _) => { }, _ => Shared & ~UnixFileMode.SetGroup);

        probe.TryApply("/any", Shared, out var why).Should().BeFalse();
        why.Should().Contain("does not keep the setgid bit");
    }

    [Fact]
    public void AChmodThatSticks_IsTrue_AndTheModeAskedForIsTheModeSet()
    {
        UnixFileMode set = 0;
        var probe = new SetgidSupport((_, mode) => set = mode, _ => set);

        probe.TryApply("/any", Shared, out var why).Should().BeTrue(why);
        set.Should().Be(Shared);
        why.Should().BeEmpty();
    }

    [Fact]
    public void ARefusalOfSomethingElse_IsNotSwallowed()
    {
        var probe = new SetgidSupport((_, _) => throw new InvalidOperationException("a bug"), _ => Shared);

        var act = () => probe.TryApply("/any", Shared, out _);

        act.Should().Throw<InvalidOperationException>("only a refusal of the chmod is a machine that cannot; a bug is a failure");
    }

    [Fact]
    public void OnThisMachine_TheRealProbeAnswersWithoutThrowing_AndAgreesWithTheModeThatIsThere()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "modes are a Unix thing");
        using var temp = TempDir.For("coai-setgid-probe-");
        var folder = temp.Path;
        var can = SetgidSupport.Real.TryApply(folder, Shared, out var why);

        (can == ((SetgidSupport.Real.Get(folder) & UnixFileMode.SetGroup) != 0)).Should().BeTrue("the answer is what the folder shows afterwards: " + why);
    }
}
