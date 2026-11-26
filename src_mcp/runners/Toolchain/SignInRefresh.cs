using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>What <see cref="SignInRefresh.RunAsync"/> came to: the refreshed file is in the scratch home (the caller makes the coder's copy from it and deletes the scratch), or a sentence that says why not.</summary>
internal abstract record SignInRefreshResult
{
    private SignInRefreshResult()
    {
    }

    /// <summary>The unit ended and was gone; <paramref name="File"/> is the file as the CLI left it.</summary>
    public sealed record Refreshed(string File) : SignInRefreshResult;

    /// <summary>No refresh: why. The scratch home is already deleted.</summary>
    public sealed record Failed(string Sentence) : SignInRefreshResult;
}

/// <summary>
/// The refresh of a sign-in that coai does before a turn, when the access token of the real file has no margin (epoch 3 E3 s7 t6 d2; judgement G10, A11, A12): a unit of its own that runs the vendor's CLI with NO model — a command that needs the sign-in and nothing else (agy: <c>models</c>) — on a scratch home that
/// holds the FULL copy of the real file, so the CLI refreshes it as it does at its own start.
/// </summary>
/// <remarks>
/// <para><b>What the unit does not have:</b> no model, no coder home, no other job's folder (the folder of the jobs is an empty tmpfs; a worktree or main checkout that lies elsewhere is read-only under <c>ProtectSystem=strict</c>, as in every unit of the family, and is not bound) — its one writable folder is the scratch home, and the isolation, the hardening and the stop timeout are the family's (<see cref="BoundaryUnit.Isolation"/>, <see cref="BoundaryUnit.Hardening"/>, <c>TimeoutStopSec</c>), with the host network.</para>
/// <para><b>Where the full copy lives</b> (risk A12, M10 of the fourth security verify): in <c>&lt;job&gt;/refresh</c>, INSIDE the job folder, so every unit of the job has it under the cover and the retention removes it with the job. It is deleted when the unit ends, before the coder starts, whatever the unit came to; a runner that dies while the unit runs leaves it, and the next start of the job deletes it (<see cref="CoderBoundary"/>), else the retention does. The job folder being its own cover, no refresh is run (the caller refuses).</para>
/// <para>The caller's real file is never opened for writing: the full copy is a byte copy made through a shared read handle.</para>
/// </remarks>
internal sealed class SignInRefresh(IProcessLauncher launcher, BoundaryHost host)
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(120);

    private readonly UnitControl _units = new(launcher, host);

    /// <summary>The properties of the refresh unit, for a record and for a test: the family's isolation and hardening, the scratch home the one writable folder, the program's folder read-only, the host network, the memory and time limits.</summary>
    internal static IReadOnlyList<string> Properties(BoundaryHost host, BoundaryJob job, string scratch, CoderProgram program) =>
    [
        .. BoundaryUnit.Isolation,
        // The folder that holds the jobs' folders is an empty tmpfs: no other job, no worktree, no coder home of this job is there. Only the scratch home is bound back, writable.
        $"TemporaryFileSystem={job.JobCover}:ro",
        $"BindPaths={scratch}",
        .. program.BindFolder.Length > 0 ? [$"BindReadOnlyPaths={program.BindFolder}"] : Array.Empty<string>(),
        .. host.ResolverFile.Length > 0 ? [$"BindReadOnlyPaths={host.ResolverFile}"] : Array.Empty<string>(),
        .. BoundaryUnit.Hardening,
        "PrivateNetwork=no",
        $"RuntimeMaxSec={(int)(Deadline + UnitControl.Backstop).TotalSeconds}",
        $"TimeoutStopSec={UnitControl.UnitStopSeconds}",
        "MemoryMax=1000M",
        "MemorySwapMax=0",
    ];

    /// <summary>
    /// Copies <paramref name="realFile"/> to <paramref name="scratch"/>/<paramref name="relative"/> (mode 0600, folders 0700), runs <paramref name="program"/> with <paramref name="argv"/> in the refresh unit <paramref name="unit"/>, asks that it is gone, deletes the scratch home when the refresh failed, and answers where the refreshed file is when it did not (the caller reads it, makes the coder's copy, and deletes the scratch with <see cref="Delete"/> before any coder starts).
    /// </summary>
    public async Task<SignInRefreshResult> RunAsync(BoundaryJob job, string realFile, string scratch, string relative, CoderProgram program, IReadOnlyList<string> argv, IReadOnlyList<KeyValuePair<string, string>> environment, string unit, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade, CancellationToken ct, string staleField = "")
    {
        var target = Path.Combine(scratch, relative);
        try
        {
            var folder = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(scratch);
            Directory.CreateDirectory(folder);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(scratch, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var from = new FileStream(realFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var to = new FileStream(target, options))
            {
                from.CopyTo(to);
            }

            // agy refreshes only a token that is EXPIRED (measured, 10.5): the access token of THIS scratch copy is set to the past so the refresh unit refreshes. The real file is never touched.
            if (staleField.Length > 0 && MakeStale(target, staleField) is { Length: > 0 } cannot)
            {
                Delete(scratch);

                return new SignInRefreshResult.Failed(cannot);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Delete(scratch);

            return new SignInRefreshResult.Failed($"the full copy of the sign-in for the refresh could not be made ({e.GetType().Name}), so nothing was refreshed");
        }

        var failure = await RunUnitAsync(job, scratch, program, argv, environment, unit, degrade, ct);
        if (failure.Length > 0)
        {
            Delete(scratch);

            return new SignInRefreshResult.Failed(failure);
        }

        return new SignInRefreshResult.Refreshed(target);
    }

    /// <summary>Sets the time at <paramref name="field"/> (a <c>/</c>-separated path) of the JSON file at <paramref name="file"/> two hours into the past; empty when it was, else the sentence (no value of the file in it).</summary>
    private static string MakeStale(string file, string field)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file)) is not System.Text.Json.Nodes.JsonObject root)
            {
                return "the sign-in file is not a JSON object, so its copy for the refresh could not be made stale";
            }

            var parts = field.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var at = root;
            foreach (var part in parts[..^1])
            {
                if (at[part] is not System.Text.Json.Nodes.JsonObject next)
                {
                    return "the sign-in file has no expiry field, so its copy for the refresh could not be made stale";
                }

                at = next;
            }

            if (at[parts[^1]] is null)
            {
                return "the sign-in file has no expiry field, so its copy for the refresh could not be made stale";
            }

            at[parts[^1]] = DateTime.UtcNow.AddHours(-2).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            using var stream = new FileStream(file, FileMode.Truncate, FileAccess.Write, FileShare.None);
            stream.Write(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));

            return string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return $"the copy of the sign-in for the refresh could not be made stale ({e.GetType().Name})";
        }
    }

    /// <summary>The unit's own run: empty when it ended 0 and was gone, else the sentence.</summary>
    private async Task<string> RunUnitAsync(BoundaryJob job, string scratch, CoderProgram program, IReadOnlyList<string> argv, IReadOnlyList<KeyValuePair<string, string>> environment, string unit, Func<IReadOnlyList<string>, IReadOnlyList<string>>? degrade, CancellationToken ct)
    {
        IReadOnlyList<string> inner = ["/usr/bin/env", "-i", .. environment.Select(pair => $"{pair.Key}={pair.Value}"), program.Real, .. argv];
        var arguments = BoundaryUnit.Wrap(unit, scratch, [], Properties(host, job, scratch, program), inner, degrade);
        var started = await _units.StartAsync(new ProcessRequest(host.SystemdRun, arguments, scratch) { Timeout = Deadline, InheritsEnvironment = false, Passthrough = UnitControl.Client }, ct);
        if (started.Problem.Length > 0)
        {
            return started.Problem;
        }

        if (!await _units.EnsureGoneAsync(unit))
        {
            return $"the refresh unit {unit} could not be stopped, so nothing was refreshed and no launch is started";
        }

        return started.Result.ExitCode != 0 || started.Result.TimedOut
            ? $"the refresh of the sign-in ({string.Join(' ', argv)}) {(started.Result.TimedOut ? "ran out of time" : $"exited {started.Result.ExitCode}")}, so no launch is started"
            : string.Empty;
    }

    /// <summary>Deletes the scratch home with the full copy in it; never follows a link out of it.</summary>
    internal static void Delete(string scratch)
    {
        try
        {
            if (RealPath.Lstat(scratch).Kind == EntryKind.Directory)
            {
                GitScratch.DeleteEvenIfReadOnly(scratch);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // What stays is inside the job folder: the next start of the job deletes it, else the retention removes it with the job.
        }
    }
}
