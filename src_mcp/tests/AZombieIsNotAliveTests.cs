using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A killed process whose parent has not reaped it yet — a zombie — holds nothing any more, so the tree confirmation
/// must count it as gone (story 6.1a of <c>research/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>Found on Linux CI: a reader's grandchild, killed with the tree, kept its <c>/proc</c> entry until something
/// reaped it, and a check by pid and start time alone called it alive. Under a parent that never reaps — a server
/// running as a container's first process is one — that entry stays for ever, and the slot would stay unavailable for
/// ever over a process that has already exited.</para>
/// <para>The zombie is made by <see cref="ZombieMaker"/> (the one helper, C21/C29): the fake CLI's <c>unreaped-child</c> verb forks a child and never waits for it. An executable and an argv — the shell script
/// this test first ran (E5 checkpoint 2, F0) broke the rule to launch external processes as an executable plus argv, and had a race of its own: a shell that has not yet exec'd may reap the dead child itself.</para>
/// </remarks>
public sealed class AZombieIsNotAliveTests
{
    [Fact]
    public async Task AKilledButUnreapedProcess_IsNotAlive()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "a zombie is read from /proc, which Linux alone has");
        using var zombie = await ZombieMaker.StartAsync();
        ProcessTree.IsAlive(zombie.Identity).Should().BeTrue("the child is running until it is killed");

        await zombie.KillToZombieAsync();

        ProcessTree.IsAlive(zombie.Identity).Should().BeFalse("a zombie has exited; only its table entry is left");
        (await ProcessTree.ConfirmGoneAsync([zombie.Identity])).Should().BeEmpty("a zombie is confirmed gone, not reported as a survivor");
    }
}
