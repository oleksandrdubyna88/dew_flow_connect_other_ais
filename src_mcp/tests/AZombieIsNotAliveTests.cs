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
/// <para>The zombie is made by <see cref="ZombieMaker"/> (the one helper, C21/C29): a shell starts a background <c>sleep</c> and then <c>exec</c>s another
/// <c>sleep</c>, which never waits for children. The kill waits for that <c>exec</c>; a shell that has not yet exec'd may reap the dead child itself.</para>
/// </remarks>
public sealed class AZombieIsNotAliveTests
{
    [Theory]
    [InlineData("/bin/sh", "sleep 600 & echo $!; exec sleep 600")]
    [InlineData("/bin/bash", "sleep 600 & echo $!; sleep 2; exec sleep 600")]
    public async Task AKilledButUnreapedProcess_IsNotAlive(string shell, string script)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && File.Exists(shell), "a zombie is read from /proc, which Linux alone has");
        using var zombie = await ZombieMaker.StartAsync(shell, script);
        ProcessTree.IsAlive(zombie.Identity).Should().BeTrue("the background sleep is running");

        await zombie.KillToZombieAsync();

        ProcessTree.IsAlive(zombie.Identity).Should().BeFalse("a zombie has exited; only its table entry is left");
        (await ProcessTree.ConfirmGoneAsync([zombie.Identity])).Should().BeEmpty("a zombie is confirmed gone, not reported as a survivor");
    }
}
