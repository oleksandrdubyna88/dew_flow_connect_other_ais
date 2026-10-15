using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A killed process whose parent has not reaped it yet — a zombie — holds nothing any more, so the tree confirmation
/// must count it as gone (story 6.1a of <c>todo/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para>Found on Linux CI: a reader's grandchild, killed with the tree, kept its <c>/proc</c> entry until something
/// reaped it, and a check by pid and start time alone called it alive. Under a parent that never reaps — a server
/// running as a container's first process is one — that entry stays for ever, and the slot would stay unavailable for
/// ever over a process that has already exited.</para>
/// <para>The zombie is made the plain way: a shell starts a background <c>sleep</c> and then <c>exec</c>s another
/// <c>sleep</c>, which never waits for children. Killing the background one leaves it a zombie for as long as the
/// outer one lives.</para>
/// </remarks>
public sealed class AZombieIsNotAliveTests
{
    [Fact]
    public async Task AKilledButUnreapedProcess_IsNotAlive()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "a zombie is read from /proc, which Linux alone has");
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in new[] { "-c", "sleep 600 & echo $!; exec sleep 600" })
        {
            start.ArgumentList.Add(arg);
        }

        using var parent = Process.Start(start)!;
        try
        {
            var pid = int.Parse((await parent.StandardOutput.ReadLineAsync())!.Trim());
            var identity = new ProcessIdentity(pid, ProcessTracking.StartedAt(pid)!.Value);
            ProcessTree.IsAlive(identity).Should().BeTrue("the background sleep is running");

            using (var child = Process.GetProcessById(pid))
            {
                child.Kill();
            }

            (await BecomesZombieAsync(pid)).Should().BeTrue("the outer sleep never reaps, so the killed child stays a zombie");
            ProcessTree.IsAlive(identity).Should().BeFalse("a zombie has exited; only its table entry is left");
            (await ProcessTree.ConfirmGoneAsync([identity])).Should().BeEmpty("a zombie is confirmed gone, not reported as a survivor");
        }
        finally
        {
            parent.Kill(entireProcessTree: true);
        }
    }

    private static async Task<bool> BecomesZombieAsync(int pid)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            var stat = await File.ReadAllTextAsync($"/proc/{pid}/stat");
            if (stat[(stat.LastIndexOf(')') + 1)..].TrimStart().StartsWith('Z'))
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }
}
