using System.Text.Json;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Install;

/// <summary>
/// How a file another program owns is changed when the installer edits it itself rather than through the program's own command (Claude's <c>settings.json</c>, Codex's <c>AGENTS.md</c>): read it, build the new
/// bytes by splicing, and write them whole or not at all (v2 E3.S7).
/// </summary>
/// <remarks>
/// <para>A result equal to the file is not written; the new bytes go to a temporary file BESIDE the real one (mode kept) and are renamed over it; a file reached through a symbolic link is written at the link's
/// target, the link left standing. Another program may write the file too, so the bytes are compared again just before the rename: a file that changed since they were read is read again, up to
/// <see cref="Attempts"/> times, and then the target is <c>refused</c> with nothing written. The window between that comparison and the rename is not closed (a rename cannot be made conditional); a write inside
/// it is lost, and the next run sees the change missing and makes it again.</para>
/// </remarks>
internal static class FileEdit
{
    /// <summary>How many times the file is read and written before another writer's changes make the target refused.</summary>
    internal const int Attempts = 3;

    /// <summary>Carries out <paramref name="plan"/> on its file. <paramref name="transform"/> makes the new bytes from the file's bytes (or none) and may throw <see cref="JsonException"/> or <see cref="FormatException"/> for text that is not the shape the target needs.</summary>
    internal static Task<TargetOutcome> ApplyAsync(IInstallTarget target, PlannedTarget plan, ApplyContext context, Func<byte[]?, PlannedTarget, byte[]> transform, CancellationToken ct) =>
        Task.FromResult(Apply(target, plan, context, transform, ct));

    private static TargetOutcome Apply(IInstallTarget target, PlannedTarget plan, ApplyContext context, Func<byte[]?, PlannedTarget, byte[]> transform, CancellationToken ct)
    {
        // Where the file really is: the end of a chain of links (every directory inside a link's target resolved too), through the product's one resolver. A path that cannot be resolved is refused, never spelled.
        var real = RealPath.Resolve(plan.File);
        if (!real.Ok)
        {
            return Refuse(plan, context, $"{plan.File} cannot be changed: {real.Why}");
        }

        var file = real.Path;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var original = File.Exists(file) ? File.ReadAllBytes(file) : null;
            context.Faults.AfterSettingsRead?.Invoke(plan.File, attempt);
            byte[] next;
            try
            {
                next = transform(original, plan);
            }
            catch (Exception e) when (e is JsonException or FormatException)
            {
                return Refuse(plan, context, $"{plan.File} cannot be changed: {e.Message}");
            }

            if (original is not null && next.AsSpan().SequenceEqual(original))
            {
                return Done(plan, context);
            }

            if (WriteUnlessChanged(file, original, next) == WriteResult.Changed)
            {
                continue;
            }

            Step(plan, context, ManifestStates.Written);
            if (!Matches(target.Read(File.ReadAllText(file)), plan.After))
            {
                Put(file, original);

                return new TargetOutcome(OutcomeKind.Failed, $"{plan.File} does not hold the wanted change after it was written; the file was put back");
            }

            return Done(plan, context);
        }

        return Refuse(plan, context, $"{plan.File} changed between being read and being written, {Attempts} times in a row — another program is writing it; nothing was written, run this again");
    }

    private enum WriteResult
    {
        Yes,
        Changed,
    }

    private static TargetOutcome Done(PlannedTarget plan, ApplyContext context)
    {
        Step(plan, context, ManifestStates.Verified);

        return TargetOutcome.Done;
    }

    private static TargetOutcome Refuse(PlannedTarget plan, ApplyContext context, string reason)
    {
        context.Backup.SetState(plan.Client, plan.Kind, ManifestStates.RolledBack);

        return new TargetOutcome(OutcomeKind.Refused, reason);
    }

    private static void Step(PlannedTarget plan, ApplyContext context, string state)
    {
        context.Backup.SetState(plan.Client, plan.Kind, state);
        context.Faults.AfterState(plan.Client, plan.Kind, state);
    }

    private static bool Matches(ConfigRead read, McpEntry? wanted) =>
        read is ConfigRead.Found found && (wanted is null ? found.Entry is null : wanted.Same(found.Entry));

    /// <summary>Writes <paramref name="next"/> over <paramref name="file"/> through a temporary file beside it — unless the file is no longer what <paramref name="original"/> was.</summary>
    private static WriteResult WriteUnlessChanged(string file, byte[]? original, byte[] next)
    {
        var folder = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, $".{Path.GetFileName(file)}.coai-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
        try
        {
            var mode = original is null || OperatingSystem.IsWindows() ? UnixFileMode.UserRead | UnixFileMode.UserWrite : File.GetUnixFileMode(file);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = mode;
            }

            using (var stream = new FileStream(temp, options))
            {
                stream.Write(next);
                stream.Flush(flushToDisk: true);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, mode);
            }

            var now = File.Exists(file) ? File.ReadAllBytes(file) : null;
            if (original is null ? now is not null : now is null || !now.AsSpan().SequenceEqual(original))
            {
                return WriteResult.Changed;
            }

            File.Move(temp, file, overwrite: true);

            return WriteResult.Yes;
        }
        finally
        {
            InstallFiles.TryDelete(temp);
        }
    }

    /// <summary>Puts <paramref name="original"/> back (a failed read-back), by the same temporary file and rename.</summary>
    private static void Put(string file, byte[]? original)
    {
        if (original is null)
        {
            InstallFiles.TryDelete(file);

            return;
        }

        var temp = $"{file}.coai-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temp, original);
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            InstallFiles.TryDelete(temp);
        }
    }

}
