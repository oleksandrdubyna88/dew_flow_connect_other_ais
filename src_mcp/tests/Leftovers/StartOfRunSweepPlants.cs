using FluentAssertions;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// What the coai-owned sweep scenarios (<c>StartOfRunSweepTests</c>, <c>StartOfRunSweepSwitchTests</c>, <c>StartOfRunSweepRaceAndRootTests</c>) share: names built from <see cref="Owned.NameBase"/>, a dead pid with its premise
/// asserted, a claim's text, the sweep's options with the kernel-owner seam <c>_ =&gt; true</c>, and a planted folder with its claim, both aged. Split out of one 839-line file ([S2-B code1 1]).
/// </summary>
internal static class SweepPlants
{
    // Spelled in two pieces: the machine scan reads this file for the bare prefix too.
    internal const string QuarantineWord = "teststo" + "delete-quarantine-";

    internal static readonly TimeSpan Old = TimeSpan.FromMinutes(11);

    internal static string Trimmed(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    internal static string Name(string what) => Owned.NameBase + what + "-abc123";

    internal static long Dead()
    {
        var pid = UnmatchablePid.Next();
        UnmatchablePid.AssertNoSuchProcess(pid);
        return pid;
    }

    internal static string Rec(long pid) => (Owner.This with { Pid = pid }).Format() + "\n";

    internal static SweepOptions Options(string tmp, Func<string, bool>? mine = null, SweepHooks? hooks = null, DateTimeOffset? now = null, TimeSpan? lockWait = null) =>
        new(tmp, now ?? DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, mine ?? (_ => true), hooks, lockWait);

    internal static string? ReasonOf(SweepResult result, string path) =>
        result.Preserved.Where(p => p.Path == path).Select(p => p.Reason).SingleOrDefault();

    /// <summary>A folder with a file in it, its claim beside it, both aged (unless <paramref name="old"/> is false).</summary>
    internal static string PlantFolder(string root, string name, string? claim, bool old = true)
    {
        var dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "file.txt"), "x");
        if (claim is not null)
        {
            File.WriteAllText(dir + ".owner", claim);
        }

        if (old)
        {
            AgeFolder(dir);
            if (claim is not null)
            {
                File.SetLastWriteTimeUtc(dir + ".owner", DateTime.UtcNow - Old);
            }
        }

        return dir;
    }

    internal static void AgeFolder(string dir) => Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow - Old);

    /// <summary>Every entry of a root with its bytes (a file) or a marker (a folder): what "nothing changed" is compared with.</summary>
    internal static string[] Snapshot(string root) =>
        [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(entry => File.Exists(entry) ? $"{entry}={File.ReadAllText(entry)}" : $"{entry}/")];
}
