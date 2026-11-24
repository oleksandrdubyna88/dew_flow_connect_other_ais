using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What publishing the BA's files came to — a closed set, so the work cannot forget a case.</summary>
internal abstract record BaPublished
{
    private BaPublished()
    {
    }

    /// <summary>The whole set is in <c>&lt;epic&gt;/stories/</c>, with <c>ba-manifest.json</c> beside the stories. <see cref="Manifest"/> is the entry of each file: its repository path, the SHA-256 of its LF text and its kind.</summary>
    public sealed record Done(IReadOnlyList<BaManifestEntry> Manifest, string ManifestText) : BaPublished;

    /// <summary>Nothing was moved into place, and the sentence says why: a path that would leave the epic folder, an epic that has a story now, a folder that could not be written.</summary>
    public sealed record Refused(string Reason) : BaPublished;

    /// <summary>This runner is no longer the job's writer (a cancel fenced it): nothing was published.</summary>
    public sealed record Stale : BaPublished;
}

/// <summary>One line of <c>ba-manifest.json</c>: the file's path from the repository's top, the SHA-256 (hex, lower case) of its text with LF line ends, and <c>story</c> or <c>task</c>.</summary>
internal sealed record BaManifestEntry(string Path, string Sha256, string Kind);

/// <summary>
/// Puts the BA's validated files into the repository ATOMICALLY (spec E.2): they are written into <c>&lt;epic&gt;/.coai-ba-staging-&lt;jobId&gt;/stories/</c> (the same file system), <c>ba-manifest.json</c> LAST inside that folder, and the
/// whole <c>stories</c> folder is moved to <c>&lt;epic&gt;/stories</c> with ONE directory rename. A run killed at any point leaves either no <c>stories</c> folder or the complete one; at worst a staging folder, which the next BA job for the
/// epic and the start-up sweep remove (<see cref="BaStaging"/>), and "an epic with a story" ignores it.
/// </summary>
/// <remarks>
/// <para><b>Nothing leaves the epic folder.</b> Every target path — and the staging folder and <c>stories</c> itself — is resolved and checked to be under the epic folder BEFORE the first write (<see cref="RealPath.ResolveBelow"/>: links
/// resolved, the last name not a link); the ids are checked by pattern before that (<see cref="BaOutputReader"/>), so this is the second lock on the same door.</para>
/// <para><b>The generation is compared twice</b> — before the first write and again just before the rename — through <c>stillWriter</c>: a runner that a cancel fenced publishes nothing.</para>
/// <para><b>The manifest is kept twice.</b> <c>keepManifest</c> is given its text just before the rename — the BA job keeps a copy in its own folder, which <c>--role-delta</c> measures against: it must not be the copy in the repository, which a person or
/// the orchestrator may edit.</para>
/// <para><b>The boundary hook is for the tests.</b> It is called after every step that changes the disk, so a test can stop the publisher at each one — as a killed process would — and look at what stands.</para>
/// </remarks>
internal sealed class BaPublisher(BaEpicSite site, string jobId, Func<bool> stillWriter, Action<string>? boundary = null, Action<string>? keepManifest = null)
{
    internal const string ManifestName = "ba-manifest.json";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>The staging folder of this job.</summary>
    public string Staging => Path.Combine(site.EpicFolder, BaEpic.StagingPrefix + jobId);

    public BaPublished Publish(IReadOnlyList<BaFile> files)
    {
        if (Escapes(files) is { Length: > 0 } escape)
        {
            return new BaPublished.Refused(escape);
        }

        if (BaEpic.Stories(site.EpicFolder).Count > 0)
        {
            return new BaPublished.Refused($"epic '{site.EpicId}' has a story now: another run or a person planned it first; nothing was written");
        }

        if (!stillWriter())
        {
            return new BaPublished.Stale();
        }

        var manifest = files.OrderBy(file => file.EpicRelative, StringComparer.Ordinal)
            .Select(file => new BaManifestEntry($"{site.EpicRel}/{file.EpicRelative}", Sha256Of(file.Text), file.Kind)).ToList();
        var text = ManifestText(manifest);
        try
        {
            Stage(files, text);

            return Move(manifest, text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RemoveQuietly(Staging);

            return new BaPublished.Refused($"the plan could not be written into the epic folder: {e.Message}; nothing was published");
        }
    }

    /// <summary>The sentence for the first target path that would leave the epic folder, or empty. Nothing is written yet.</summary>
    private string Escapes(IReadOnlyList<BaFile> files)
    {
        var targets = files.Select(file => file.EpicRelative).Append(BaEpic.StagingPrefix + jobId).Append("stories");
        foreach (var relative in targets)
        {
            var path = RealPath.ResolveBelow(site.EpicFolder, Path.Combine(site.EpicFolder, relative.Replace('/', Path.DirectorySeparatorChar)), PathComparison);
            if (!path.Ok || relative.Split('/').Contains(".."))
            {
                return $"the path '{RealPath.Shown(relative)}' would leave the epic folder: nothing was written";
            }
        }

        return string.Empty;
    }

    private void Stage(IReadOnlyList<BaFile> files, string manifestText)
    {
        RemoveQuietly(Staging);
        Directory.CreateDirectory(Staging);
        Boundary("staging-created");
        var index = 0;
        foreach (var file in files.OrderBy(file => file.EpicRelative, StringComparer.Ordinal))
        {
            var target = Path.Combine(Staging, file.EpicRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, file.Text, Utf8);
            Boundary($"file-{++index}");
        }

        // The manifest LAST: a staging folder that has it holds every file.
        File.WriteAllText(Path.Combine(Staging, "stories", ManifestName), manifestText, Utf8);
        Boundary("manifest-written");
    }

    private BaPublished Move(List<BaManifestEntry> manifest, string manifestText)
    {
        var stories = Path.Combine(site.EpicFolder, "stories");
        if (!stillWriter())
        {
            RemoveQuietly(Staging);

            return new BaPublished.Stale();
        }

        // An EMPTY stories folder is not a story; the move needs the place free.
        if (Directory.Exists(stories) && !Directory.EnumerateFileSystemEntries(stories).Any())
        {
            Directory.Delete(stories);
        }

        keepManifest?.Invoke(manifestText);
        Boundary("before-rename");
        Directory.Move(Path.Combine(Staging, "stories"), stories);
        Boundary("renamed");
        RemoveQuietly(Staging);

        return new BaPublished.Done(manifest, manifestText);
    }

    private void Boundary(string name) => boundary?.Invoke(name);

    private static void RemoveQuietly(string folder)
    {
        try
        {
            // The product's one deleter: a link is unlinked by its own name and never entered, a real folder is emptied down.
            if (Directory.Exists(folder))
            {
                GitScratch.DeleteEvenIfReadOnly(folder);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A staging folder that stays (LinkTargetLostException is an IOException) is removed by the next BA job for the epic or the start-up sweep.
        }
    }

    internal static string Sha256Of(string lfText) => Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(lfText)));

    /// <summary><c>ba-manifest.json</c>: the files of the run, sorted by path, each with the hash of its LF text.</summary>
    internal static string ManifestText(IReadOnlyList<BaManifestEntry> entries)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartArray("files");
            foreach (var entry in entries)
            {
                json.WriteStartObject();
                json.WriteString("path", entry.Path);
                json.WriteString("sha256", entry.Sha256);
                json.WriteString("kind", entry.Kind);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Utf8.GetString(buffer.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }
}

/// <summary>
/// The staging folders a BA job leaves when it is killed between its first write and the rename (spec E.2). They are removed by the next BA job for the epic and by the start-up sweep — never while the job that made one is still
/// running.
/// </summary>
internal static class BaStaging
{
    /// <summary>Removes every staging folder of <paramref name="epicFolder"/> whose job is not live. <paramref name="isLive"/> is asked with the job id the folder's name carries.</summary>
    public static int Sweep(string epicFolder, Func<string, bool> isLive)
    {
        var removed = 0;
        foreach (var folder in StagingFolders(epicFolder))
        {
            if (!isLive(folder.Name[BaEpic.StagingPrefix.Length..]) && Remove(folder))
            {
                removed++;
            }
        }

        return removed;
    }

    /// <summary>Removes the one staging folder a job of the epic left, when there is one (the job is over).</summary>
    public static bool RemoveOf(string epicFolder, string jobId) => Remove(new DirectoryInfo(Path.Combine(epicFolder, BaEpic.StagingPrefix + jobId)));

    private static IEnumerable<DirectoryInfo> StagingFolders(string epicFolder) =>
        Directory.Exists(epicFolder)
            ? new DirectoryInfo(epicFolder).EnumerateDirectories(BaEpic.StagingPrefix + "*").ToList()
            : [];

    private static bool Remove(DirectoryInfo folder)
    {
        try
        {
            if (!folder.Exists)
            {
                return false;
            }

            GitScratch.DeleteEvenIfReadOnly(folder.FullName);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
