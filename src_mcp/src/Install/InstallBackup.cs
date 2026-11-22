using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiMcp.Install;

/// <summary>
/// The backup of one run: <c>&lt;state&gt;/install-backups/&lt;utcStamp&gt;-&lt;pid&gt;/</c> — a full copy of each file the run will change, and the manifest
/// (v2 E3.S6). Made before the first change; a run that changes nothing makes none.
/// </summary>
/// <remarks>
/// <para>The folder is 0700 and every file in it 0600. It is never the data folder: a backup of <c>.claude.json</c> holds other servers' environment, which belongs
/// to a person's state, not to the store the extension reads.</para>
/// <para>The last <see cref="Keep"/> are kept; an older one is deleted unless a line in it is still <see cref="ManifestStates.Removed"/> — a half-done update a later run is yet to finish.</para>
/// </remarks>
internal sealed partial class InstallBackup
{
    /// <summary>How many backups stay.</summary>
    internal const int Keep = 10;

    private readonly InstallHost _host;
    private ManifestRecord _manifest;

    private InstallBackup(InstallHost host, string dir, ManifestRecord manifest)
    {
        _host = host;
        Dir = dir;
        _manifest = manifest;
    }

    /// <summary>The run's id: its folder's name.</summary>
    internal string Id => _manifest.Id;

    internal string Dir { get; }

    /// <summary>One target to back up, before the run changes it.</summary>
    internal sealed record Subject(Client Cli, string Kind, string File, McpEntry? Before, McpEntry? After);

    /// <summary>Makes the folder, copies each file that exists, and writes the manifest with every line <see cref="ManifestStates.Planned"/>. Throws an <see cref="IOException"/> when it cannot — and then nothing has been changed.</summary>
    internal static InstallBackup Create(InstallHost host, DateTimeOffset now, int pid, string binary, IReadOnlyList<Subject> subjects)
    {
        InstallFiles.PrivateDirectory(host.BackupsDir);
        var id = $"{now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture)}-{pid}";
        var dir = Path.Combine(host.BackupsDir, id);
        for (var suffix = 2; Directory.Exists(dir); suffix++)
        {
            dir = Path.Combine(host.BackupsDir, $"{id}-{suffix}");
        }

        InstallFiles.PrivateDirectory(dir);
        var targets = new List<TargetRecord>();
        foreach (var subject in subjects)
        {
            var hash = InstallFiles.Sha256Of(subject.File);
            if (hash.Length > 0)
            {
                InstallFiles.CopyPrivate(subject.File, Path.Combine(dir, CopyName(subject)));
            }

            targets.Add(new TargetRecord(subject.Cli.Word(), subject.Kind, subject.File, hash, EntryRecord.Of(subject.Before), EntryRecord.Of(subject.After), ManifestStates.Planned));
        }

        var manifest = new ManifestRecord(Path.GetFileName(dir), binary, now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), [.. targets]);
        ManifestFile.Write(dir, manifest);

        return new InstallBackup(host, dir, manifest);
    }

    private static string CopyName(Subject subject) => $"{subject.Cli.Word()}-{subject.Kind}-{Path.GetFileName(subject.File)}";

    /// <summary>Records how far one target got, on disk, before the caller goes on.</summary>
    internal void SetState(Client cli, string kind, string state)
    {
        _manifest = _manifest with
        {
            Targets = [.. _manifest.Targets.Select(target => target.Cli == cli.Word() && target.Kind == kind ? target with { State = state } : target)],
        };
        ManifestFile.Write(Dir, _manifest);
    }

    /// <summary>The backups on disk, newest first (their names sort by time), each with its manifest when it can be read.</summary>
    internal static IReadOnlyList<(string Dir, ManifestRecord? Manifest)> All(InstallHost host) =>
        !Directory.Exists(host.BackupsDir)
            ? []
            : [.. Directory.GetDirectories(host.BackupsDir).OrderByDescending(dir => Path.GetFileName(dir), StringComparer.Ordinal).Select(dir => (dir, ManifestFile.Read(dir)))];

    /// <summary>A backup by id (or <c>latest</c>); none for an id no backup has. An id is a folder NAME — one with a separator in it names nothing.</summary>
    internal static ManifestRecord? Find(InstallHost host, string idOrLatest)
    {
        var all = All(host).Where(one => one.Manifest is not null).ToList();
        if (idOrLatest == "latest")
        {
            return all.Count == 0 ? null : all[0].Manifest;
        }

        return SafeId().IsMatch(idOrLatest) ? all.FirstOrDefault(one => Path.GetFileName(one.Dir) == idOrLatest).Manifest : null;
    }

    /// <summary>The entry a killed run left with its old one removed and its new one not yet written, for <paramref name="cli"/> and <paramref name="kind"/> — newest such line.</summary>
    internal static McpEntry? InterruptedEntry(InstallHost host, Client cli, string kind) =>
        All(host).SelectMany(one => one.Manifest?.Targets ?? [])
            .Where(target => target.Cli == cli.Word() && target.Kind == kind && target.State == ManifestStates.Removed && target.Before is not null)
            .Select(target => target.Before!.ToEntry())
            .FirstOrDefault();

    /// <summary>After a target was brought to its wanted state, the older lines that were left half done for it are closed.</summary>
    internal static void SupersedeInterrupted(InstallHost host, Client cli, string kind, string exceptId)
    {
        foreach (var (dir, manifest) in All(host))
        {
            if (manifest is null || manifest.Id == exceptId || !manifest.Targets.Any(target => target.Cli == cli.Word() && target.Kind == kind && target.State == ManifestStates.Removed))
            {
                continue;
            }

            ManifestFile.Write(dir, manifest with
            {
                Targets = [.. manifest.Targets.Select(target => target.Cli == cli.Word() && target.Kind == kind && target.State == ManifestStates.Removed ? target with { State = ManifestStates.Superseded } : target)],
            });
        }
    }

    /// <summary>Deletes every backup past the last <see cref="Keep"/>, except one that still holds a half-done update.</summary>
    internal static void Prune(InstallHost host)
    {
        foreach (var (dir, manifest) in All(host).Skip(Keep))
        {
            if (manifest?.Targets.Any(target => target.State == ManifestStates.Removed) == true)
            {
                continue;
            }

            try
            {
                CoaiMcp.Runners.Files.GitScratch.DeleteEvenIfReadOnly(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // An old backup that cannot be removed now is removed by a later run.
            }
        }
    }

    [GeneratedRegex("^[0-9A-Za-z-]+$")]
    private static partial Regex SafeId();
}
