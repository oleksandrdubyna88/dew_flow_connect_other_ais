using System.Text.Json;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The files of ONE job folder, <c>&lt;dataDir&gt;/roles/jobs/&lt;jobId&gt;/</c>, and the only door that reads or writes them (spec A): <c>request.json</c>, <c>job.json</c>,
/// <c>job.lock</c>, <c>lease</c>, <c>heartbeat</c>, <c>cancel</c>, <c>env.json</c>, <c>attempts/&lt;n&gt;/</c>, <c>result.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Whole or not at all.</b> <c>job.json</c>, <c>heartbeat</c>, <c>request.json</c> and <c>result.json</c> are written through <see cref="AtomicFile"/> (a temp file and a move), so a
/// reader sees the whole of a version or none of it, and a runner killed in the middle of a write leaves the previous version. A read-modify-write of <c>job.json</c> runs under the short
/// <c>job.lock</c> (<see cref="UnderLock"/>) — a lock file held open exclusively, which the kernel releases when the holder dies, so there is nothing stale to break.</para>
/// <para><b>The lock is never skipped.</b> A turn that cannot be taken in <see cref="LockWait"/> is a refusal the caller sees, not a write without the turn: <c>SessionTurn</c> writes
/// without it when it times out, and that is the race a turn exists to close. Here the loser of a race is told, and the file is not touched.</para>
/// </remarks>
internal sealed class RoleJobFolder(string path)
{
    /// <summary>How long a writer waits for <c>job.lock</c>. A hold lasts milliseconds; this is the ceiling before a refusal.</summary>
    internal static readonly TimeSpan LockWait = TimeSpan.FromSeconds(3);

    public string Location => path;

    public string RequestFile => System.IO.Path.Combine(path, "request.json");

    public string JobFile => System.IO.Path.Combine(path, "job.json");

    public string LockFile => System.IO.Path.Combine(path, "job.lock");

    public string LeaseFile => System.IO.Path.Combine(path, "lease");

    public string HeartbeatFile => System.IO.Path.Combine(path, "heartbeat");

    public string CancelFile => System.IO.Path.Combine(path, "cancel");

    public string EnvFile => System.IO.Path.Combine(path, "env.json");

    public string ResultFile => System.IO.Path.Combine(path, "result.json");

    /// <summary>The marker that says the one <c>role-job-stalled</c> notice of this stall was written (<see cref="RoleJobStore.TryMarkStalled"/>).</summary>
    public string StalledMarker => System.IO.Path.Combine(path, "stalled.noticed");

    public string AttemptsFolder => System.IO.Path.Combine(path, "attempts");

    public bool Exists => Directory.Exists(path);

    /// <summary>Runs <paramref name="work"/> holding <c>job.lock</c>, or answers false without running it when the lock could not be taken in <see cref="LockWait"/>.</summary>
    public bool UnderLock(Action work, TimeProvider clock)
    {
        var until = clock.GetTimestamp() + (long)(LockWait.TotalSeconds * clock.TimestampFrequency);
        while (true)
        {
            if (HeldFile.TryHold(LockFile) is { } held)
            {
                using (held)
                {
                    work();
                }

                return true;
            }

            if (clock.GetTimestamp() >= until)
            {
                return false;
            }

            Thread.Sleep(Random.Shared.Next(2, 8));
        }
    }

    /// <summary>The job record, or null when the file is not there or cannot be read (a torn or foreign file is "no record", never a throw).</summary>
    public RoleJobRecord? ReadJob() => ReadJson(JobFile, RoleJobsJson.Default.RoleJobRecord)?.Normalised();

    public RoleJobRequest? ReadRequest() => ReadJson(RequestFile, RoleJobsJson.Default.RoleJobRequest)?.Normalised();

    public RoleHeartbeat? ReadHeartbeat() => ReadJson(HeartbeatFile, RoleJobsJson.Default.RoleHeartbeat)?.Normalised();

    public void WriteJob(RoleJobRecord record) =>
        AtomicFile.Write(JobFile, JsonSerializer.Serialize(record, RoleJobsJson.Default.RoleJobRecord), AtomicFile.SharingRetry);

    public void WriteRequest(RoleJobRequest request) =>
        AtomicFile.Write(RequestFile, JsonSerializer.Serialize(request, RoleJobsJson.Default.RoleJobRequest), AtomicFile.SharingRetry);

    public void WriteHeartbeat(RoleHeartbeat beat) =>
        AtomicFile.Write(HeartbeatFile, JsonSerializer.Serialize(beat, RoleJobsJson.Default.RoleHeartbeat), AtomicFile.SharingRetry);

    public void WriteResult(string json) => AtomicFile.Write(ResultFile, json, AtomicFile.SharingRetry);

    /// <summary>The result text, or empty when no result was written.</summary>
    public string ReadResult() => ReadText(ResultFile);

    /// <summary>The environment file, owner-only (<c>0600</c> on a Unix host): written before the runner starts, read once by it and deleted.</summary>
    public void WriteEnvironment(RoleJobEnvironment environment)
    {
        var text = JsonSerializer.Serialize(environment, RoleJobsJson.Default.RoleJobEnvironment);
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(EnvFile, options);
        stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
    }

    /// <summary>The environment the launcher left for the runner — and the file removed, whether or not it could be read.</summary>
    public RoleJobEnvironment? TakeEnvironment()
    {
        try
        {
            return ReadJson(EnvFile, RoleJobsJson.Default.RoleJobEnvironment);
        }
        finally
        {
            DeleteQuietly(EnvFile);
        }
    }

    public bool CancelRequested => File.Exists(CancelFile);

    public void RequestCancel() => File.WriteAllText(CancelFile, string.Empty);

    internal static void DeleteQuietly(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A file that could not be removed is left for the retention; no caller's answer depends on it.
        }
    }

    private static T? ReadJson<T>(string file, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
        where T : class
    {
        var text = ReadText(file);
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(text, info);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The text of a file, or empty when it is not there. A file another process is moving into place right now (Windows refuses the read for a moment) is read again, briefly.</summary>
    private static string ReadText(string file)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(file);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return string.Empty;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 4)
                {
                    return string.Empty;
                }

                Thread.Sleep(10);
            }
        }
    }
}
