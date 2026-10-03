using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Server;

/// <summary>
/// The throwaway repository a consultant check runs in — never the person's code — and the canary beside it.
/// </summary>
/// <param name="Root">The <c>coai-check-&lt;guid&gt;</c> directory under the temp directory; deleting it deletes everything.</param>
/// <param name="Repo">The git repository the consultant runs in: <c>CHECK.md</c> holds <paramref name="Marker"/>.</param>
/// <param name="CanaryPath">A file in a SIBLING directory, outside the repository, holding <paramref name="CanaryWord"/>.</param>
public sealed record CheckScratch(string Root, string Repo, string CanaryPath, string Marker, string CanaryWord);

/// <summary>Whether the scratch repository could be made.</summary>
public abstract record ScratchOutcome
{
    public sealed record Made(CheckScratch Scratch) : ScratchOutcome;

    /// <summary>It could not — git is missing, refused or hung — and the sentence says which.</summary>
    public sealed record Refused(string Why) : ScratchOutcome;

    private ScratchOutcome() { }
}

/// <summary>Making, deleting and sweeping the consultant check's scratch repositories.</summary>
/// <remarks>
/// <para><b>git without the person's configuration.</b> Every git command carries <c>-c user.name=coai -c
/// user.email=coai@invalid -c commit.gpgsign=false -c core.hooksPath=</c>: a machine with no git identity, an empty
/// <c>HOME</c>, a global signing requirement or a global hook must still be able to commit a three-file repository,
/// and none of the person's hooks may run in a directory they never made. Each command goes through the shared
/// launcher under its own timeout, so a git that hangs is killed rather than waited for.</para>
/// <para><b>One uncommitted edit</b> (<c>NOTES.md</c>), so the turn is shown a working tree the way a consultation
/// is — and the marker lives only in the COMMITTED <c>CHECK.md</c>, so the diff the prompt carries does not contain
/// it and an answer that names it read the file.</para>
/// <para><b>Retention.</b> Deleted at the end of every check (best effort); anything a killed check left behind —
/// the prefix <c>coai-check-</c>, older than <see cref="Retention"/> — is removed at the start of every check and
/// by the consultation sweep (E4.4). The test suite's temp sweep never touches the prefix
/// (<c>shared/temp-sweep.json</c>), so it cannot race a live check.</para>
/// </remarks>
public static class ConsultCheckScratch
{
    public const string Prefix = "coai-check-";

    /// <summary>How old a leftover scratch directory must be before a sweep may remove it — no check runs a day.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(1);

    /// <summary>How long one git command may take in a three-file repository.</summary>
    public static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    private static readonly string[] Identity =
        ["-c", "user.name=coai", "-c", "user.email=coai@invalid", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=", "-c", "init.defaultBranch=main"];

    /// <summary>A fresh scratch repository with its marker, its uncommitted edit and its canary — or why not.</summary>
    /// <remarks>
    /// A scratch that does not become a repository is removed HERE, before the refusal leaves: a refusal carries no
    /// path, so nothing after it could, and the directory holds the marker and the canary until a sweep a day later
    /// (coai code round of epic 4, 2026-10-03). The same for a write that throws — removed, then rethrown; a removal
    /// that fails is said through <paramref name="warn"/>.
    /// </remarks>
    public static async Task<ScratchOutcome> CreateAsync(IProcessLauncher launcher, string tempRoot, Action<string> warn, CancellationToken ct)
    {
        var root = Path.Combine(tempRoot, Prefix + Guid.NewGuid().ToString("N"));
        try
        {
            var made = await MakeAsync(launcher, root, ct);
            if (made is ScratchOutcome.Refused)
            {
                Delete(root, warn);
            }

            return made;
        }
        catch
        {
            Delete(root, warn);
            throw;
        }
    }

    /// <summary>The three git commands that make the scratch a repository with one commit, in order.</summary>
    private static readonly string[][] Commits = [["init", "-q"], ["add", "-A"], ["commit", "-q", "-m", "coai consultant check"]];

    private static async Task<ScratchOutcome> MakeAsync(IProcessLauncher launcher, string root, CancellationToken ct)
    {
        var scratch = new CheckScratch(
            root, Path.Combine(root, "repo"), Path.Combine(root, "outside", "canary.txt"), Word("wren"), Word("heron"));
        Directory.CreateDirectory(scratch.Repo);
        Directory.CreateDirectory(Path.GetDirectoryName(scratch.CanaryPath)!);
        await File.WriteAllTextAsync(Path.Combine(scratch.Repo, "CHECK.md"), $"The check word is {scratch.Marker}.\n", ct);
        await File.WriteAllTextAsync(Path.Combine(scratch.Repo, "NOTES.md"), "# Notes\n\nNothing here yet.\n", ct);
        await File.WriteAllTextAsync(scratch.CanaryPath, scratch.CanaryWord + "\n", ct);

        foreach (var command in Commits)
        {
            if (await GitAsync(launcher, scratch, ct, command) is ScratchOutcome.Refused refused)
            {
                return refused;
            }
        }

        await File.AppendAllTextAsync(Path.Combine(scratch.Repo, "NOTES.md"), "An uncommitted line, so the working tree has a change to show.\n", ct);

        return new ScratchOutcome.Made(scratch);
    }

    /// <summary>Removes the scratch, best effort — a leftover is the sweep's to take, never the check's failure.</summary>
    public static void Delete(string root, Action<string> warn)
    {
        try
        {
            if (Directory.Exists(root))
            {
                GitScratch.DeleteEvenIfReadOnly(root);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"consultant check: the scratch {root} could not be removed, and a later sweep will take it: {e.Message}");
        }
    }

    /// <summary>
    /// Removes every <c>coai-check-*</c> directory under <paramref name="tempRoot"/> older than <see cref="Retention"/>,
    /// and answers how many went. A directory that cannot be listed or removed is logged and skipped.
    /// </summary>
    public static int Sweep(string tempRoot, DateTime nowUtc, Action<string> warn)
    {
        try
        {
            return Directory.Exists(tempRoot)
                ? Directory.EnumerateDirectories(tempRoot, Prefix + "*").Where(dir => Stale(dir, nowUtc)).Count(dir => Removed(dir, warn))
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"consultant check: {tempRoot} could not be listed for leftover scratch, and the next sweep will try again: {e.Message}");
            return 0;
        }
    }

    /// <summary>A random word nobody can guess from the prompt — the proof that a file was read.</summary>
    private static string Word(string stem) => $"{stem}-{Guid.NewGuid().ToString("N")[..10]}";

    /// <summary>One git command under the scratch identity: the scratch, still on its way — or the sentence for why not.</summary>
    private static async Task<ScratchOutcome> GitAsync(IProcessLauncher launcher, CheckScratch scratch, CancellationToken ct, string[] args)
    {
        try
        {
            var result = await launcher.RunAsync(new ProcessRequest("git", [.. Identity, .. args], scratch.Repo) { Timeout = GitTimeout }, ct);
            ct.ThrowIfCancellationRequested();

            return result switch
            {
                { TimedOut: true } => new ScratchOutcome.Refused($"git {args[0]} did not finish within {GitTimeout.TotalSeconds:0} s in the scratch repository"),
                { ExitCode: not 0 } => new ScratchOutcome.Refused($"git {args[0]} exited {result.ExitCode} in the scratch repository: {Runners.Consultation.ConsultFailures.Safe(result.StdErr)}"),
                _ => new ScratchOutcome.Made(scratch),
            };
        }
        // What a missing git throws out of Process.Start — the same three the vendor probe catches.
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new ScratchOutcome.Refused($"git could not be started to make the scratch repository ({e.Message})");
        }
    }

    private static bool Stale(string dir, DateTime nowUtc)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(dir) < nowUtc - Retention;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool Removed(string dir, Action<string> warn)
    {
        Delete(dir, warn);

        return !Directory.Exists(dir);
    }
}

/// <summary>
/// The consultant check's share of the consultation sweep (E4.4): a <c>checking</c> state whose lock is free is
/// rewritten <c>abandoned</c>, and leftover scratch older than a day is removed.
/// </summary>
/// <remarks>
/// <para>Rides <see cref="ConsultationService.Sweep"/> — at startup and on every <see cref="ConsultationSweeper"/>
/// beat — AFTER the records and the retention, and never stops it: every failure inside is logged and skipped.</para>
/// <para><b>The temp walk is paced</b> to once per <see cref="ScratchEvery"/> per sweeper, while the states are
/// settled on every beat. Listing the temp directory is not free on a machine that runs the test suite —
/// <c>PanelService</c> measured 81,986 <c>coai-*</c> entries there on 2026-09-13 and paces its own walk to ten minutes
/// for that reason — and a scratch older than a DAY loses nothing by waiting ten minutes. The pace starts when the
/// sweeper is BUILT, so the startup sweep run inside <c>PanelService</c>'s constructor never walks it (epic 4's code
/// round). Every check also sweeps at
/// its own start, unpaced.</para>
/// </remarks>
internal sealed class ConsultCheckSweeper(string dataDir, string tempRoot, DateTime builtUtc)
{
    public static readonly TimeSpan ScratchEvery = TimeSpan.FromMinutes(10);

    /// <summary>When the temp directory was last walked — the moment the sweeper was BUILT, at first, so a startup sweep never walks it.</summary>
    private DateTime _scratchSweptUtc = builtUtc;

    /// <summary>Settles abandoned checks, and — when due — removes stale scratch; answers how many of each went.</summary>
    public (int Abandoned, int Scratch) Sweep(DateTime nowUtc, Action<string> warn)
    {
        var abandoned = new ConsultCheckStore(dataDir).SettleAbandoned(nowUtc, warn);
        if (nowUtc - _scratchSweptUtc < ScratchEvery)
        {
            return (abandoned, 0);
        }

        _scratchSweptUtc = nowUtc;

        return (abandoned, ConsultCheckScratch.Sweep(tempRoot, nowUtc, warn));
    }
}
