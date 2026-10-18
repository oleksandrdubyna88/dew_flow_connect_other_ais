using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CoaiMcp.Store;

namespace CoaiMcp.Tests;

/// <summary>Which transcript file of a materialised case a helper edits: <see cref="Main"/>, or an agent by the id in its file name.</summary>
public readonly record struct TranscriptFile(string Key)
{
    public static TranscriptFile Main => new("main");

    public bool IsMain => Key == "main";

    public static TranscriptFile Agent(string key) => new(key);
}

/// <summary>
/// One of E1's fixture cases, copied into a temp folder and made real: a Claude home the reader can be pointed at, and a tree of
/// fixture roots with <c>.git</c> markers where the manifest names checkouts (E3.S4).
/// </summary>
/// <remarks>
/// <para><b>The mutation helpers are the ONLY way a test alters a fixture</b> — every public instance method that returns
/// <c>void</c> is one, so E3.S5 can find them by reflection and demand that each has a sample call. A helper edits the parsed JSON of
/// one line and writes it back with the terminator the file already uses; none puts a string a test typed into a text field. The two
/// truncating helpers, <see cref="CorruptMeta"/> and <see cref="EmptyAgentFile"/> cut bytes. After every write the file's last-write time
/// is set to its own largest stamp, so <c>WrittenSince</c> is real (a copied file's time is the copy time, which says nothing about
/// a synthetic 2000-01-01 clock).</para>
/// <para><b>Paths.</b> A fixture path is rewritten on the parsed value: the part after <c>D:/fixture</c> keeps its separators and its drive
/// case as written on Windows (13 cases mix <c>\</c> and <c>/</c>; Q8's forms are the point) and has <c>\</c> folded to <c>/</c> elsewhere. The
/// parent folder of every written path is created, because a missing folder is <c>Unresolved</c> and never walked up from.</para>
/// </remarks>
public sealed class CallerTranscriptFixture
{
    private const string FixturePrefix = "D:/fixture";

    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly UTF8Encoding Utf8 = new(false);
    private static readonly Regex Renamed = new(@"^k\d+\z", RegexOptions.CultureInvariant);

    private CallerTranscriptFixture(FixtureCase fixtureCase, FixtureRounds rounds, ClaudeHome home, string root)
    {
        Case = fixtureCase;
        Rounds = rounds;
        Home = home;
        Root = root;
        MainFile = FindMain(home);
    }

    public FixtureCase Case { get; }

    public FixtureRounds Rounds { get; }

    public ClaudeHome Home { get; }

    /// <summary>The folder the fixture roots (<c>repo-a</c>, <c>other</c>, <c>scratch</c>) were made in.</summary>
    public string Root { get; }

    /// <summary>The caller's transcript, or empty for a case that has none (c10).</summary>
    public string MainFile { get; }

    /// <summary><c>&lt;project folder&gt;/&lt;session id&gt;</c>: where the subagents live.</summary>
    public string SessionFolder => MainFile.Length == 0 ? string.Empty : MainFile[..^".jsonl".Length];

    public IReadOnlyList<string> AgentKeys => [.. AgentFiles().Select(KeyOfAgentFile)];

    public CallerTranscriptLocation Location => CallerTranscriptLocator.LocateStored(Rounds.CallerSessionId, Home);

    /// <summary>The one value a test uses for "a bound past every line": inside the guard the gate yields every line of an 88-second fixture (G8).</summary>
    public DateTime BeyondTheGuard => LargestStamp() + CallerTranscript.StraddleGuard + TimeSpan.FromSeconds(1);

    public static CallerTranscriptFixture Materialise(string name, TempDir temp)
    {
        var fixtureCase = FixtureManifest.Find(name);
        var source = Path.Combine(FixtureManifest.Folder, fixtureCase.Dir);
        var home = new ClaudeHome(temp.At("home", "projects"), temp.At("home", "sessions"));
        var root = temp.At("fixture");
        CopyTree(Path.Combine(source, "projects"), home.ProjectsDir);
        CopyTree(Path.Combine(source, "sessions"), home.SessionsDir);
        MakeRoots(fixtureCase, root);
        var fixture = new CallerTranscriptFixture(fixtureCase, FixtureManifest.RoundsOf(fixtureCase), home, root);
        fixture.RewriteEveryPath();

        return fixture;
    }

    public ObservedSession Read(DateTime? fromUtc = null) => Read(ReadBudget.Unbounded, fromUtc);

    public ObservedSession Read(ReadBudget budget, DateTime? fromUtc = null) => CallerTranscript.Read(Location, budget, fromUtc);

    // ---- what a test reads from the files ------------------------------------------------------------------------------

    public IReadOnlyList<string> Lines(TranscriptFile file) => ReadLines(PathOf(file));

    public DateTime StampOf(TranscriptFile file, int line) => StampIn(ReadLines(PathOf(file))[line - 1]);

    /// <summary>The length in bytes of each line WITH its own terminator, from the file's raw bytes (never a flat +1).</summary>
    public IReadOnlyList<int> ByteLengths(TranscriptFile file)
    {
        var bytes = File.ReadAllBytes(PathOf(file));
        var lengths = new List<int>();
        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                lengths.Add(i + 1 - start);
                start = i + 1;
            }
        }

        return start < bytes.Length ? [.. lengths, bytes.Length - start] : lengths;
    }

    public DateTime LargestStamp() => AllFiles().Select(LargestStampOf).DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>The full tool-use id (a call's or a result's) that ends with <paramref name="suffix"/>, which must name exactly one id in the file.</summary>
    public string ToolUseIdEndingWith(TranscriptFile file, string suffix)
    {
        var ids = ReadLines(PathOf(file)).SelectMany(IdsIn).Where(id => id.EndsWith(suffix, StringComparison.Ordinal)).Distinct().ToList();

        return ids.Count == 1 ? ids[0] : throw new InvalidOperationException($"{ids.Count} tool-use ids end with '{suffix}' in {file.Key}.");
    }

    /// <summary>The ids of the file's <c>tool_use</c> blocks named <paramref name="tools"/>, in file order, each once.</summary>
    public IReadOnlyList<string> CallIds(TranscriptFile file, params string[] tools) =>
        [.. ReadLines(PathOf(file)).SelectMany(line => CallsIn(line, tools)).Distinct()];

    // ---- the mutation helpers (public, void) -----------------------------------------------------------------------------

    /// <summary>Cuts the last line in half and drops its newline: a line the CLI was still writing.</summary>
    public void TruncateLastLine(TranscriptFile file)
    {
        var path = PathOf(file);
        var lines = ReadLines(path);
        lines[^1] = lines[^1][..(lines[^1].Length / 2)];
        WriteLines(path, lines, finalNewline: false);
    }

    /// <summary>Cuts line <paramref name="n"/> in half, keeping its newline: corruption in the MIDDLE of a file.</summary>
    public void TruncateLine(TranscriptFile file, int n)
    {
        var path = PathOf(file);
        var lines = ReadLines(path);
        lines[n - 1] = lines[n - 1][..(lines[n - 1].Length / 2)];
        WriteLines(path, lines);
    }

    /// <summary>Cuts line <paramref name="n"/> just before its top-level <c>"timestamp":</c> key, so it has no stamp and does not parse (G5).</summary>
    public void TruncateBeforeStamp(TranscriptFile file, int n)
    {
        const string Key = "\"timestamp\":";
        var path = PathOf(file);
        var lines = ReadLines(path);
        var at = lines[n - 1].LastIndexOf(Key, StringComparison.Ordinal);
        var cut = at > 0 && lines[n - 1][at - 1] == ',' ? at - 1 : at;
        lines[n - 1] = at < 0 ? throw new InvalidOperationException("The line has no timestamp key.") : lines[n - 1][..cut];
        WriteLines(path, lines);
    }

    public void DeleteMeta(string id) => File.Delete(MetaPathOf(id));

    public void DeleteAgentFile(string id) => File.Delete(PathOf(TranscriptFile.Agent(id)));

    /// <summary>Overwrites the meta with half of its bytes (G1).</summary>
    public void CorruptMeta(string id)
    {
        var bytes = File.ReadAllBytes(MetaPathOf(id));
        File.WriteAllBytes(MetaPathOf(id), bytes[..(bytes.Length / 2)]);
    }

    /// <summary>Truncates <c>agent-&lt;id&gt;.jsonl</c> to 0 bytes, the meta untouched (S4-23).</summary>
    public void EmptyAgentFile(string id) => File.WriteAllBytes(PathOf(TranscriptFile.Agent(id)), []);

    public void RemoveMetaKey(string id, string key) => EditMeta(id, meta => meta.Remove(key));

    public void SetMetaKey(string id, string key, string value) => EditMeta(id, meta => meta[key] = value);

    /// <summary>Points every line's <c>cwd</c> at another fixture root, in every file. The reader never reads it.</summary>
    public void RewriteEveryCwd(string fixtureRoot)
    {
        var target = Map(fixtureRoot);
        foreach (var file in AllFiles())
        {
            EditEveryLine(file, line => line.ContainsKey("cwd") && Assign(line, "cwd", target));
        }
    }

    public void SetModelOnLine(TranscriptFile file, int n, string value) => EditLine(file, n, line => line["message"]!["model"] = value);

    public void SetRequestedModel(TranscriptFile file, int n, string value) => EditLine(file, n, line => line["requestedModel"] = value);

    /// <summary>A null <paramref name="value"/> removes the key.</summary>
    public void SetEffort(TranscriptFile file, int n, string? value) =>
        EditLine(file, n, line => _ = value is null ? line.Remove("effort") : Assign(line, "effort", value));

    public void SetAgentId(TranscriptFile file, int n, string id) => EditLine(file, n, line => line["agentId"] = id);

    public void SetSidechain(TranscriptFile file, int n, bool sidechain, string? agentId = null) => EditLine(file, n, line =>
    {
        line["isSidechain"] = sidechain;
        _ = agentId is null || Assign(line, "agentId", agentId);
    });

    public void SetStamp(TranscriptFile file, int n, DateTime instant) => EditLine(file, n, line => line["timestamp"] = StampText(instant));

    /// <summary>Removes a top-level key of line <paramref name="n"/> (<c>timestamp</c>), or one level into <c>message</c> when the key reads <c>message.id</c>.</summary>
    public void RemoveLineKey(TranscriptFile file, int n, string key) => EditLine(file, n, line => RemoveKey(line, key));

    private static void RemoveKey(JsonObject line, string key)
    {
        var path = key.Split('.', 2);
        var holder = path.Length == 1 ? line : line[path[0]] as JsonObject;
        _ = holder?.Remove(path[^1]);
    }

    public void PrependLine(TranscriptFile file, string json)
    {
        var path = PathOf(file);
        var lines = ReadLines(path);
        lines.Insert(0, json);
        WriteLines(path, lines);
    }

    /// <summary>Adds a 3-byte character to a renamed <c>k&lt;n&gt;</c> string value of line <paramref name="n"/>, never to a structural one.</summary>
    public void PutMultibyte(TranscriptFile file, int n) => EditLine(file, n, line => Pad(line, "\u20AC"));

    /// <summary>Pads a renamed <c>k&lt;n&gt;</c> string value so that line <paramref name="n"/> is at least <paramref name="bytes"/> bytes long (G2).</summary>
    public void PutLongLine(TranscriptFile file, int n, int bytes) => EditLine(file, n, line =>
    {
        var missing = bytes - Utf8.GetByteCount(line.ToJsonString(Json));
        Pad(line, new string('x', Math.Max(missing, 1)));
    });

    public void SetResultError(TranscriptFile file, string toolUseId) =>
        EditResult(file, toolUseId, (block, _) => block["is_error"] = true);

    public void DeleteResult(TranscriptFile file, string toolUseId) =>
        EditResult(file, toolUseId, (block, content) => content.Remove(block));

    /// <summary>Appends a <c>user</c> line with a <c>tool_result</c> for the call, stamped one second after the file's largest stamp.</summary>
    public void AnswerCall(TranscriptFile file, string toolUseId)
    {
        var path = PathOf(file);
        var id = ToolUseIdEndingWith(file, toolUseId);
        var answer = new JsonObject
        {
            ["type"] = "user",
            ["isSidechain"] = !file.IsMain,
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = id }),
            },
            ["timestamp"] = StampText(LargestStampOf(path) + TimeSpan.FromSeconds(1)),
        };
        _ = file.IsMain || Assign(answer, "agentId", file.Key);
        WriteLines(path, [.. ReadLines(path), answer.ToJsonString(Json)]);
    }

    /// <summary>Appends main's line <paramref name="n"/> to an agent file: the same message id in two files (S4-21).</summary>
    public void CopyLineInto(int n, string agentId)
    {
        var path = PathOf(TranscriptFile.Agent(agentId));
        WriteLines(path, [.. ReadLines(path), ReadLines(MainFile)[n - 1]]);
    }

    /// <summary>Gives each line of the message id (ends with <paramref name="messageId"/>) a larger <c>output_tokens</c> than the one before it.</summary>
    public void GrowOutputAcross(string messageId)
    {
        var seen = 0;
        foreach (var file in AllFiles())
        {
            EditEveryLine(file, line => MessageIdOf(line).EndsWith(messageId, StringComparison.Ordinal) && Assign(line["message"]!["usage"]!.AsObject(), "output_tokens", 10 * ++seen));
        }

        if (seen < 2)
        {
            throw new InvalidOperationException($"{seen} line(s) carry a message id ending with '{messageId}'.");
        }
    }

    // ---- files, lines, JSON ----------------------------------------------------------------------------------------------

    private static string FindMain(ClaudeHome home) =>
        Directory.Exists(home.ProjectsDir)
            ? Directory.EnumerateDirectories(home.ProjectsDir).SelectMany(dir => Directory.EnumerateFiles(dir, "*.jsonl")).FirstOrDefault() ?? string.Empty
            : string.Empty;

    private string PathOf(TranscriptFile file) =>
        file.IsMain ? MainFile : Path.Combine(SessionFolder, "subagents", "agent-" + file.Key + ".jsonl");

    private string MetaPathOf(string id) => Path.Combine(SessionFolder, "subagents", "agent-" + id + ".meta.json");

    private List<string> AgentFiles() =>
        Directory.Exists(Path.Combine(SessionFolder, "subagents"))
            ? [.. Directory.EnumerateFiles(Path.Combine(SessionFolder, "subagents"), "agent-*.jsonl").Order(StringComparer.Ordinal)]
            : [];

    private static string KeyOfAgentFile(string file) => Path.GetFileNameWithoutExtension(file)["agent-".Length..];

    private IEnumerable<string> AllFiles() => MainFile.Length == 0 ? [] : [MainFile, .. AgentFiles()];

    private static List<string> ReadLines(string path)
    {
        var lines = File.ReadAllText(path).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static void WriteLines(string path, List<string> lines, bool finalNewline = true)
    {
        File.WriteAllText(path, string.Join('\n', lines) + (finalNewline && lines.Count > 0 ? "\n" : string.Empty), Utf8);
        TouchToLargestStamp(path);
    }

    private static void TouchToLargestStamp(string path)
    {
        var largest = LargestStampOf(path);
        if (largest > DateTime.MinValue)
        {
            File.SetLastWriteTimeUtc(path, largest);
        }
    }

    private static DateTime LargestStampOf(string path) =>
        ReadLines(path).Select(TryStampIn).DefaultIfEmpty(DateTime.MinValue).Max();

    private static DateTime TryStampIn(string line)
    {
        try
        {
            return StampIn(line);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return DateTime.MinValue;
        }
    }

    private static DateTime StampIn(string line)
    {
        using var document = JsonDocument.Parse(line);
        var stamp = document.RootElement.GetProperty("timestamp").GetString() ?? string.Empty;

        return DateTime.Parse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    private static string StampText(DateTime instant) => instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static bool Assign(JsonObject target, string key, JsonNode? value)
    {
        target[key] = value;

        return true;
    }

    private static void Pad(JsonObject line, string extra)
    {
        var key = line.Where(pair => Renamed.IsMatch(pair.Key) && pair.Value is JsonValue value && value.TryGetValue<string>(out _)).Select(pair => pair.Key).FirstOrDefault()
            ?? throw new InvalidOperationException("The line has no renamed string value to pad.");
        line[key] = line[key]!.GetValue<string>() + extra;
    }

    private static string MessageIdOf(JsonObject line) => (line["message"] as JsonObject)?["id"]?.GetValue<string>() ?? string.Empty;

    private static IEnumerable<string> IdsIn(string line)
    {
        using var document = JsonDocument.Parse(line);
        if (!document.RootElement.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return content.EnumerateArray().SelectMany(IdsOf).ToList();
    }

    private static IEnumerable<string> CallsIn(string line, string[] tools)
    {
        using var document = JsonDocument.Parse(line);
        if (!document.RootElement.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return content.EnumerateArray().Where(block => IsCallOf(block, tools)).Select(block => block.GetProperty("id").GetString() ?? string.Empty).ToList();
    }

    private static bool IsCallOf(JsonElement block, string[] tools) =>
        block.TryGetProperty("type", out var type) && type.GetString() == "tool_use" && tools.Contains(block.GetProperty("name").GetString());

    private static IEnumerable<string> IdsOf(JsonElement block) =>
        new[] { "id", "tool_use_id" }.Where(name => block.TryGetProperty(name, out _)).Select(name => block.GetProperty(name).GetString() ?? string.Empty);

    private void EditLine(TranscriptFile file, int n, Action<JsonObject> edit)
    {
        var path = PathOf(file);
        var lines = ReadLines(path);
        var line = (JsonObject)JsonNode.Parse(lines[n - 1])!;
        edit(line);
        lines[n - 1] = line.ToJsonString(Json);
        WriteLines(path, lines);
    }

    /// <summary>Re-serialises only the lines <paramref name="edit"/> changed (it returns true) and leaves every other byte alone.</summary>
    private static void EditEveryLine(string path, Func<JsonObject, bool> edit)
    {
        var lines = ReadLines(path);
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i] = TryEdit(lines[i], edit);
        }

        WriteLines(path, lines);
    }

    private static string TryEdit(string text, Func<JsonObject, bool> edit)
    {
        JsonObject? line;
        try
        {
            line = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return text;
        }

        return line is not null && edit(line) ? line.ToJsonString(Json) : text;
    }

    private void EditMeta(string id, Action<JsonObject> edit)
    {
        var meta = (JsonObject)JsonNode.Parse(File.ReadAllText(MetaPathOf(id)))!;
        edit(meta);
        File.WriteAllText(MetaPathOf(id), meta.ToJsonString(Json), Utf8);
    }

    private void EditResult(TranscriptFile file, string toolUseId, Action<JsonObject, JsonArray> edit)
    {
        var id = ToolUseIdEndingWith(file, toolUseId);
        var done = false;
        EditEveryLine(PathOf(file), line => !done && (done = TryEditResultIn(line, id, edit)));
        if (!done)
        {
            throw new InvalidOperationException($"No tool_result answers {id}.");
        }
    }

    private static bool TryEditResultIn(JsonObject line, string id, Action<JsonObject, JsonArray> edit)
    {
        if ((line["message"] as JsonObject)?["content"] is not JsonArray content)
        {
            return false;
        }

        var block = content.OfType<JsonObject>().FirstOrDefault(b => b["type"]?.GetValue<string>() == "tool_result" && b["tool_use_id"]?.GetValue<string>() == id);
        _ = block is null || Edited(block, content, edit);

        return block is not null;
    }

    private static bool Edited(JsonObject block, JsonArray content, Action<JsonObject, JsonArray> edit)
    {
        edit(block, content);

        return true;
    }

    // ---- roots and paths --------------------------------------------------------------------------------------------------

    private static void CopyTree(string source, string target)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    private static void MakeRoots(FixtureCase fixtureCase, string root)
    {
        foreach (var fixtureRoot in fixtureCase.FixtureRoots)
        {
            Directory.CreateDirectory(Under(root, fixtureRoot));
        }

        foreach (var checkout in fixtureCase.Checkouts)
        {
            Directory.CreateDirectory(Under(root, checkout));
            File.WriteAllBytes(Path.Combine(Under(root, checkout), ".git"), []);
        }
    }

    private static string Under(string root, string fixturePath) => Path.Combine(root, fixturePath[(FixturePrefix.Length + 1)..].Replace('/', Path.DirectorySeparatorChar));

    private void RewriteEveryPath()
    {
        foreach (var file in AllFiles())
        {
            EditEveryLine(file, RewritePathsIn);
        }
    }

    private bool RewritePathsIn(JsonObject line)
    {
        var changed = RewriteKey(line, "cwd", makeFolder: false);
        var content = (line["message"] as JsonObject)?["content"] as JsonArray;

        var inputs = content is null ? [] : content.OfType<JsonObject>().Select(block => RewriteInput(block["input"] as JsonObject)).ToList();

        return inputs.Contains(true) | changed;
    }

    private bool RewriteInput(JsonObject? input) =>
        input is not null && (RewriteKey(input, "file_path", makeFolder: true) | RewriteKey(input, "notebook_path", makeFolder: true));

    private bool RewriteKey(JsonObject owner, string key, bool makeFolder)
    {
        if (owner[key] is not JsonValue value || !value.TryGetValue<string>(out var path) || !IsUnderARoot(path))
        {
            return false;
        }

        var mapped = Map(path);
        owner[key] = mapped;
        _ = !makeFolder || Directory.CreateDirectory(Path.GetDirectoryName(mapped)!).Exists;

        return true;
    }

    private bool IsUnderARoot(string path)
    {
        var folded = path.Replace('\\', '/');

        return Case.FixtureRoots.Any(root => folded.Equals(root, StringComparison.OrdinalIgnoreCase) || folded.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The temp root's real path for a fixture path; the remainder keeps its separators and drive case on Windows.</summary>
    private string Map(string fixturePath)
    {
        var remainder = fixturePath[FixturePrefix.Length..];

        return Root + (OperatingSystem.IsWindows() ? remainder : remainder.Replace('\\', '/'));
    }
}
