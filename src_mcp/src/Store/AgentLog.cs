using System.Text;
using System.Text.Json;

namespace CoaiMcp.Store;

/// <summary>
/// What the calling agent was DOING in the stretch before a round — from its own session log.
/// </summary>
/// <remarks>
/// <para>Asked for on 2026-09-05, and the reasoning behind it is the point: <i>"сессия началась в
/// 13:00 и ревью плана было в 13:39 — берём всё за этот промежуток. Потом с 13:39 до 15:03 было
/// написание кода — берём всё за этот промежуток и прикрепляем к код-ревью."</i> The gate records
/// what other models found. It records nothing about what the agent did before they found it, which
/// is exactly the half a blind-spot analysis needs: not only "this was missed", but "here is what
/// was being done while it was missed".</para>
/// <para>The source is the CLI's own transcript — one JSONL file per session under
/// <c>~/.claude/projects/&lt;folder&gt;/&lt;session&gt;.jsonl</c>, a line per event carrying
/// <c>timestamp</c>, <c>type</c>, <c>cwd</c> and the message. Nothing is written there by us and
/// nothing here writes to it.</para>
/// <para><b>Trimmed, hard.</b> A busy ninety minutes is megabytes, and a database row is not where
/// megabytes of transcript belong. Each entry keeps its instant, its kind and the first few hundred
/// characters of what it said; the slice keeps at most a few hundred entries and a quarter of a
/// megabyte, and says how many it dropped rather than pretending it holds everything.</para>
/// <para><b>Local only.</b> This is a transcript of somebody's own session on their own machine,
/// beside their own sessions. It is never sent anywhere by this server.</para>
/// </remarks>
public static class AgentLog
{
    /// <summary>At most this many entries in one slice.</summary>
    private const int MaxEntries = 400;

    /// <summary>And at most this much text, whichever comes first.</summary>
    private const int MaxBytes = 256 * 1024;

    /// <summary>How much of one message is kept.</summary>
    private const int MaxTextPerEntry = 600;

    /// <summary>Where the CLI keeps its transcripts, for this machine's user.</summary>
    public static string DefaultProjectsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    /// <summary>
    /// Everything the agent did between two instants, as a compact JSON array.
    /// </summary>
    /// <remarks>
    /// <para>When any entry in the window is working IN the repository under review — that folder or
    /// anything under it — only those entries are kept: several agents are at work at once on this
    /// machine, and the one standing in this repository is the one that called this gate.</para>
    /// <para>When none is, the fallback is ONE session and not all of them: the busiest transcript in
    /// the window, which is the one that was doing the work. The gate's own security reviewers
    /// raised the first version of this, and they were right — sweeping every project's transcript
    /// into this repository's database copies other people's work, and whatever it contains, into a
    /// file this one owns. The exact answer is for the caller to name its own transcript, which is a
    /// protocol change and is written down as one.</para>
    /// </remarks>
    public static string Slice(string projectsDir, DateTime fromUtc, DateTime toUtc, string repoPath)
    {
        if (!Directory.Exists(projectsDir) || toUtc <= fromUtc)
        {
            return string.Empty;
        }

        var byFile = Read(projectsDir, fromUtc, toUtc);
        var mine = byFile.SelectMany(f => f.Value).Where(e => InRepo(e.Cwd, repoPath)).ToList();

        return Render(mine.Count > 0 ? Ordered(mine) : Busiest(byFile));
    }

    /// <summary>The one transcript that was doing the most in this window, or nothing.</summary>
    private static IReadOnlyList<Entry> Busiest(Dictionary<string, List<Entry>> byFile) =>
        byFile.Count == 0 ? [] : Ordered(byFile.OrderByDescending(f => f.Value.Count).First().Value);

    private static IReadOnlyList<Entry> Ordered(IEnumerable<Entry> entries) => [.. entries.OrderBy(e => e.Utc)];

    /// <summary>One line of the transcript, reduced to what is worth keeping.</summary>
    private sealed record Entry(DateTime Utc, string Kind, string Cwd, string Text);

    private static Dictionary<string, List<Entry>> Read(string projectsDir, DateTime fromUtc, DateTime toUtc)
    {
        var byFile = new Dictionary<string, List<Entry>>();
        var days = Days(fromUtc, toUtc);
        foreach (var file in Files(projectsDir, fromUtc))
        {
            var entries = ReadFile(file, fromUtc, toUtc, days).ToList();
            if (entries.Count > 0)
            {
                byFile[file] = entries;
            }
        }

        return byFile;
    }

    /// <summary>
    /// The days the window touches, as the text an entry's timestamp starts with.
    /// </summary>
    /// <remarks>
    /// A cheap gate in front of an expensive one: a transcript is tens of megabytes and the window
    /// is usually one afternoon of it, so a line that does not even mention a day in range is
    /// skipped by a substring scan rather than parsed as JSON. Empty for a window spanning more days
    /// than are worth listing, and then every line is parsed as before.
    /// </remarks>
    internal static IReadOnlyList<string> Days(DateTime fromUtc, DateTime toUtc)
    {
        var span = toUtc.Date - fromUtc.Date;
        if (span.TotalDays > 7)
        {
            return [];
        }

        return [.. Enumerable.Range(0, (int)span.TotalDays + 1).Select(n => fromUtc.Date.AddDays(n).ToString("yyyy-MM-dd"))];
    }

    /// <summary>
    /// The transcripts that could hold anything in the window.
    /// </summary>
    /// <remarks>
    /// A file last written before the window opened cannot contain an entry inside it, and skipping
    /// those is what keeps this from parsing every megabyte the CLI has ever written.
    /// </remarks>
    private static IEnumerable<string> Files(string projectsDir, DateTime fromUtc)
    {
        foreach (var file in Directory.EnumerateFiles(projectsDir, "*.jsonl", SearchOption.AllDirectories))
        {
            if (File.GetLastWriteTimeUtc(file) >= fromUtc)
            {
                yield return file;
            }
        }
    }

    private static IEnumerable<Entry> ReadFile(string file, DateTime fromUtc, DateTime toUtc, IReadOnlyList<string> days)
    {
        foreach (var line in ReadLines(file))
        {
            if (!Mentions(line, days))
            {
                continue;
            }

            var entry = Parse(line);
            if (entry is null || entry.Utc < fromUtc)
            {
                continue;
            }

            if (entry.Utc > toUtc)
            {
                // A transcript is written in order, so the first entry past the window ends the file.
                yield break;
            }

            yield return entry;
        }
    }

    private static bool Mentions(string line, IReadOnlyList<string> days)
    {
        if (days.Count == 0)
        {
            return true;
        }

        foreach (var day in days)
        {
            if (line.Contains(day, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The file's lines, or none at all.
    /// </summary>
    /// <remarks>
    /// The CLI is appending to its own transcript while this reads it, so the file is opened shared
    /// and a failure to read is silence rather than an exception — this is a nicety attached to a
    /// round, and it does not get to fail one.
    /// </remarks>
    private static IEnumerable<string> ReadLines(string file)
    {
        StreamReader reader;
        try
        {
            reader = new StreamReader(new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        using (reader)
        {
            while (reader.ReadLine() is { } line)
            {
                yield return line;
            }
        }
    }

    private static Entry? Parse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("timestamp", out var stamp)
                || !DateTime.TryParse(stamp.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var utc))
            {
                return null;
            }

            return new Entry(
                utc,
                Text(root, "type"),
                Text(root, "cwd"),
                Said(root));
        }
        catch (JsonException)
        {
            return null; // a half-written last line, which is the normal state of a live transcript
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// What the entry actually said, whatever shape it said it in.
    /// </summary>
    /// <remarks>
    /// A message's content is a string on the simple path and an array of blocks on every other —
    /// text, a tool call, a tool's result. A tool call keeps its NAME rather than its arguments: the
    /// arguments are most of the bytes and the least of the meaning.
    /// </remarks>
    private static string Said(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return Cut(content.GetString() ?? string.Empty);
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var said = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            Append(said, block);
        }

        return Cut(said.ToString());
    }

    private static void Append(StringBuilder said, JsonElement block)
    {
        var kind = Text(block, "type");
        var piece = kind switch
        {
            "text" => Text(block, "text"),
            "thinking" => Text(block, "thinking"),
            "tool_use" => "[" + Text(block, "name") + "]",
            _ => string.Empty,
        };
        if (piece.Length > 0)
        {
            said.Append(said.Length > 0 ? " " : string.Empty).Append(piece);
        }
    }

    private static string Cut(string text)
    {
        var one = text.Replace("\r", " ").Replace("\n", " ").Trim();

        return one.Length <= MaxTextPerEntry ? one : one[..MaxTextPerEntry] + "…";
    }

    /// <summary>
    /// Whether this entry was working in the repository under review — that folder, or under it.
    /// </summary>
    /// <remarks>
    /// Under it as well as at it: an agent standing in <c>repo/src_vs_code</c> is working in the
    /// repository, and the first version answered no and fell back to every project on the machine.
    /// The separator is part of the test, so <c>/repo-two</c> is not inside <c>/repo</c>.
    /// </remarks>
    private static bool InRepo(string cwd, string repoPath)
    {
        if (cwd.Length == 0 || repoPath.Length == 0)
        {
            return false;
        }

        var (where, repo) = (Normalise(cwd), Normalise(repoPath));

        return where.Equals(repo, StringComparison.OrdinalIgnoreCase)
            || where.StartsWith(repo + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string path) => path.Replace('\\', '/').TrimEnd('/');

    /// <summary>The slice as JSON, capped, saying what it had to leave out.</summary>
    private static string Render(IReadOnlyList<Entry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            var written = Write(json, entries);
            json.WriteEndArray();
            json.Flush();
            if (written < entries.Count)
            {
                return Truncated(buffer, entries.Count - written);
            }
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static int Write(Utf8JsonWriter json, IReadOnlyList<Entry> entries)
    {
        json.WriteStartArray();
        var written = 0;
        foreach (var entry in entries)
        {
            if (written >= MaxEntries || json.BytesPending + json.BytesCommitted > MaxBytes)
            {
                break;
            }

            json.WriteStartObject();
            json.WriteString("utc", entry.Utc.ToString("O"));
            json.WriteString("kind", entry.Kind);
            json.WriteString("said", entry.Text);
            json.WriteEndObject();
            written++;
        }

        return written;
    }

    /// <summary>
    /// A capped slice says so, in the slice.
    /// </summary>
    /// <remarks>
    /// A truncated record that looks complete is worse than no record: somebody counts the entries
    /// later and reads the cap as the measurement.
    /// </remarks>
    private static string Truncated(MemoryStream buffer, int dropped)
    {
        var text = Encoding.UTF8.GetString(buffer.ToArray());

        return text[..^1] + ",{\"utc\":\"\",\"kind\":\"truncated\",\"said\":\"" + dropped + " further entries were not kept\"}]";
    }
}
