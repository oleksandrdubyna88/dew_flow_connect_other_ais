using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CoaiMcp.Store;

/// <summary>What a parsed meta file said. Every string is an id or has passed its shape (S4-18, G11); <c>description</c> is never read.</summary>
internal sealed record MetaFacts(string ToolUseId, string AgentType, string Alias, string ParentAgentId)
{
    public static MetaFacts Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>The agents and launches a read ended with, and the meta alias of each agent key (what a message's <c>Requested</c> falls back to).</summary>
internal sealed record ResolvedLaunches(
    IReadOnlyList<ObservedAgent> Agents,
    IReadOnlyList<UnobservedLaunch> Unobserved,
    IReadOnlyDictionary<string, string> Aliases);

/// <summary>
/// The launches of one session (E3.S4 steps 3 and 8a): every <c>subagents/agent-*.jsonl</c> and <c>agent-*.meta.json</c> is LISTED before
/// any line is read, so a budget stop never leaves a launch unlisted; the parent link, the called instant and the Agent-count check are
/// worked out once every file has been read.
/// </summary>
/// <remarks>
/// <para><b>One missing transcript is one item and one reason.</b> A meta whose file is absent, or whose file yielded no parseable stamped
/// line (S4-23), gives exactly ONE <see cref="UnobservedLaunch"/> and one <c>meta without transcript</c>; the agent listed for the same
/// key with <c>TranscriptPresent == false</c> is inert. An <c>Agent</c> call joined to no meta, and not refused, gives one
/// <c>agent call without transcript</c>.</para>
/// <para>Files come in ordinal key order — the order a budget stop truncates in, and the order the <c>Agents</c> ties break by.</para>
/// </remarks>
internal sealed class TranscriptLaunches
{
    private const int MaxMetaBytes = 64 * 1024;
    private const string Folder = "subagents";

    private readonly Dictionary<string, MetaFacts> _metas;
    private readonly IReadOnlyList<string> _fileKeys;
    private readonly string _folder;

    private TranscriptLaunches(string folder, IReadOnlyList<string> fileKeys, Dictionary<string, MetaFacts> metas)
    {
        _folder = folder;
        _fileKeys = fileKeys;
        _metas = metas;
    }

    /// <summary>The agent keys that have a transcript file, ordinal.</summary>
    public IReadOnlyList<string> FileKeys => _fileKeys;

    public string PathOf(string key) => Path.Combine(_folder, "agent-" + key + ".jsonl");

    /// <summary>
    /// Lists the folder and parses every meta (a few hundred bytes each). A meta that does not parse is <c>torn meta</c> and reads as
    /// absent; a transcript file with no parsed meta and no torn one is <c>agent without meta</c> (S4-6, S4-19).
    /// </summary>
    public static TranscriptLaunches List(string sessionFolder, ReadTally tally)
    {
        var folder = Path.Combine(sessionFolder, Folder);
        var fileKeys = KeysOf(folder, "agent-*.jsonl", ".jsonl");
        var metas = new Dictionary<string, MetaFacts>(StringComparer.Ordinal);
        var torn = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in KeysOf(folder, "agent-*.meta.json", ".meta.json"))
        {
            ReadMeta(folder, key, metas, torn, tally);
        }

        tally.Reason(ObservationReasons.TornMeta, torn.Count);
        tally.Reason(ObservationReasons.AgentWithoutMeta, fileKeys.Count(key => !metas.ContainsKey(key) && !torn.Contains(key)));

        return new TranscriptLaunches(folder, fileKeys, metas);
    }

    /// <summary>
    /// The agents, the unobserved launches and the aliases, once every file has been read (or stopped). <paramref name="afterBudgetStop"/>
    /// skips step 8a: the lines after the stop were never read, so every unmatched meta would otherwise be a false "agent call without
    /// transcript".
    /// </summary>
    public ResolvedLaunches Resolve(IReadOnlyList<FileRead> files, TranscriptMessages messages, bool afterBudgetStop, ReadTally tally)
    {
        var holders = HoldersOf(files);
        var launches = AllKeys().Select(key => Launch(key, files, holders, messages, tally)).ToList();
        var missing = launches.Where(launch => launch.HasMeta && !launch.Present).Select(launch => MetaWithoutTranscript(launch, tally));
        List<UnobservedLaunch> calls = afterBudgetStop ? [] : UnansweredCalls(files, messages, tally);
        var agents = launches.Select(Describe).OrderBy(LaunchOrder).ThenBy(agent => agent.Key, StringComparer.Ordinal);

        return new ResolvedLaunches(
            [MainAgent(), .. agents],
            [.. missing.Concat(calls).OrderBy(UnobservedOrder).ThenBy(launch => launch.ToolUseId, StringComparer.Ordinal)],
            _metas.ToDictionary(pair => pair.Key, pair => pair.Value.Alias, StringComparer.Ordinal));
    }

    /// <summary>Agents by launch: the first stamped line, else the launching call, else last.</summary>
    private static DateTime LaunchOrder(ObservedAgent agent) => agent.LaunchedUtc ?? agent.CalledUtc ?? DateTime.MaxValue;

    /// <summary>Unobserved launches by instant; one without an instant goes last.</summary>
    private static DateTime UnobservedOrder(UnobservedLaunch launch) => launch.Utc ?? DateTime.MaxValue;

    private static ObservedAgent MainAgent() => new("main", string.Empty, string.Empty, string.Empty, string.Empty, null, null, true);

    private IEnumerable<string> AllKeys() => _fileKeys.Union(_metas.Keys, StringComparer.Ordinal);

    /// <summary>One agent key's facts: its meta (if any), its file (if any), who launched it, and when.</summary>
    private sealed record Launched(string Key, bool HasMeta, MetaFacts Meta, string ParentKey, DateTime? CalledUtc, DateTime? FirstStamp)
    {
        /// <summary>S4-23: present means the file yielded a parseable stamped line — never merely that it exists.</summary>
        public bool Present => FirstStamp is not null;
    }

    private Launched Launch(string key, IReadOnlyList<FileRead> files, Dictionary<string, FileRead> holders, TranscriptMessages messages, ReadTally tally)
    {
        var firstStamp = FirstStampOf(files.FirstOrDefault(candidate => !candidate.IsMain && candidate.Key == key), tally);

        return _metas.TryGetValue(key, out var meta)
            ? WithMeta(key, meta, firstStamp, holders, messages, tally)
            : new Launched(key, false, MetaFacts.Empty, string.Empty, null, firstStamp);
    }

    /// <summary>The agent file's first stamp, or null for no file — and for a file with no stamped line, which is counted (S4-23).</summary>
    private static DateTime? FirstStampOf(FileRead? file, ReadTally tally)
    {
        if (file is not null && file.FirstStamp is null)
        {
            tally.AgentFilesEmpty++;
        }

        return file?.FirstStamp;
    }

    private static Launched WithMeta(string key, MetaFacts meta, DateTime? firstStamp, Dictionary<string, FileRead> holders, TranscriptMessages messages, ReadTally tally)
    {
        var holder = holders.GetValueOrDefault(meta.ToolUseId);
        var called = holder is null ? null : messages.InstantOf(holder.Calls[meta.ToolUseId].MessageIds);

        return new Launched(key, true, meta, ParentOf(meta, holder, tally), called, firstStamp);
    }

    /// <summary>
    /// S4-5: the file holding the <c>toolUseId</c> join; else the meta's <c>parentAgentId</c>; else <c>main</c> (a meta without one IS the
    /// statement that main launched it). The join and <c>parentAgentId</c> naming different parents is <c>inconsistent transcript</c>.
    /// </summary>
    private static string ParentOf(MetaFacts meta, FileRead? holder, ReadTally tally)
    {
        if (Contradicts(meta, holder))
        {
            tally.Reason(ObservationReasons.InconsistentTranscript);
        }

        return holder?.Key ?? NamedParent(meta);
    }

    private static bool Contradicts(MetaFacts meta, FileRead? holder) =>
        holder is not null && meta.ParentAgentId.Length > 0 && meta.ParentAgentId != holder.Key;

    private static string NamedParent(MetaFacts meta) => meta.ParentAgentId.Length > 0 ? meta.ParentAgentId : "main";

    private static ObservedAgent Describe(Launched launch) => new(
        launch.Key, launch.ParentKey, launch.Meta.ToolUseId, launch.Meta.AgentType, launch.Meta.Alias, launch.FirstStamp, launch.CalledUtc, launch.Present);

    private static bool IsHeldByNoMeta(FileRead file, KeyValuePair<string, CallRecord> call, HashSet<string> joined) =>
        call.Value.Kind == BlockKind.AgentCall && !joined.Contains(call.Key) && !IsRefused(file, call.Key);

    private static UnobservedLaunch MetaWithoutTranscript(Launched launch, ReadTally tally)
    {
        tally.Reason(ObservationReasons.MetaWithoutTranscript);

        return new UnobservedLaunch(launch.ParentKey, launch.Meta.ToolUseId, launch.CalledUtc, ObservationReasons.MetaWithoutTranscript);
    }

    /// <summary>Every <c>Agent</c> call, by tool-use id, to the FIRST file (read order) that carries it.</summary>
    private static Dictionary<string, FileRead> HoldersOf(IReadOnlyList<FileRead> files)
    {
        var holders = new Dictionary<string, FileRead>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            file.Calls.Where(pair => pair.Value.Kind == BlockKind.AgentCall).ToList().ForEach(pair => holders.TryAdd(pair.Key, file));
        }

        return holders;
    }

    /// <summary>
    /// Step 8a: an <c>Agent</c> call is launched (a meta joins it — a missing result does not matter), refused (its result is an error:
    /// nothing is recorded) or missing: one reason and one launch, stamped with the call's message instant.
    /// </summary>
    private List<UnobservedLaunch> UnansweredCalls(IReadOnlyList<FileRead> files, TranscriptMessages messages, ReadTally tally)
    {
        var joined = _metas.Values.Select(meta => meta.ToolUseId).ToHashSet(StringComparer.Ordinal);
        var missing = files.SelectMany(file => file.Calls.Where(call => IsHeldByNoMeta(file, call, joined)).OrderBy(call => call.Value.Position)
            .Select(call => new UnobservedLaunch(file.Key, call.Key, messages.InstantOf(call.Value.MessageIds), ObservationReasons.AgentCallWithoutTranscript))).ToList();
        tally.Reason(ObservationReasons.AgentCallWithoutTranscript, missing.Count);

        return missing;
    }

    private static bool IsRefused(FileRead file, string toolUseId) => file.Results.TryGetValue(toolUseId, out var result) && result.IsError;

    private static List<string> KeysOf(string folder, string pattern, string suffix)
    {
        try
        {
            return Directory.Exists(folder)
                ? [.. Directory.EnumerateFiles(folder, pattern).Select(Path.GetFileName).OfType<string>().Where(name => name.EndsWith(suffix, StringComparison.Ordinal))
                    .Select(name => name["agent-".Length..^suffix.Length]).Where(key => key.Length > 0).Order(StringComparer.Ordinal)]
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void ReadMeta(string folder, string key, Dictionary<string, MetaFacts> metas, HashSet<string> torn, ReadTally tally)
    {
        if (TryParseMeta(Path.Combine(folder, "agent-" + key + ".meta.json"), out var document))
        {
            using (document)
            {
                metas[key] = FactsOf(document.RootElement, tally);
            }
        }
        else
        {
            torn.Add(key);
        }
    }

    private static MetaFacts FactsOf(JsonElement root, ReadTally tally)
    {
        var model = JsonText.StringProperty(root, "model");
        var type = TranscriptShapes.Admit(JsonText.StringProperty(root, "agentType"), TranscriptShapes.IsAgentType);
        var alias = TranscriptShapes.Admit(model, TranscriptShapes.IsAlias);
        tally.ValuesOffShape += type.OffShape + alias.OffShape;
        tally.MetasWithoutModel += model.Length == 0 ? 1 : 0;

        return new MetaFacts(JsonText.StringProperty(root, "toolUseId"), type.Value, alias.Value, JsonText.StringProperty(root, "parentAgentId"));
    }

    /// <summary>A meta read once and bounded by what is READ: shared, at most 64 KiB, an object. A failure to read is as good as a torn file.</summary>
    private static bool TryParseMeta(string path, [NotNullWhen(true)] out JsonDocument? document)
    {
        document = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[MaxMetaBytes + 1];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            return read <= MaxMetaBytes && TryObject(buffer.AsMemory(0, read), out document);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryObject(ReadOnlyMemory<byte> bytes, [NotNullWhen(true)] out JsonDocument? document)
    {
        document = null;
        if (!TranscriptLines.TryParse(bytes, out var parsed))
        {
            return false;
        }

        if (parsed.RootElement.ValueKind != JsonValueKind.Object)
        {
            parsed.Dispose();

            return false;
        }

        document = parsed;

        return true;
    }
}
