using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server.RoleJobs;

namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp --role-delta &lt;jobId&gt; --at &lt;commit&gt;</c> — how much of what a BA job wrote the orchestrator changed (epoch 3 E1 s4 t3, spec C): the share of the BA's task files that were edited, deleted or joined by another
/// task file by the commit the plan gate passed at, answered as ONE JSON object. The measure of the epic's "the orchestrator changes &lt; 20 % of the task files".
/// </summary>
/// <remarks>
/// <para><b>The measure.</b> <c>changedShare = (changed + deleted + added task files) / task files of the manifest</c>. The manifest is the BA job's own <c>ba-manifest.json</c> (in its job folder: what the BA wrote, with the SHA-256
/// of each file's LF text). A manifest task file is <em>changed</em> when its text at the commit — line ends read as LF, so a checkout that turns them into CRLF changes nothing — hashes differently, <em>deleted</em> when the commit
/// has no such file; a task file the commit has below the epic's <c>stories/</c> that the manifest does not name is <em>added</em>. A rename is a delete and an add. Story files are not counted: the measure is of tasks. It passes
/// <b>below 0.20</b> — decided on whole numbers (<c>5 × changes &lt; tasks</c>), never on a float — so one change in ten passes and two in ten do not.</para>
/// <para><b>A reader.</b> It writes nothing and never makes the data directory; it reads the job folder and asks <c>git</c> about the repository the job recorded (<c>git rev-parse</c>, <c>ls-tree</c>, <c>show</c>).</para>
/// <para><b>Exit codes — never 64</b> (this binary KNOWS the mode): <b>0</b> measured, whatever the share (<c>pass</c> in the answer says which side of 0.20 it is on); <b>65</b> the arguments are wrong, there is no such job, it is not a
/// BA job that succeeded, it has no manifest or no task in it, the repository is gone or the commit is not in it; <b>74</b> something that should read does not (a manifest that is torn, a <c>git</c> that cannot be started or fails).</para>
/// </remarks>
internal static partial class RoleDeltaMode
{
    internal const string Usage = "--role-delta takes a job id and a commit: coai-mcp --role-delta <jobId> --at <commit>";

    /// <summary>The share below which the epic's measure passes: 20 %, written as the whole numbers it is judged on.</summary>
    internal const int PassBelowOneIn = 5;

    [GeneratedRegex(@"^t\d+-[a-z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskFolder();

    /// <summary>What the mode decided: the exit code, the answer when there is one, and the sentence when it refused.</summary>
    internal readonly record struct Outcome(int Code, string Json, string Refusal);

    internal static async Task<int> RunAsync(string[] args, Func<string, string?> env, TextWriter output, Action<string> note, CancellationToken ct = default)
    {
        if (args.Length != 4 || args[2] != "--at" || args[1].StartsWith("--", StringComparison.Ordinal) || !RoleJobIds.IsValid(args[1]) || !CommitName(args[3]))
        {
            note(Usage);

            return 65; // EX_DATAERR — this binary HAS the mode and is refusing the request
        }

        var outcome = await EvaluateAsync(args[1], args[3], Server.PanelSettings.DataDirectoryFor(env).Path, new ProcessLauncher(), ct);
        if (outcome.Refusal.Length > 0)
        {
            note($"--role-delta: {outcome.Refusal}");

            return outcome.Code;
        }

        output.WriteLine(outcome.Json);

        return outcome.Code;
    }

    /// <summary>A commit as an argument: something git reads as a revision, not as an option — no leading dash, no space or control character, a sane length.</summary>
    private static bool CommitName(string text) => text.Length is > 0 and <= 200 && text[0] != '-' && text.All(c => c > ' ' && c != '\u007f');

    internal static async Task<Outcome> EvaluateAsync(string jobId, string commit, string dataDir, IProcessLauncher git, CancellationToken ct = default)
    {
        var store = new RoleJobStore(dataDir, TimeProvider.System, Serilog.Core.Logger.None);
        if (!Directory.Exists(store.JobsRoot) || store.FolderOf(jobId) is not { Exists: true } folder || folder.ReadJob() is not { } job || folder.ReadRequest() is not { } request)
        {
            return Refused(65, $"there is no role job '{jobId}' in {dataDir}");
        }

        if (job.Role != RoleDescriptors.Ba || job.State != RoleJobStates.Succeeded)
        {
            return Refused(65, $"job '{jobId}' is a {job.Role} job in state {job.State}: the measure is of a BA job that succeeded, because only then did it write files");
        }

        var (manifest, problem) = ReadManifest(Path.Combine(folder.Location, BaPublisher.ManifestName));
        if (manifest is null)
        {
            return problem.Code == 74 ? problem : Refused(65, problem.Refusal);
        }

        return await MeasureAsync(job, request, manifest, commit, git, ct);
    }

    private static async Task<Outcome> MeasureAsync(RoleJobRecord job, RoleJobRequest request, IReadOnlyList<BaManifestEntry> manifest, string commit, IProcessLauncher git, CancellationToken ct)
    {
        var tasks = manifest.Where(entry => entry.Kind == "task").ToList();
        if (tasks.Count == 0)
        {
            return Refused(65, $"the manifest of job '{job.JobId}' names no task file, so there is nothing to measure a share of");
        }

        if (!Directory.Exists(job.Repo))
        {
            return Refused(65, $"the repository of job '{job.JobId}' ({job.Repo}) is not a folder any more");
        }

        var asked = new Git(git, job.Repo, ct);
        if (await asked.RunAsync("rev-parse", "--verify", "--quiet", commit + "^{commit}") is not { } resolved)
        {
            return Refused(74, "git could not be started, so the commit could not be read");
        }

        if (resolved.ExitCode is not (0 or 1))
        {
            return resolved.StdErr.Contains("not a git repository", StringComparison.Ordinal)
                ? Refused(65, $"the repository of job '{job.JobId}' ({job.Repo}) is not a git repository any more")
                : Refused(74, $"git failed in the repository, so the commit could not be read: {resolved.StdErr.Trim()}");
        }

        if (resolved.ExitCode != 0)
        {
            return Refused(65, $"'{commit}' is not a commit of the repository of job '{job.JobId}'");
        }

        var epic = EpicFolder(request.TaskFile);
        var present = await asked.TreeAsync(resolved.StdOut.Trim(), epic + "/stories");
        if (present is null)
        {
            return Refused(74, "git could not list the epic's stories at the commit");
        }

        var delta = await DeltaAsync(asked, resolved.StdOut.Trim(), tasks, present);

        return delta is null ? Refused(74, "git could not read a task file at the commit") : new Outcome(0, Json(job, request, commit, tasks.Count, delta), string.Empty);
    }

    private sealed record Delta(IReadOnlyList<string> Changed, IReadOnlyList<string> Deleted, IReadOnlyList<string> Added)
    {
        public int Count => Changed.Count + Deleted.Count + Added.Count;
    }

    private static async Task<Delta?> DeltaAsync(Git git, string commit, IReadOnlyList<BaManifestEntry> tasks, IReadOnlySet<string> present)
    {
        var changed = new List<string>();
        var deleted = new List<string>();
        foreach (var task in tasks)
        {
            if (!present.Contains(task.Path))
            {
                deleted.Add(task.Path);

                continue;
            }

            if (await git.ShowAsync(commit, task.Path) is not { } text)
            {
                return null;
            }

            if (BaPublisher.Sha256Of(text.Replace("\r\n", "\n", StringComparison.Ordinal)) != task.Sha256)
            {
                changed.Add(task.Path);
            }
        }

        var known = tasks.Select(task => task.Path).ToHashSet(StringComparer.Ordinal);
        var added = present.Where(path => IsTaskFile(path) && !known.Contains(path)).Order(StringComparer.Ordinal).ToList();

        return new Delta(changed, deleted, added);
    }

    /// <summary><c>…/tasks/tN-name/task.yaml</c>: the shape of a task file below a story.</summary>
    private static bool IsTaskFile(string path)
    {
        var parts = path.Split('/');

        return parts.Length >= 3 && parts[^1] == "task.yaml" && TaskFolder().IsMatch(parts[^2]) && parts[^3] == "tasks";
    }

    private static string EpicFolder(string taskFile) => taskFile.Contains('/', StringComparison.Ordinal) ? taskFile[..taskFile.LastIndexOf('/')] : string.Empty;

    private static (IReadOnlyList<BaManifestEntry>? Manifest, Outcome Problem) ReadManifest(string file)
    {
        if (!File.Exists(file))
        {
            return (null, Refused(65, "the job has no ba-manifest.json: it wrote nothing, or it is a job of an older build"));
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var entries = new List<BaManifestEntry>();
            foreach (var entry in document.RootElement.GetProperty("files").EnumerateArray())
            {
                entries.Add(new BaManifestEntry(entry.GetProperty("path").GetString()!, entry.GetProperty("sha256").GetString()!, entry.GetProperty("kind").GetString()!));
            }

            return (entries, default);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return (null, Refused(74, $"the manifest of the job could not be read: {e.Message}"));
        }
    }

    private static Outcome Refused(int code, string sentence) => new(code, string.Empty, sentence);

    private static string Json(RoleJobRecord job, RoleJobRequest request, string commit, int tasks, Delta delta)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("mode", "role-delta");
            json.WriteString("jobId", job.JobId);
            json.WriteString("epic", EpicFolder(request.TaskFile));
            json.WriteString("commit", commit);
            json.WriteNumber("manifestTasks", tasks);
            json.WriteNumber("changed", delta.Changed.Count);
            json.WriteNumber("deleted", delta.Deleted.Count);
            json.WriteNumber("added", delta.Added.Count);
            json.WriteNumber("changedShare", Math.Round((double)delta.Count / tasks, 4));
            json.WriteString("threshold", "below 0.20");
            json.WriteBoolean("pass", delta.Count * PassBelowOneIn < tasks);
            WriteList(json, "changedFiles", delta.Changed);
            WriteList(json, "deletedFiles", delta.Deleted);
            WriteList(json, "addedFiles", delta.Added);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteList(Utf8JsonWriter json, string name, IReadOnlyList<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    /// <summary>The few questions this mode asks <c>git</c>, each as one launch in the job's repository. A launch that cannot even start is null.</summary>
    private sealed class Git(IProcessLauncher launcher, string repo, CancellationToken ct)
    {
        public async Task<ProcessResult?> RunAsync(params string[] args)
        {
            try
            {
                return await launcher.RunAsync(new ProcessRequest("git", ["-C", repo, .. args], repo) { Timeout = TimeSpan.FromSeconds(60) }, ct);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>The paths of every file below <paramref name="folder"/> at the commit, with <c>/</c>; null when git failed.</summary>
        public async Task<IReadOnlySet<string>?> TreeAsync(string commit, string folder)
        {
            var result = await RunAsync("ls-tree", "-r", "--name-only", "-z", commit, "--", folder);

            return result is { ExitCode: 0 } ? result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal) : null;
        }

        /// <summary>The text of a file at the commit; null when git failed.</summary>
        public async Task<string?> ShowAsync(string commit, string path)
        {
            var result = await RunAsync("show", $"{commit}:{path}");

            return result is { ExitCode: 0 } ? result.StdOut : null;
        }
    }
}
