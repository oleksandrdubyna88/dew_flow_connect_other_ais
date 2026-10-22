using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ONE way a test here makes a zombie (C21 and C29 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md</c>, §3.2–§3.3): a shell starts a background <c>sleep</c>, prints its pid
/// and becomes ANOTHER <c>sleep</c> that never waits for children — killing the background one then leaves it a zombie for as long as the outer one lives.
/// </summary>
/// <remarks>
/// <para><b>The kill waits for the parent to BE the outer <c>sleep</c>.</b> Until the shell has <c>exec</c>ed it is still a shell, and a shell that collects a dead background child (bash does, while it waits for a
/// foreground command) leaves no zombie at all: the entry is reaped before anyone can observe it. The wait reads the parent's <c>comm</c> through <see cref="ProcStat"/>, the one parser of
/// <c>/proc/&lt;pid&gt;/stat</c>; a second reader here would throw <see cref="DirectoryNotFoundException"/> on the reaped entry instead of reading it as <see cref="ProcStatRead.Gone"/>.</para>
/// <para>Both waits are bounded by <see cref="Patience"/> and each fails with a sentence that names what it was waiting for and the last thing it read.</para>
/// </remarks>
internal sealed class ZombieMaker : IDisposable
{
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly Process _parent;

    private ZombieMaker(Process parent, int pid, ProcessIdentity identity)
    {
        _parent = parent;
        Pid = pid;
        Identity = identity;
    }

    /// <summary>The background <c>sleep</c> that will be killed.</summary>
    public int Pid { get; }

    /// <summary>Who it is: its pid with the start time it had when the script printed the pid.</summary>
    public ProcessIdentity Identity { get; }

    /// <summary>Starts <paramref name="shell"/> on <paramref name="script"/> (which prints the background pid and ends in <c>exec sleep</c>) and returns once the shell has become the outer <c>sleep</c>.</summary>
    public static async Task<ZombieMaker> StartAsync(string shell, string script)
    {
        var info = new ProcessStartInfo(shell) { UseShellExecute = false, RedirectStandardOutput = true, ArgumentList = { "-c", script } };
        var parent = Process.Start(info)!;
        try
        {
            var line = await parent.StandardOutput.ReadLineAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
            var pid = int.Parse(line!.Trim(), System.Globalization.CultureInfo.InvariantCulture);
            await ParentBecomesSleepAsync(parent.Id);

            return new ZombieMaker(parent, pid, new ProcessIdentity(pid, ProcessTracking.StartedAt(pid)!.Value));
        }
        catch
        {
            End(parent);
            throw;
        }
    }

    /// <summary>Kills the background <c>sleep</c> and waits until <c>/proc</c> shows it as a zombie (state <c>Z</c>).</summary>
    public async Task KillToZombieAsync()
    {
        using (var child = Process.GetProcessById(Pid))
        {
            child.Kill();
        }

        var last = "(not read)";
        var zombie = await Polls.Until(() => IsZombie(ref last), Patience);

        zombie.Should().BeTrue($"the outer sleep never reaps, so the killed child {Pid} should have stayed a zombie; its /proc entry read: {last}");
    }

    public void Dispose() => End(_parent);

    private bool IsZombie(ref string last)
    {
        var read = ProcStat.ReadFor(Pid, ProcReads.Real);
        last = read switch
        {
            ProcStatRead.Read { Line: var line } => $"state {line.State}",
            ProcStatRead.Gone => "gone - the killed child's /proc entry is gone: it was reaped before it could be observed as a zombie (its parent was still a shell?)",
            ProcStatRead.Unreadable { Reason: var reason } => $"unreadable: {reason}",
            _ => "unknown",
        };

        return read is ProcStatRead.Read { Line.State: "Z" };
    }

    private static async Task ParentBecomesSleepAsync(int parent)
    {
        var last = "(not read)";
        var became = await Polls.Until(
            () =>
            {
                last = ProcStat.ReadFor(parent, ProcReads.Real) is ProcStatRead.Read { Line.Comm: var comm } ? comm : "(unreadable)";

                return last == "sleep";
            },
            Patience);

        became.Should().BeTrue($"the shell (pid {parent}) never became the outer sleep: its comm read '{last}', and a killed child is only a zombie once its parent is a program that never waits");
    }

    private static void End(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already gone
        }

        process.Dispose();
    }
}
