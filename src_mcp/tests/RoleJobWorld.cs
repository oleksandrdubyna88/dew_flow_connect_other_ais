using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using CoaiMcp.Core.Notices;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;

namespace CoaiMcp.Tests;

/// <summary>
/// One data directory for a test of the role jobs (epoch 3 E1 s1): the store over it, the notices a keeper wrote, and the real child processes a test needs — a lease held by a PROCESS, a
/// sleeper, a signal sent — all removed with it. The kernel's lock is what is under test, so a test kills and stops real processes and never simulates a holder.
/// </summary>
internal sealed class RoleJobWorld : IDisposable
{
    private readonly TempDir _data = TempDir.For("coai-rolejobs-");
    private readonly List<Process> _children = [];

    internal RoleJobWorld(TimeProvider? clock = null)
    {
        Clock = clock ?? TimeProvider.System;
        Log = new Serilog.LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(new Collecting(Said)).CreateLogger();
        Store = new RoleJobStore(DataDir, Clock, Log);
        Keeper = new RoleJobKeeper(Store, Offer, Log);
    }

    /// <summary>What the store, the keeper and the projection said in the log — a warning here is a projection that could not be written.</summary>
    internal Serilog.ILogger Log { get; }

    internal ConcurrentQueue<Serilog.Events.LogEvent> Said { get; } = new();

    internal string LogText => string.Join(" | ", Said.Select(line => line.RenderMessage() + (line.Exception is { } e ? " :: " + e.GetType().Name + ": " + e.Message : string.Empty)));

    private sealed class Collecting(ConcurrentQueue<Serilog.Events.LogEvent> said) : Serilog.Core.ILogEventSink
    {
        public void Emit(Serilog.Events.LogEvent logEvent) => said.Enqueue(logEvent);
    }

    /// <summary>A store over this directory whose projection writes into the rounds database — the way a server builds one.</summary>
    internal RoleJobStore ProjectedStore() => new(DataDir, Clock, Log, RoleJobProjection.For(DataDir, Log));

    internal TimeProvider Clock { get; }

    internal string DataDir => _data.Path;

    internal RoleJobStore Store { get; }

    internal RoleJobKeeper Keeper { get; }

    /// <summary>Every notice a keeper offered, in order.</summary>
    internal ConcurrentQueue<ServerNotice> Notices { get; } = new();

    private bool Offer(ServerNotice notice)
    {
        Notices.Enqueue(notice);

        return true;
    }

    internal IReadOnlyList<string> NoticeCodes => [.. Notices.Select(notice => notice.Code)];

    /// <summary>A queued job of the <c>probe</c> role, made through the store the way a start makes it.</summary>
    internal RoleJobRecord NewJob(string brief = "succeed", int timeoutMinutes = 30, string role = "probe") =>
        Store.Create(new RoleJobRequest
        {
            JobId = RoleJobIds.New(Clock),
            Role = role,
            RepoPath = DataDir,
            Brief = brief,
            TimeoutMinutes = timeoutMinutes,
        });

    internal static string FakeCli => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    /// <summary>The lease of a job held by another PROCESS for <paramref name="milliseconds"/> — returns once that process says it holds it.</summary>
    internal Process HoldLease(string jobId, int milliseconds = 120_000)
    {
        var lease = Store.FolderOf(jobId)!.LeaseFile;
        File.Delete(lease + ".held");
        var holder = Started(FakeCli, "lease-hold", lease, milliseconds.ToString(CultureInfo.InvariantCulture));
        WaitFor(() => File.Exists(lease + ".held"), "the holder process to take the lease");

        return holder;
    }

    /// <summary>A process that sleeps — a stand-in for the CLI child a runner owns.</summary>
    internal Process Sleeper(int milliseconds = 120_000)
    {
        var sleeper = Started(FakeCli, "sleep", milliseconds.ToString(CultureInfo.InvariantCulture));
        WaitFor(() => ProcessTracking.StartedAt(sleeper.Id) is not null, "the sleeper to start");

        return sleeper;
    }

    private Process Started(string exe, params string[] args)
    {
        // The streams are taken, so a child that outlives its test cannot hold the test host's own stdout open and stall whoever is reading it.
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        var process = Process.Start(info)!;
        _children.Add(process);

        return process;
    }

    internal static RoleChild Identity(Process process) => new(process.Id, ProcessTracking.StartedAt(process.Id)!.Value);

    internal static void Kill9(Process process)
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit(10_000);
    }

    /// <summary>SIGSTOP or SIGCONT by number — the holder stays alive and holds what it holds, and does nothing.</summary>
    internal static void Signal(string signal, Process process, bool group = false)
    {
        // `kill -KILL -- -<pgid>` is a whole process group: the starter of a test is its own session and group leader, so its group is its pid.
        var target = (group ? "-" : string.Empty) + process.Id.ToString(CultureInfo.InvariantCulture);
        using var kill = Process.Start(new ProcessStartInfo("kill", ["-" + signal, "--", target]) { UseShellExecute = false })!;
        kill.WaitForExit(10_000);
    }

    internal static bool IsAlive(RoleChild who) => ProcessTree.IsAlive(who.Identity);

    internal static void WaitFor(Func<bool> condition, string what, int seconds = 20)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"timed out waiting for {what}");
            }

            Thread.Sleep(25);
        }
    }

    public void Dispose()
    {
        foreach (var child in _children)
        {
            try
            {
                if (!child.HasExited)
                {
                    Signal("CONT", child);
                    child.Kill(entireProcessTree: true);
                    child.WaitForExit(10_000);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone already.
            }

            child.Dispose();
        }

        _data.Dispose();
    }
}
