using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Runners.Processes;

/// <summary>Recording a launched child so a later run can find it. No-op when nothing is tracked.</summary>
public interface IProcessTracker
{
    void Record(int pid, DateTime startedUtc, string label);

    void Forget(int pid);
}

/// <summary>A tracker that records nothing — the default, and what every test gets.</summary>
public sealed class NoProcessTracking : IProcessTracker
{
    public static readonly NoProcessTracking Instance = new();

    public void Record(int pid, DateTime startedUtc, string label) { }

    public void Forget(int pid) { }
}

[JsonSerializable(typeof(TrackedProcess))]
internal sealed partial class TrackedProcessContext : JsonSerializerContext;

/// <summary>
/// The record of every reviewer this server has running, and the sweep that collects the ones a
/// dead server left behind.
/// </summary>
/// <remarks>
/// <para>One small file per child under <c>&lt;dataDir&gt;/running/</c>, written when it starts and
/// deleted when it exits. A file rather than memory for the only reason that matters: the case this
/// exists for is the server not being there any more.</para>
/// <para>The same shape as the worktree lifecycle — written on the way in, swept on the next
/// <c>open</c> — because it is the same problem, and a leaked reviewer costs more than a leaked
/// directory: it holds a vendor's rate limit, a GPU, or a paid token budget.</para>
/// <para><b>Every failure here is swallowed.</b> Recording is bookkeeping; a review must not fail
/// because a directory was not writable, and a sweep must not fail because one process refused to
/// die. Both log and carry on.</para>
/// </remarks>
public sealed class ProcessTracking(string dataDir, Action<string>? note = null) : IProcessTracker
{
    private readonly string _dir = Path.Combine(dataDir, "running");

    public void Record(int pid, DateTime startedUtc, string label)
    {
        try
        {
            Directory.CreateDirectory(_dir);
            var record = new TrackedProcess(
                pid,
                startedUtc,
                Environment.ProcessId,
                Process.GetCurrentProcess().StartTime.ToUniversalTime(),
                label);
            File.WriteAllText(
                Path.Combine(_dir, $"{pid}.json"),
                JsonSerializer.Serialize(record, TrackedProcessContext.Default.TrackedProcess));
        }
        catch (Exception e)
        {
            note?.Invoke($"could not record process {pid}: {e.Message}");
        }
    }

    public void Forget(int pid)
    {
        try
        {
            File.Delete(Path.Combine(_dir, $"{pid}.json"));
        }
        catch (Exception e)
        {
            note?.Invoke($"could not forget process {pid}: {e.Message}");
        }
    }

    /// <summary>
    /// Kills the reviewers a dead server left running, and forgets the records that no longer
    /// name anything.
    /// </summary>
    /// <returns>How many processes were actually killed, for the log line.</returns>
    public int Sweep()
    {
        var plan = OrphanSweep.Plan(Read(), StartedAt, Environment.ProcessId);
        var killed = 0;
        foreach (var orphan in plan.Reap)
        {
            try
            {
                // The whole tree, for the same reason the timeout kill takes the tree: a reviewer
                // CLI spawns its own children, and the one holding the vendor's connection is
                // usually not the process this product started.
                using var process = Process.GetProcessById(orphan.Pid);
                process.Kill(entireProcessTree: true);
                killed += 1;
                note?.Invoke($"killed orphaned reviewer {orphan.Label} (pid {orphan.Pid}), left by a server that is gone");
            }
            catch (Exception e)
            {
                // It exited between the plan and the kill, or it is not ours to signal. Neither is
                // worth failing an `open` over.
                note?.Invoke($"could not kill orphaned reviewer {orphan.Pid}: {e.Message}");
            }

            Forget(orphan.Pid);
        }

        foreach (var stale in plan.Forget)
        {
            Forget(stale.Pid);
        }

        return killed;
    }

    internal IReadOnlyList<TrackedProcess> Read()
    {
        if (!Directory.Exists(_dir))
        {
            return [];
        }

        var records = new List<TrackedProcess>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize(File.ReadAllText(file), TrackedProcessContext.Default.TrackedProcess) is { } record)
                {
                    records.Add(record);
                }
            }
            catch (Exception)
            {
                // A half-written record from a server that died mid-write. Deleting it is the
                // whole remedy: it names a process nothing can now identify.
                try
                {
                    File.Delete(file);
                }
                catch (Exception e)
                {
                    note?.Invoke($"could not delete the unreadable record {file}: {e.Message}");
                }
            }
        }

        return records;
    }

    /// <summary>When the live process with that pid started, or null when there is none.</summary>
    private static DateTime? StartedAt(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.StartTime.ToUniversalTime();
        }
        catch (Exception)
        {
            // No such process, or one this account may not look at. Both mean "not ours to kill",
            // which is the safe answer either way.
            return null;
        }
    }
}
