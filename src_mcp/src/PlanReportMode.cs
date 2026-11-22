using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CoaiMcp.Server;
using CoaiMcp.Store;
using Microsoft.Data.Sqlite;

namespace CoaiMcp;

/// <summary>
/// <c>coai-mcp --plan-report &lt;feature-dir&gt; --json</c> — what each item of a plan cost the gate and the consultants, as ONE JSON object on stdout, read from the
/// rounds database, then exit 0 (v2 E3.S8; <c>v2/architecture/server.md</c> §21). The items are the folders of the feature found on disk; the numbers are the rounds,
/// consultations and questions whose <c>workItem</c> names one of them.
/// </summary>
/// <remarks>
/// <para><b>A reader, and it never makes the data directory.</b> It opens <c>coai.db</c> read-only (<c>RoundCounts.OpenReadOnly</c>: no migration, no new file), and a
/// folder with no database, or a database an older server last wrote (no <c>work_item</c> column yet), answers every item as zeros. <c>Program.WriterModes</c> does not
/// hold it, and <c>AReaderModeNeverMakesTheDataDirectoryTests</c> runs it against a missing named root.</para>
/// <para><b>The items are read from folder NAMES, never from YAML</b> (a later epoch adds the YAML contract): an <c>epoch-N</c> folder is an epoch, an <c>eN-*</c> folder an
/// epic, an <c>sN-*</c> folder a story, a <c>tN-*</c> folder a task, found by walking down from the feature folder through the items themselves and the <c>stories</c> and
/// <c>tasks</c> containers (a folder that is none of these, such as <c>archive</c>, is not walked into). The feature folder itself is the first item (<c>kind: feature</c>), because a plan gate may be about the whole. An item's <c>path</c> is what a caller sends as
/// <c>workItem</c>: relative to the repository's top level (the nearest folder above the feature folder holding <c>.git</c>), written with <c>/</c>.</para>
/// <para><b>What is counted, per item and for that item alone</b> — an epic's numbers do not include its stories', the page that shows the tree adds them up.
/// <c>rounds</c>: the rounds that ran (a <c>skipped</c> round launched nothing and is no round here); <c>gates</c>: the distinct (session, stage) they belong to — a plan gate
/// of three rounds and a code gate of two are 2 gates, 5 rounds; <c>cons</c> and <c>consask</c>: the consultation and question records; <c>firstUtc</c> and <c>lastUtc</c>: the
/// earliest start and the latest end of any of them; <c>wallSeconds</c>: the length of the UNION of their intervals, so a consultation running beside a round is counted
/// once. <b>An item with no events reports zeros</b> — every key present, the two instants the empty string — never a missing item.</para>
/// <para>An attributed event whose item is under the feature folder but matches no folder now (renamed, deleted, mistyped) is listed under <c>unlisted</c> in the same shape,
/// so a number never disappears for want of a folder.</para>
/// <para><b>Exit codes.</b> 0 with the object; <b>65</b> for the wrong arguments or a folder that is not one inside a git checkout (the sentence on stderr); <b>74</b> for
/// a database the disk will not read. Never 64 — that is "never heard of this mode", how a caller takes an older binary.</para>
/// </remarks>
internal static partial class PlanReportMode
{
    internal const string Usage = "--plan-report takes a feature folder and --json: coai-mcp --plan-report <feature-dir> --json";

    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [GeneratedRegex(@"^epoch-\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex EpochName();

    [GeneratedRegex(@"^e\d+-.+$", RegexOptions.CultureInvariant)]
    private static partial Regex EpicName();

    [GeneratedRegex(@"^s\d+-.+$", RegexOptions.CultureInvariant)]
    private static partial Regex StoryName();

    [GeneratedRegex(@"^t\d+-.+$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskName();

    /// <summary>One folder of the feature.</summary>
    internal sealed record Item(string Path, string Parent, string Kind, string Name);

    /// <summary>One attributed row of the database, reduced to what the report totals.</summary>
    internal sealed record Event(string WorkItem, string Kind, string Group, DateTimeOffset? Start, DateTimeOffset? End);

    internal static int Run(string[] args, Func<string, string?> env, string workingDirectory, TextWriter output, Action<string> note)
    {
        var rest = args.Skip(1).ToList();
        if (rest.Count != 2 || rest.Count(one => one == "--json") != 1 || rest.Any(one => one.StartsWith("--", StringComparison.Ordinal) && one != "--json"))
        {
            note(Usage);

            // 65, NOT 64: this binary HAS the mode and is refusing the request.
            return 65; // EX_DATAERR
        }

        var folder = System.IO.Path.GetFullPath(rest.Single(one => one != "--json"), workingDirectory);
        if (!Directory.Exists(folder))
        {
            note($"--plan-report: {folder} is not a folder");

            return 65;
        }

        if (RepositoryRoot(folder) is not { } root)
        {
            note($"--plan-report: {folder} is not inside a git checkout, so an item has no path from the repository's top level to be named by");

            return 65;
        }

        try
        {
            output.WriteLine(Answer(PanelSettings.FromEnvironment(env).DataDir, root, folder));

            return 0;
        }
        catch (Exception e) when (Program.Unreadable(e))
        {
            note(Program.WhyUnreadable(e));

            return 74; // EX_IOERR
        }
    }

    /// <summary>The nearest folder at or above <paramref name="folder"/> that holds <c>.git</c> (a folder, or the file a worktree has), or null.</summary>
    internal static string? RepositoryRoot(string folder)
    {
        for (var at = new DirectoryInfo(folder); at is not null; at = at.Parent)
        {
            var git = System.IO.Path.Combine(at.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return at.FullName;
            }
        }

        return null;
    }

    /// <summary>The object, on one line.</summary>
    internal static string Answer(string dataDir, string repositoryRoot, string featureFolder)
    {
        var items = Items(repositoryRoot, featureFolder);
        var prefix = Relative(repositoryRoot, featureFolder);
        var events = ReadEvents(dataDir).Where(e => prefix.Length == 0 || e.WorkItem == prefix || e.WorkItem.StartsWith(prefix + "/", StringComparison.Ordinal)).ToList();
        var known = items.Select(item => item.Path).ToHashSet(StringComparer.Ordinal);

        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, WriterOptions))
        {
            json.WriteStartObject();
            json.WriteString("mode", "plan-report");
            json.WriteString("featureDir", prefix);
            json.WriteStartArray("items");
            foreach (var item in items)
            {
                WriteItem(json, item.Path, item.Parent, item.Kind, item.Name, events.Where(e => e.WorkItem == item.Path).ToList());
            }

            json.WriteEndArray();
            json.WriteStartArray("unlisted");
            foreach (var group in events.Where(e => !known.Contains(e.WorkItem)).GroupBy(e => e.WorkItem, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                WriteItem(json, group.Key, string.Empty, "unlisted", group.Key[(group.Key.LastIndexOf('/') + 1)..], [.. group]);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    // ---------- the folders ----------

    /// <summary>The feature folder and every epoch, epic, story and task folder below it, parents before children, in the order a person numbers them.</summary>
    internal static List<Item> Items(string repositoryRoot, string featureFolder)
    {
        var root = Relative(repositoryRoot, featureFolder);
        var items = new List<Item> { new(root, string.Empty, "feature", System.IO.Path.GetFileName(featureFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar, '/'))) };
        Walk(repositoryRoot, featureFolder, root, items);

        return items;
    }

    private static void Walk(string repositoryRoot, string folder, string parent, List<Item> items)
    {
        foreach (var child in Directory.EnumerateDirectories(folder).Select(path => new DirectoryInfo(path)).OrderBy(Numbered).ThenBy(d => d.Name, StringComparer.Ordinal))
        {
            // A symbolic link is never followed: a link inside the feature folder must not lead the walk out of it, or round in a circle.
            if (child.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            if (KindOf(child.Name) is { } kind)
            {
                var path = Relative(repositoryRoot, child.FullName);
                items.Add(new Item(path, parent, kind, child.Name));
                Walk(repositoryRoot, child.FullName, path, items);
            }
            else if (child.Name is "stories" or "tasks")
            {
                Walk(repositoryRoot, child.FullName, parent, items);
            }
        }
    }

    private static string? KindOf(string name) =>
        EpochName().IsMatch(name) ? "epoch"
        : EpicName().IsMatch(name) ? "epic"
        : StoryName().IsMatch(name) ? "story"
        : TaskName().IsMatch(name) ? "task"
        : null;

    /// <summary>The number in a folder's name (<c>epoch-2</c>, <c>e10-x</c>), so <c>e10</c> follows <c>e9</c>; a name with none sorts last.</summary>
    private static int Numbered(DirectoryInfo folder)
    {
        var digits = new string([.. folder.Name.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit)]);

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;
    }

    private static string Relative(string root, string path) =>
        System.IO.Path.GetRelativePath(root, path).Replace('\\', '/') is var relative && relative == "." ? string.Empty : relative;

    // ---------- the events ----------

    private static List<Event> ReadEvents(string dataDir)
    {
        var file = System.IO.Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return [];
        }

        using var db = RoundCounts.OpenReadOnly(file);
        var events = new List<Event>();
        if (RoundsQuery.HasColumn(db, "rounds", "work_item"))
        {
            events.AddRange(Read(
                db, "round",
                "SELECT work_item, session_id || '/' || stage AS grp, started_utc AS started, completed_utc AS ended FROM rounds WHERE work_item <> '' AND verdict <> 'skipped'"));
        }

        if (RoundsQuery.HasColumn(db, "consultations", "work_item"))
        {
            events.AddRange(Read(db, "cons", "SELECT work_item, id AS grp, started_utc AS started, ended_utc AS ended FROM consultations WHERE work_item <> ''"));
        }

        if (RoundsQuery.HasColumn(db, "question_consults", "work_item"))
        {
            events.AddRange(Read(db, "consask", "SELECT work_item, id AS grp, started_utc AS started, ended_utc AS ended FROM question_consults WHERE work_item <> ''"));
        }

        return events;
    }

    /// <summary>The four literal queries above, run — nothing a caller sends reaches the SQL.</summary>
    private static IEnumerable<Event> Read(SqliteConnection db, string kind, string sql)
    {
        using var read = db.CreateCommand();
        read.CommandText = sql;
        using var rows = read.ExecuteReader();
        var found = new List<Event>();
        while (rows.Read())
        {
            found.Add(new Event(Columns.Text(rows, "work_item"), kind, Columns.Text(rows, "grp"), Instant(Columns.Text(rows, "started")), Instant(Columns.Text(rows, "ended"))));
        }

        return found;
    }

    private static DateTimeOffset? Instant(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    // ---------- one item ----------

    private static void WriteItem(Utf8JsonWriter json, string path, string parent, string kind, string name, List<Event> events)
    {
        var rounds = events.Where(e => e.Kind == "round").ToList();
        var instants = events.SelectMany(e => new[] { e.Start, e.End }).OfType<DateTimeOffset>().ToList();

        json.WriteStartObject();
        json.WriteString("path", path);
        json.WriteString("parent", parent);
        json.WriteString("kind", kind);
        json.WriteString("name", name);
        json.WriteNumber("gates", rounds.Select(e => e.Group).Distinct(StringComparer.Ordinal).Count());
        json.WriteNumber("rounds", rounds.Count);
        json.WriteNumber("cons", events.Count(e => e.Kind == "cons"));
        json.WriteNumber("consask", events.Count(e => e.Kind == "consask"));
        json.WriteString("firstUtc", instants.Count == 0 ? string.Empty : Stamp(instants.Min()));
        json.WriteString("lastUtc", instants.Count == 0 ? string.Empty : Stamp(instants.Max()));
        json.WriteNumber("wallSeconds", Math.Round(WallSeconds(events), 3));
        json.WriteEndObject();
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The length of the union of the events' intervals: an event with no end, or an end before its start, is a point and adds nothing.</summary>
    internal static double WallSeconds(IEnumerable<Event> events)
    {
        var intervals = events
            .Where(e => e.Start is not null)
            .Select(e => (Start: e.Start!.Value, End: e.End is { } end && end > e.Start ? end : e.Start.Value))
            .OrderBy(i => i.Start)
            .ToList();
        var total = TimeSpan.Zero;
        DateTimeOffset? from = null;
        DateTimeOffset to = default;
        foreach (var (start, end) in intervals)
        {
            if (from is null)
            {
                (from, to) = (start, end);
            }
            else if (start <= to)
            {
                to = end > to ? end : to;
            }
            else
            {
                total += to - from.Value;
                (from, to) = (start, end);
            }
        }

        return (from is null ? total : total + (to - from.Value)).TotalSeconds;
    }
}
