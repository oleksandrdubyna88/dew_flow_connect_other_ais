using System.Diagnostics;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ONE way a test here makes a zombie (C21 and C29 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md</c>, §3.2–§3.3): the fake CLI's <c>unreaped-child</c> verb — an executable and an argv, never a
/// shell with a script string — forks a child, prints its pid and then never waits for it; killing that child leaves it a zombie for as long as the fake CLI lives.
/// </summary>
/// <remarks>
/// <para><b>There is no parent to wait for.</b> The first version ran <c>sh -c</c> and had to wait until the shell had <c>exec</c>ed a program that never waits, because a shell that collects a dead background
/// child (bash does, while it waits for a foreground command) leaves no zombie at all. The verb is never a shell, so the child is a zombie the moment it is dead, and the fixture only has to see the
/// <c>Z</c> state.</para>
/// <para>The <c>Z</c> is polled through <see cref="ProcStat"/>, the one parser of <c>/proc/&lt;pid&gt;/stat</c>; a second reader here would throw <see cref="DirectoryNotFoundException"/> on a reaped entry instead of reading it
/// as <see cref="ProcStatRead.Gone"/>. The wait is bounded by <see cref="Patience"/> and fails with a sentence that names what it was waiting for and the last thing it read.</para>
/// </remarks>
internal sealed class ZombieMaker : IDisposable
{
    internal static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly string FakeCli = Path.Combine(AppContext.BaseDirectory, "FakeCli");

    private readonly Process _parent;

    private ZombieMaker(Process parent, int pid, ProcessIdentity identity)
    {
        _parent = parent;
        Pid = pid;
        Identity = identity;
    }

    /// <summary>The child that will be killed.</summary>
    public int Pid { get; }

    /// <summary>Who it is: its pid with the start time it had when the verb printed the pid.</summary>
    public ProcessIdentity Identity { get; }

    /// <summary>Starts the fake CLI's <c>unreaped-child</c> verb and returns once it has printed the child's pid.</summary>
    public static async Task<ZombieMaker> StartAsync()
    {
        var info = new ProcessStartInfo(FakeCli) { UseShellExecute = false, RedirectStandardOutput = true, ArgumentList = { "unreaped-child" } };
        var parent = Process.Start(info) ?? throw new InvalidOperationException($"{FakeCli} was not started: Process.Start answered no process for the verb that makes the zombie");
        try
        {
            var line = await parent.StandardOutput.ReadLineAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
            var pid = int.Parse(line!.Trim(), System.Globalization.CultureInfo.InvariantCulture);

            return new ZombieMaker(parent, pid, new ProcessIdentity(pid, ProcessTracking.StartedAt(pid)!.Value));
        }
        catch
        {
            End(parent);
            throw;
        }
    }

    /// <summary>Kills the child and waits until <c>/proc</c> shows it as a zombie (state <c>Z</c>).</summary>
    public async Task KillToZombieAsync()
    {
        using (var child = Process.GetProcessById(Pid))
        {
            child.Kill();
        }

        var last = "(not read)";
        var zombie = await Polls.Until(() => IsZombie(ref last), Patience);

        zombie.Should().BeTrue($"the verb never reaps, so the killed child {Pid} should have stayed a zombie; its /proc entry read: {last}");
    }

    public void Dispose() => End(_parent);

    private bool IsZombie(ref string last)
    {
        var read = ProcStat.ReadFor(Pid, ProcReads.Real);
        last = read switch
        {
            ProcStatRead.Read { Line: var line } => $"state {line.State}",
            ProcStatRead.Gone => "gone - the killed child's /proc entry is gone: it was reaped before it could be observed as a zombie",
            ProcStatRead.Unreadable { Reason: var reason } => $"unreadable: {reason}",
            _ => "unknown",
        };

        return read is ProcStatRead.Read { Line.State: "Z" };
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
