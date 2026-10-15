using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The fixtures under <c>fixtures/caller-transcripts</c> are cut from real Claude Code sessions and PUBLISHED, so this class is
/// the privacy boundary of the repository's tests (todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E1.md, E1.S5).
/// EVERY file of every case is classified and validated — transcripts, metas, <c>rounds.json</c>, registry entries and the
/// manifest — and a file that is none of those fails by name. Every string is the sentinel, an allowlisted shape or an approved
/// vocabulary; every key is structural or <c>k&lt;n&gt;</c>; every path sits under a fixture root; every instant is on the
/// synthetic clock and every count is a small non-negative integer (a real timestamp or a real magnitude is a leak); no byte is a
/// carriage return; children join parents; the manifest's counts equal the files and its prose equals a pinned list.
/// </summary>
/// <remarks>
/// <para>No regex over emails or user names is hard-coded: the shape rule already refuses them, and a real name in a public test
/// file would itself be the leak.</para>
/// <para><b>The policy is pinned here, not only read.</b> <c>allowlist.json</c> is read by the cutter and by this class, so a
/// widened allowlist would widen both at once and every test would still pass. Two independent pins stop that:
/// <see cref="ApprovedAllowlistSha256"/> (any edit to the file must be matched by a deliberate edit of this constant, which a
/// reviewer sees in the same diff) and a probe that every shape, enum and vocabulary REJECTS a set of private-looking strings.
/// A hash alone is silent about WHAT was widened; the probe alone is silent about a harmless narrowing needing no review — together
/// the first makes widening deliberate and the second makes a careless widening fail even after the hash was updated.</para>
/// </remarks>
public sealed partial class ACallerTranscriptFixtureCarriesNoTextTests
{
    /// <summary>SHA-256 of allowlist.json with CRLF read as LF. Update ONLY after reading what changed and whether it widens what a fixture may carry.</summary>
    private const string ApprovedAllowlistSha256 = "b74320a9e3c182c9ccaaa1a7cdfec3a65f8a5b6ff349671670c318ae39626992";

    /// <summary>
    /// The required cases (parent §6, §11 step 0, §12, §16) with the ONLY serves/provenance prose a manifest may carry: this pinned list was
    /// reviewed once, by hand, for dates, clock times, names and ids, and the manifest must equal it exactly — free text in a manifest
    /// is the one place a description of a real session could be typed by hand. It is compared BOTH ways with the manifest and the folder.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Serves, string Provenance)> RequiredCases = new Dictionary<string, (string, string)>
    {
        ["message-as-several-lines"] = ("one message id spans several lines whose usage grows; the figure to take is the maximum over its lines", "cut"),
        ["message-last-line-below-max"] = ("a message whose last line does not hold the largest usage: the figure to take is the maximum, never the last line", "derived: cut, the first and last line of one message swapped their usage"),
        ["model-switch-in-window"] = ("the caller changes model inside a round window", "derived: cut, the model id of every assistant message after the first changed"),
        ["resumed-agent"] = ("an agent resumed after a round started keeps writing under its original id", "cut, agent file kept whole"),
        ["background-agent-across-round-start"] = ("a background agent launched before a round starts is still writing after it", "cut"),
        ["nested-agents"] = ("an agent launched from another agent (depth 2) carries its parent id", "cut, agent files clipped to the window"),
        ["two-repositories-one-session"] = ("one caller session writes into two repositories, one gate session each", "cut, agent files clipped to the window"),
        ["missing-child-transcripts"] = ("an agent meta without its file, and an Agent call with neither meta nor file", "derived: cut, agent files and metas dropped"),
        ["second-gate-session-same-caller"] = ("the same caller opens a second gate session in one repository", "derived: cut, rounds file shaped for the case"),
        ["replay-same-round-twice"] = ("the same round listed twice must not be counted twice", "derived: cut, rounds file shaped for the case"),
        ["persistence-off"] = ("a caller that ran without session persistence: a registry entry and rounds, no transcript", "derived: no transcript, rounds file and registry entry written by hand"),
        ["work-before-open"] = ("the caller wrote before the gate session was opened", "cut, agent files clipped to the window"),
        ["meta-without-model"] = ("a launch without a model argument writes a meta without one", "cut"),
        ["no-effort-lines"] = ("assistant lines without an effort field", "cut; the only assistant lines without an effort field are synthetic ones"),
        ["registry-and-variable-disagree"] = ("the registry session id differs from the id of the transcript", "derived: cut, registry entry written by hand with another session id"),
        ["writes-only-elsewhere"] = ("the only write in the window is a scratch file outside the gated repository", "cut"),
        ["no-evidence-two-repositories"] = ("two gate sessions in two repositories and a transcript with no write", "cut"),
        ["skipped-round-between-recorded"] = ("rounds 1 and 3 are recorded, round 2 is not", "derived: cut, rounds file shaped for the case"),
        ["write-into-submodule-and-scratchpad"] = ("one write lands in a submodule of the gated repository, one in the scratchpad", "derived: cut, one repository write moved into a submodule path"),
        ["refused-agent-call"] = ("an Agent call refused by the model guard: an error result, no meta, no file", "derived: cut, an agent dropped and its result marked as an error"),
    };

    /// <summary>
    /// The folder of each case: SHORT, because a fixture path must stay under Windows' 260 characters when a checkout lives in a long
    /// folder (the coai server's review worktree is about 71 characters deep and git adds ".lock"). The human name lives in the manifest.
    /// Pinned here so a folder cannot be renamed, added or dropped without this list changing in the same diff.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RequiredDirs = new Dictionary<string, string>
    {
        ["background-agent-across-round-start"] = "c01",
        ["message-as-several-lines"] = "c02",
        ["message-last-line-below-max"] = "c03",
        ["meta-without-model"] = "c04",
        ["missing-child-transcripts"] = "c05",
        ["model-switch-in-window"] = "c06",
        ["nested-agents"] = "c07",
        ["no-effort-lines"] = "c08",
        ["no-evidence-two-repositories"] = "c09",
        ["persistence-off"] = "c10",
        ["refused-agent-call"] = "c11",
        ["registry-and-variable-disagree"] = "c12",
        ["replay-same-round-twice"] = "c13",
        ["resumed-agent"] = "c14",
        ["second-gate-session-same-caller"] = "c15",
        ["skipped-round-between-recorded"] = "c16",
        ["two-repositories-one-session"] = "c17",
        ["work-before-open"] = "c18",
        ["write-into-submodule-and-scratchpad"] = "c19",
        ["writes-only-elsewhere"] = "c20",
    };

    private static string NameOfDir(string dir) => RequiredDirs.Where(p => p.Value == dir).Select(p => p.Key).DefaultIfEmpty(dir).First();

    /// <summary>A fixture path, relative to the repository root, may not exceed this: about 71 (worktree root) + 160 + 5 (".lock") stays well under 260.</summary>
    private const int MaxRelativePathLength = 160;
    private const string FixtureRootFromRepo = "src_mcp/tests/fixtures/caller-transcripts/";

    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "fixtures", "caller-transcripts");
    private static readonly Regex Renamed = RenamedKey();
    private static readonly Regex Segment = MappedSegment();
    private static readonly Regex FixtureRoot = FixtureRootShape();
    private static readonly Regex Count = NonNegativeInteger();

    [GeneratedRegex(@"^k\d+\z")] private static partial Regex RenamedKey();
    [GeneratedRegex(@"^p[0-9]+([.][a-z0-9]{1,5})?\z")] private static partial Regex MappedSegment();
    [GeneratedRegex(@"^([A-Za-z]:/fixture|/fixture)/(repo-[a-z]|other|scratch)\z")] private static partial Regex FixtureRootShape();
    [GeneratedRegex(@"^(0|[1-9][0-9]*)\z")] private static partial Regex NonNegativeInteger();
    [GeneratedRegex(@"^projects/[^/]+/[^/]+\.jsonl\z")] private static partial Regex MainFilePath();
    [GeneratedRegex(@"^projects/[^/]+/[^/]+/subagents/agent-[0-9a-f]+\.jsonl\z")] private static partial Regex AgentFilePath();
    [GeneratedRegex(@"^projects/[^/]+/[^/]+/subagents/agent-[0-9a-f]+\.meta\.json\z")] private static partial Regex MetaFilePath();
    [GeneratedRegex(@"^sessions/[0-9]{1,9}\.json\z")] private static partial Regex RegistryFilePath();
    [GeneratedRegex(@"^projects/(?<slug>(-|[A-Z]--)fixture(-(repo-[a-z]|other|scratch)(-p[0-9]+)*)?)/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(\.jsonl|/subagents/agent-[0-9a-f]{8,64}\.(jsonl|meta\.json))\z")]
    private static partial Regex WholeTranscriptPath();
    [GeneratedRegex(@"^2000-01-01T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3}Z\z")] private static partial Regex SyntheticInstant();
    [GeneratedRegex(@"^[0-9a-f]{40}\z")] private static partial Regex ShaForty();

    /// <summary>Output forms the cutter produces for remapped ids: lowercase hex after the prefix, so a typed word cannot pass as an id.</summary>
    private static readonly IReadOnlyDictionary<string, Regex> IdOutputForms = new Dictionary<string, Regex>
    {
        ["msgid"] = new(@"^msg_[0-9a-f]{8,64}\z"), ["toolid"] = new(@"^toolu_[0-9a-f]{8,80}\z"),
        ["uuid"] = new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\z"), ["hexid"] = new(@"^[0-9a-f]{8,64}\z"),
    };

    // ---- the allowlist, read as the cutter reads it ------------------------------------------------------------------
    private sealed record Allowlist(string Sentinel, IReadOnlyDictionary<string, Regex> Shapes, IReadOnlyDictionary<string, Vocabulary> Vocabularies,
        IReadOnlyDictionary<string, string> Fields, IReadOnlySet<string> KeptKeys);

    private sealed record Vocabulary(IReadOnlySet<string> Builtin, Regex Rewritten);

    private static Regex Anchored(string source) => new(source.EndsWith('$') ? source[..^1] + @"\z" : source);

    private static readonly Regex NeverMatches = new("(?!)");

    private static Allowlist LoadAllowlist()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "allowlist.json")));
        var top = doc.RootElement;
        var fields = top.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("path").GetString()!, f => f.GetProperty("shape").GetString()!);
        var kept = fields.Keys.SelectMany(p => p.Split('.')).Select(s => s.Replace("[]", string.Empty)).ToHashSet();
        return new Allowlist(
            top.GetProperty("sentinel").GetString()!,
            top.GetProperty("shapes").EnumerateObject().ToDictionary(p => p.Name, p => Anchored(p.Value.GetString()!)),
            top.GetProperty("vocabularies").EnumerateObject().ToDictionary(p => p.Name, p => new Vocabulary(
                p.Value.GetProperty("builtin").EnumerateArray().Select(e => e.GetString()!).ToHashSet(),
                p.Value.TryGetProperty("rewritten", out var rewritten) ? Anchored(rewritten.GetString()!) : NeverMatches)),
            fields, kept);
    }

    // ---- the cases on disk -------------------------------------------------------------------------------------------
    private sealed record Case(string Name, string Dir, IReadOnlyList<string> Files)
    {
        public IReadOnlyList<string> MainFiles => Files.Where(f => MainFilePath().IsMatch(f)).ToList();
        public string Main => MainFiles.Count == 1 ? MainFiles[0] : string.Empty;
        public IEnumerable<string> AgentFiles => Files.Where(f => AgentFilePath().IsMatch(f));
        public IEnumerable<string> Metas => Files.Where(f => MetaFilePath().IsMatch(f));
        public IEnumerable<string> Registry => Files.Where(f => RegistryFilePath().IsMatch(f));
        public IEnumerable<string> TranscriptFiles => MainFiles.Concat(AgentFiles);
        public string Read(string relative) => File.ReadAllText(Path.Combine(Dir, relative));
        public IEnumerable<JsonDocument> Lines(string relative) =>
            Read(relative).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonDocument.Parse(l)).ToList();
    }

    private static IReadOnlyList<Case> LoadCases() => Directory.GetDirectories(Root).OrderBy(d => d, StringComparer.Ordinal)
        .Select(d => new Case(NameOfDir(Path.GetFileName(d)), d, Directory.GetFiles(d, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(d, f).Replace('\\', '/')).OrderBy(f => f, StringComparer.Ordinal).ToList())).ToList();

    private static bool IsCaseFile(string f) => f == "rounds.json" || MainFilePath().IsMatch(f) || AgentFilePath().IsMatch(f) || MetaFilePath().IsMatch(f) || RegistryFilePath().IsMatch(f);

    // ---- the manifest ------------------------------------------------------------------------------------------------
    private sealed record ManifestCase(string Name, string Dir, string Serves, string Provenance, string Version, string Side, string PathStyle, bool HasTranscript, bool BreaksJoin,
        IReadOnlyList<string> FixtureRoots, int AssistantLines, int DistinctMessageIds, int AgentFiles, int Metas, int AgentToolUses);

    private static readonly Node ManifestSchema = new Obj(new Dictionary<string, Node> { ["cases"] = new Arr(new Obj(new Dictionary<string, Node>
    {
        ["name"] = new Str("enum-cases"), ["dir"] = new Str("enum-dirs"), ["serves"] = new Str("free"), ["provenance"] = new Str("free"), ["side"] = new Str("enum:windows|wsl"),
        ["hasTranscript"] = new Bool(), ["pathStyle"] = new Str("enum:none|slash|backslash|backslash+slash"), ["version"] = new Str("manifest-version"),
        ["breaksJoin"] = new Bool(), ["fixtureRoots"] = new Arr(new Str("fixture-root")),
        ["counts"] = new Obj(new Dictionary<string, Node> { ["assistantLines"] = new Num(), ["distinctMessageIds"] = new Num(), ["agentFiles"] = new Num(), ["metas"] = new Num(), ["agentToolUses"] = new Num() }),
    })) });

    private static IReadOnlyList<ManifestCase> LoadManifest()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        return doc.RootElement.GetProperty("cases").EnumerateArray().Select(c =>
        {
            var counts = c.GetProperty("counts");
            return new ManifestCase(c.GetProperty("name").GetString()!, c.GetProperty("dir").GetString()!, c.GetProperty("serves").GetString()!, c.GetProperty("provenance").GetString()!,
                c.GetProperty("version").GetString()!, c.GetProperty("side").GetString()!, c.GetProperty("pathStyle").GetString()!, c.GetProperty("hasTranscript").GetBoolean(),
                c.GetProperty("breaksJoin").GetBoolean(), c.GetProperty("fixtureRoots").EnumerateArray().Select(r => r.GetString()!).ToList(),
                counts.GetProperty("assistantLines").GetInt32(), counts.GetProperty("distinctMessageIds").GetInt32(), counts.GetProperty("agentFiles").GetInt32(),
                counts.GetProperty("metas").GetInt32(), counts.GetProperty("agentToolUses").GetInt32());
        }).ToList();
    }

    /// <summary>The manifest entry of a case on disk; a case with none fails here, by name, instead of as an opaque "no matching element".</summary>
    private static ManifestCase ManifestOf(IEnumerable<ManifestCase> manifest, string name)
    {
        var found = manifest.Where(m => m.Name == name).ToList();
        found.Should().ContainSingle($"case '{name}' must be listed in manifest.json");
        return found[0];
    }

    // ---- the side-file schemas (rounds.json, registry entry, manifest): unknown members are violations ----------------
    private abstract record Node;
    private sealed record Obj(IReadOnlyDictionary<string, Node> Members) : Node;
    private sealed record Arr(Node Item) : Node;
    private sealed record Str(string Shape) : Node;
    private sealed record Num : Node;
    private sealed record Bool : Node;

    private static readonly Node RoundsSchema = new Obj(new Dictionary<string, Node>
    {
        ["callerSessionId"] = new Str("uuid"),
        ["gateSessions"] = new Arr(new Obj(new Dictionary<string, Node>
        {
            ["id"] = new Str("uuid"), ["repoPath"] = new Str("fixture-path"), ["openedUtc"] = new Str("iso-utc"),
            ["rounds"] = new Arr(new Obj(new Dictionary<string, Node>
            {
                ["number"] = new Num(), ["startedUtc"] = new Str("iso-utc"), ["completedUtc"] = new Str("iso-utc"),
                ["status"] = new Str("enum:done|running|failed|cancelled"), ["headSha"] = new Str("sha40"),
            })),
        })),
    });

    private static readonly Node RegistrySchema = new Obj(new Dictionary<string, Node> { ["sessionId"] = new Str("uuid"), ["cwd"] = new Str("fixture-path"), ["version"] = new Str("version") });

    private sealed record Ctx(Allowlist List, IReadOnlyList<string> Roots, IReadOnlyCollection<string> CaseNames, long MaxCount);

    private static IEnumerable<string> Validate(Ctx ctx, JsonElement element, Node node, string path)
    {
        return node switch
        {
            Obj obj => ValidateObject(ctx, element, obj, path),
            Arr arr => element.ValueKind != JsonValueKind.Array ? [$"'{path}' is not an array"] : element.EnumerateArray().SelectMany((e, i) => Validate(ctx, e, arr.Item, $"{path}[{i}]")).ToList(),
            Str str => element.ValueKind != JsonValueKind.String ? [$"'{path}' is not a string"] : ValidateString(ctx, element.GetString()!, str.Shape, path),
            Num => element.ValueKind == JsonValueKind.Number && Count.IsMatch(element.GetRawText()) ? [] : [$"'{path}' is not a non-negative integer"],
            Bool => element.ValueKind is JsonValueKind.True or JsonValueKind.False ? [] : [$"'{path}' is not a boolean"],
            _ => [$"'{path}': unknown schema node"],
        };
    }

    private static IEnumerable<string> ValidateObject(Ctx ctx, JsonElement element, Obj obj, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) return [$"'{path}' is not an object"];
        var present = element.EnumerateObject().ToList();
        var unknown = present.Where(p => !obj.Members.ContainsKey(p.Name)).Select(p => $"'{path}' carries the unknown member '{Clip(p.Name)}'");
        var missing = obj.Members.Keys.Where(k => present.All(p => p.Name != k)).Select(k => $"'{path}' lacks the member '{k}'");
        var children = present.Where(p => obj.Members.ContainsKey(p.Name)).SelectMany(p => Validate(ctx, p.Value, obj.Members[p.Name], $"{path}.{p.Name}"));
        return unknown.Concat(missing).Concat(children).ToList();
    }

    private static IEnumerable<string> ValidateString(Ctx ctx, string value, string shape, string path)
    {
        var ok = shape switch
        {
            "free" => true,
            "enum-cases" => ctx.CaseNames.Contains(value),
            "enum-dirs" => RequiredDirs.Values.Contains(value),
            "sha40" => ShaForty().IsMatch(value),
            "manifest-version" => value == "none" || Accepts(ctx.List, "version", value, ctx.Roots),
            "fixture-root" => FixtureRoot.IsMatch(value),
            _ => Accepts(ctx.List, shape, value, ctx.Roots),
        };
        return ok ? [] : [$"'{path}' is '{Clip(value)}', which is not a {shape}"];
    }

    // ---- the transcript walk -----------------------------------------------------------------------------------------
    private static string Clip(string text) => text.Length <= 60 ? text : text[..60] + "...";

    private static bool UnderRoot(string path, IEnumerable<string> roots)
    {
        var folded = path.Replace('\\', '/');
        return roots.Any(r => folded == r || folded.StartsWith(r + "/", StringComparison.Ordinal)) && folded.Split('/').Skip(3).All(Segment.IsMatch);
    }

    private static bool RelativeMapped(string value) => !value.StartsWith('/') && !value.Contains(':') && value.Replace('\\', '/').Split('/').All(Segment.IsMatch);

    private static bool Accepts(Allowlist list, string shape, string value, IReadOnlyList<string> roots)
    {
        if (value == list.Sentinel) return true;
        if (shape == "fixture-path") return UnderRoot(value, roots) || RelativeMapped(value);
        if (shape == "iso-utc") return SyntheticInstant().IsMatch(value);
        if (shape.StartsWith("enum:", StringComparison.Ordinal)) return shape[5..].Split('|').Contains(value);
        if (list.Vocabularies.TryGetValue(shape, out var vocabulary)) return vocabulary.Builtin.Contains(value) || vocabulary.Rewritten.IsMatch(value);
        return list.Shapes.TryGetValue(shape, out var re) && re.IsMatch(value) && (!IdOutputForms.TryGetValue(shape, out var form) || form.IsMatch(value));
    }

    /// <summary>Every violation in one JSON value; <paramref name="locked"/> is true below a renamed key, where nothing is allowlisted any more.</summary>
    private static IEnumerable<string> Walk(Ctx ctx, JsonElement element, string path, bool locked)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().SelectMany(p => WalkProperty(ctx, p, path, locked)).ToList(),
            JsonValueKind.Array => element.EnumerateArray().SelectMany(e => Walk(ctx, e, path + "[]", locked)).ToList(),
            JsonValueKind.String => CheckString(ctx, element.GetString()!, path, locked),
            JsonValueKind.Number => CheckNumber(ctx, element, path, locked),
            _ => [],
        };
    }

    private static IEnumerable<string> WalkProperty(Ctx ctx, JsonProperty property, string path, bool locked)
    {
        var renamed = Renamed.IsMatch(property.Name);
        var structural = !locked && ctx.List.KeptKeys.Contains(property.Name);
        var childPath = path.Length == 0 ? property.Name : path + "." + property.Name;
        var own = !renamed && !structural ? [$"key '{Clip(property.Name)}' under '{path}' is neither a structural key nor k<n>"] : Array.Empty<string>();
        return own.Concat(Walk(ctx, property.Value, childPath, locked || renamed || !structural)).ToList();
    }

    private static IEnumerable<string> CheckString(Ctx ctx, string value, string path, bool locked)
    {
        if (locked || !ctx.List.Fields.TryGetValue(path, out var shape)) return value == ctx.List.Sentinel ? [] : [$"'{path}' is '{Clip(value)}', not the sentinel"];
        if (shape == "iso-utc" && !Accepts(ctx.List, shape, value, ctx.Roots)) return [$"'{path}' is '{Clip(value)}', which is not on the synthetic clock"];
        return Accepts(ctx.List, shape, value, ctx.Roots) ? [] : [$"'{path}' is '{Clip(value)}', which is not a {shape}"];
    }

    private static IEnumerable<string> CheckNumber(Ctx ctx, JsonElement number, string path, bool locked)
    {
        var raw = number.GetRawText();
        if (raw == "0") return [];
        var isInt = !locked && ctx.List.Fields.TryGetValue(path, out var shape) && shape == "int";
        if (!isInt) return [$"'{path}' holds the number {Clip(raw)} where only 0 is allowed"];
        if (!Count.IsMatch(raw)) return [$"'{path}' is {Clip(raw)}, not a non-negative integer"];
        var limit = path == "spawnDepth" ? 10 : ctx.MaxCount;
        return long.Parse(raw) <= limit ? [] : [$"'{path}' is {Clip(raw)}, a real magnitude: a count is a rank and never exceeds {limit}"];
    }

    private static IEnumerable<string> TranscriptViolations(Allowlist list, Case c, IReadOnlyList<string> roots)
    {
        var total = c.TranscriptFiles.Sum(f => c.Read(f).Count(ch => ch == '\n')) + 1;
        var ctx = new Ctx(list, roots, [], total);
        var lines = c.TranscriptFiles.SelectMany(f => c.Lines(f).SelectMany((d, i) => Walk(ctx, d.RootElement, string.Empty, false).Select(v => $"{c.Name}/{f}:{i + 1}: {v}")));
        var metas = c.Metas.SelectMany(m => { using var d = JsonDocument.Parse(c.Read(m)); return Walk(ctx, d.RootElement, string.Empty, false).Select(v => $"{c.Name}/{m}: {v}").ToList(); });
        return lines.Concat(metas).ToList();
    }

    private static IEnumerable<string> SideFileViolations(Allowlist list, Case c, IReadOnlyList<string> roots)
    {
        var ctx = new Ctx(list, roots, [], 0);
        var problems = new List<string>();
        foreach (var file in c.Files.Where(f => f == "rounds.json" || RegistryFilePath().IsMatch(f)))
        {
            using var doc = JsonDocument.Parse(c.Read(file));
            problems.AddRange(Validate(ctx, doc.RootElement, file == "rounds.json" ? RoundsSchema : RegistrySchema, file).Select(v => $"{c.Name}/{v}"));
        }
        return problems;
    }

    private static IEnumerable<string> FileShapeViolations(Case c, ManifestCase m)
    {
        var strays = c.Files.Where(f => !IsCaseFile(f)).Select(f => $"{c.Name}/{f} is not a transcript, meta, rounds file or registry entry");
        var wrongNames = c.TranscriptFiles.Concat(c.Metas).Where(f => !WholeTranscriptPath().IsMatch(f)).Select(f => $"{c.Name}/{f} has a name or folder outside the fixture's own forms");
        var wrongCount = c.MainFiles.Count == (m.HasTranscript ? 1 : 0) ? [] : new[] { $"{c.Name}: {c.MainFiles.Count} main transcripts where the manifest says hasTranscript={m.HasTranscript}" };
        var rounds = c.Files.Count(f => f == "rounds.json") == 1 ? [] : new[] { $"{c.Name}: exactly one rounds.json is required" };
        return strays.Concat(wrongNames).Concat(wrongCount).Concat(rounds).Concat(KinFolderViolations(c)).ToList();
    }

    /// <summary>Agent files and metas live under THE main transcript's own session folder and project slug, never another's.</summary>
    private static IEnumerable<string> KinFolderViolations(Case c)
    {
        if (c.Main.Length == 0) return [];
        var home = Path.GetDirectoryName(c.Main)!.Replace('\\', '/') + "/" + Path.GetFileNameWithoutExtension(c.Main) + "/subagents/";
        return c.AgentFiles.Concat(c.Metas).Where(f => !f.StartsWith(home, StringComparison.Ordinal)).Select(f => $"{c.Name}/{f} is not under the main transcript's own subagents folder");
    }

    private static IEnumerable<string> Violations(Func<Allowlist, Case, ManifestCase, IEnumerable<string>> check)
    {
        var list = LoadAllowlist();
        var manifest = LoadManifest();
        return LoadCases().SelectMany(c => check(list, c, ManifestOf(manifest, c.Name))).ToList();
    }

    // ---- the tests ---------------------------------------------------------------------------------------------------
    [Fact]
    public void EveryStringOffTheAllowlistIsTheSentinel() =>
        Violations((l, c, m) => TranscriptViolations(l, c, m.FixtureRoots).Where(v => v.Contains("not the sentinel"))).Should().BeEmpty();

    [Fact]
    public void EveryAllowlistedStringHasItsShape() =>
        Violations((l, c, m) => TranscriptViolations(l, c, m.FixtureRoots).Where(v => v.Contains("which is not a ") || v.Contains("synthetic clock"))).Should().BeEmpty();

    [Fact]
    public void EveryNumberIsZeroOrASmallNonNegativeIntegerAtAnIntPath() =>
        Violations((l, c, m) => TranscriptViolations(l, c, m.FixtureRoots).Where(v => v.Contains("where only 0") || v.Contains("non-negative integer") || v.Contains("a real magnitude"))).Should().BeEmpty();

    [Fact]
    public void EveryObjectKeyIsStructuralOrRenamed() =>
        Violations((l, c, m) => TranscriptViolations(l, c, m.FixtureRoots).Where(v => v.Contains("neither a structural key"))).Should().BeEmpty();

    [Fact]
    public void EveryPathLivesUnderAFixtureRootAndEverySideFileMatchesItsSchema()
    {
        LoadManifest().SelectMany(m => m.FixtureRoots.Where(r => !FixtureRoot.IsMatch(r)).Select(r => $"{m.Name}: root '{Clip(r)}'")).Should().BeEmpty(
            "a manifest root is one of D:/fixture/repo-<x>, /other or /scratch, never something derived from a real path");
        Violations((l, c, m) => SideFileViolations(l, c, m.FixtureRoots)).Should().BeEmpty(
            "rounds.json and registry entries are closed schemas: an unknown member, a free string or an instant off the synthetic clock fails");
    }

    [Fact]
    public void EveryFileOfEveryCaseIsKnownAndEachCaseHasExactlyOneMainTranscriptIffItHasOne() =>
        Violations((_, c, m) => FileShapeViolations(c, m)).Should().BeEmpty();

    [Fact]
    public void TheManifestIsAClosedSchemaAndItsProseIsThePinnedList()
    {
        var list = LoadAllowlist();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        Validate(new Ctx(list, [], RequiredCases.Keys.ToList(), 0), doc.RootElement, ManifestSchema, "manifest").Should().BeEmpty();
        var prose = LoadManifest().Where(m => RequiredCases.TryGetValue(m.Name, out var pinned) && (pinned.Serves != m.Serves || pinned.Provenance != m.Provenance))
            .Select(m => $"{m.Name}: serves/provenance differ from the pinned list");
        prose.Should().BeEmpty("manifest prose is limited to the pinned list in this test; a description typed into the manifest is unreviewed free text");
    }

    [Fact]
    public void NoFixtureCarriesACarriageReturn() =>
        Violations((_, c, _) => c.Files.Where(f => File.ReadAllBytes(Path.Combine(c.Dir, f)).Contains((byte)'\r')).Select(f => $"{c.Name}/{f} has a CR byte")).Should().BeEmpty();

    [Fact]
    public void EveryChildJoinsItsParentUnlessTheCaseIsTheBreak()
    {
        Violations((_, c, m) => JoinProblems(c, m.BreaksJoin)).Should().BeEmpty();
        var manifest = LoadManifest();
        LoadCases().Where(c => ManifestOf(manifest, c.Name).BreaksJoin).Should().OnlyContain(c => c.Metas.Count() != c.AgentFiles.Count(), "a case that declares the break must actually contain it");
    }

    private static IEnumerable<string> JoinProblems(Case c, bool breaksJoin)
    {
        var launched = c.TranscriptFiles.SelectMany(f => c.Lines(f)).SelectMany(d => ContentBlocks(d.RootElement))
            .Where(b => Prop(b, "name") == "Agent").Select(b => Prop(b, "id")).ToHashSet();
        var agentIds = c.AgentFiles.Select(f => Path.GetFileNameWithoutExtension(f)["agent-".Length..]).ToHashSet();
        var metaIds = c.Metas.Select(f => Path.GetFileName(f)["agent-".Length..^".meta.json".Length]).ToHashSet();
        var problems = new List<string>();
        foreach (var meta in c.Metas)
        {
            using var doc = JsonDocument.Parse(c.Read(meta));
            if (!launched.Contains(Prop(doc.RootElement, "toolUseId"))) problems.Add($"{c.Name}/{meta}: toolUseId joins no Agent tool_use");
            var parent = Prop(doc.RootElement, "parentAgentId");
            if (parent.Length > 0 && !agentIds.Contains(parent)) problems.Add($"{c.Name}/{meta}: parentAgentId names no agent file");
        }
        if (!breaksJoin)
        {
            problems.AddRange(metaIds.Except(agentIds).Select(id => $"{c.Name}: meta of agent {Clip(id)} has no file"));
            problems.AddRange(agentIds.Except(metaIds).Select(id => $"{c.Name}: agent file {Clip(id)} has no meta"));
        }
        return problems;
    }

    private static IEnumerable<JsonElement> ContentBlocks(JsonElement line) =>
        line.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object && m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().ToList() : [];

    /// <summary>The string value of a property, or the empty string when it is absent or not a string (no null leaves this method).</summary>
    private static string Prop(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    [Fact]
    public void TheManifestCountsWhatTheFilesHold() =>
        Violations((_, c, m) => CountProblems(c, m)).Should().BeEmpty();

    private static IEnumerable<string> CountProblems(Case c, ManifestCase m)
    {
        var lines = c.TranscriptFiles.SelectMany(f => c.Lines(f)).Select(d => d.RootElement).ToList();
        var assistants = lines.Where(l => Prop(l, "type") == "assistant").ToList();
        var ids = assistants.Select(a => Prop(a.GetProperty("message"), "id")).Where(i => i.Length > 0).ToHashSet();
        var agentCalls = lines.SelectMany(ContentBlocks).Count(b => Prop(b, "type") == "tool_use" && Prop(b, "name") == "Agent");
        var actual = (assistants.Count, ids.Count, c.AgentFiles.Count(), c.Metas.Count(), agentCalls);
        var expected = (m.AssistantLines, m.DistinctMessageIds, m.AgentFiles, m.Metas, m.AgentToolUses);
        return actual == expected ? [] : [$"{c.Name}: counts (assistant, ids, agent files, metas, Agent calls) are {actual} but the manifest says {expected}"];
    }

    [Fact]
    public void EveryRequiredCaseHasAFixtureAndNoFixtureIsUnlisted()
    {
        var onDisk = LoadCases().Select(c => c.Name).ToList();
        var listed = LoadManifest().Select(m => m.Name).ToList();
        RequiredCases.Keys.Except(onDisk).Should().BeEmpty("a required case has no fixture directory");
        onDisk.Except(RequiredCases.Keys).Should().BeEmpty("a fixture directory is not a required case");
        RequiredCases.Keys.Except(listed).Should().BeEmpty("a required case is missing from manifest.json");
        listed.Except(RequiredCases.Keys).Should().BeEmpty("manifest.json lists a case this test does not require");
        listed.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheSentinelIsActuallyPresent()
    {
        var withMain = LoadCases().Where(c => c.Main.Length > 0).ToList();
        withMain.Should().HaveCountGreaterThan(10);
        var list = LoadAllowlist();
        withMain.Where(c => !c.Read(c.Main).Contains(list.Sentinel, StringComparison.Ordinal)).Select(c => c.Name).Should().BeEmpty(
            "a sanitiser that dropped every field would pass the negative tests");
        LoadCases().Where(c => c.Main.Length == 0).Select(c => c.Name).Should().BeEquivalentTo(["persistence-off"], "only a persistence-off caller has no transcript");
    }

    [Fact]
    public void TheRegistryAgreesOrDisagreesAsItsCaseSays()
    {
        string RegistryId(Case c) { using var d = JsonDocument.Parse(c.Read(c.Registry.Single())); return d.RootElement.GetProperty("sessionId").GetString()!; }
        string CallerId(Case c) { using var d = JsonDocument.Parse(c.Read("rounds.json")); return d.RootElement.GetProperty("callerSessionId").GetString()!; }
        var cases = LoadCases().ToDictionary(c => c.Name);
        RegistryId(cases["persistence-off"]).Should().Be(CallerId(cases["persistence-off"]));
        RegistryId(cases["registry-and-variable-disagree"]).Should().NotBe(CallerId(cases["registry-and-variable-disagree"]));
        Path.GetFileNameWithoutExtension(cases["registry-and-variable-disagree"].Main).Should().Be(CallerId(cases["registry-and-variable-disagree"]));
    }

    [Fact]
    public void EveryRoundStartsBeforeItCompletesOnTheSyntheticClock()
    {
        foreach (var c in LoadCases())
        {
            using var doc = JsonDocument.Parse(c.Read("rounds.json"));
            foreach (var g in doc.RootElement.GetProperty("gateSessions").EnumerateArray())
                foreach (var r in g.GetProperty("rounds").EnumerateArray())
                    string.CompareOrdinal(r.GetProperty("startedUtc").GetString(), r.GetProperty("completedUtc").GetString()).Should().BeLessThan(0, $"{c.Name}: a round starts before it completes");
        }
    }

    [Fact]
    public void TheFixtureRootHoldsExactlyTheAllowlistTheManifestAndTheRequiredCases()
    {
        var entries = new DirectoryInfo(Root).EnumerateFileSystemInfos().Select(e => e.Name).ToList();
        var expected = RequiredDirs.Values.Concat(["allowlist.json", "manifest.json"]).ToList();
        entries.Except(expected).Should().BeEmpty("a stray file at the fixture root (a case spec with real values, a draft, a note) would be published with the rest");
        expected.Except(entries).Should().BeEmpty("the root lacks a required entry");
    }

    [Fact]
    public void EveryCaseSitsInItsPinnedShortFolderAndTheManifestSaysWhich()
    {
        RequiredDirs.Keys.Except(RequiredCases.Keys).Concat(RequiredCases.Keys.Except(RequiredDirs.Keys)).Should().BeEmpty("the folder list and the case list are the same set");
        RequiredDirs.Values.Should().OnlyHaveUniqueItems();
        LoadManifest().Where(m => !RequiredDirs.TryGetValue(m.Name, out var dir) || dir != m.Dir).Select(m => m.Name).Should().BeEmpty("the manifest names the pinned folder of each case");
    }

    [Fact]
    public void NoFixturePathIsLongerThan160Characters()
    {
        var tooLong = Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => FixtureRootFromRepo + Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .Where(p => p.Length > MaxRelativePathLength).Select(p => $"{p.Length}: {Clip(p)}").ToList();
        tooLong.Should().BeEmpty($"a path over {MaxRelativePathLength} characters relative to the repository root breaks 'git worktree add' on Windows when the checkout sits in a long folder");
    }

    // ---- the relations the cases exist for: a regeneration that loses one must fail, not pass vacuously -------------------
    // A round's window is [the gate session's open or the previous round's start, this round's start): evidence only BEFORE the start.

    private sealed record Window(string Case, string Repo, int Round, string Lo, string Hi);

    private static IEnumerable<Window> WindowsOf(Case c)
    {
        using var doc = JsonDocument.Parse(c.Read("rounds.json"));
        var windows = new List<Window>();
        foreach (var g in doc.RootElement.GetProperty("gateSessions").EnumerateArray())
        {
            var lo = g.GetProperty("openedUtc").GetString()!;
            var n = 0;
            foreach (var r in g.GetProperty("rounds").EnumerateArray())
            {
                var start = r.GetProperty("startedUtc").GetString()!;
                windows.Add(new Window(c.Name, g.GetProperty("repoPath").GetString()!, ++n, lo, start));
                lo = start;
            }
        }
        return windows;
    }

    private sealed record Line(string At, JsonElement Json);

    private static IEnumerable<Line> InWindow(Case c, Window w) => c.TranscriptFiles.SelectMany(f => c.Lines(f))
        .Select(d => new Line(Prop(d.RootElement, "timestamp"), d.RootElement))
        .Where(l => string.CompareOrdinal(l.At, w.Lo) >= 0 && string.CompareOrdinal(l.At, w.Hi) < 0).ToList();

    private static readonly string[] WriteTools = ["Write", "Edit", "MultiEdit", "NotebookEdit"];

    private static IEnumerable<string> WritesIn(IEnumerable<Line> lines) => lines.SelectMany(l => ContentBlocks(l.Json))
        .Where(b => Prop(b, "type") == "tool_use" && WriteTools.Contains(Prop(b, "name")))
        .Select(b => b.TryGetProperty("input", out var i) ? (Prop(i, "file_path") is { Length: > 0 } p ? p : Prop(i, "notebook_path")) : string.Empty)
        .Where(p => p.Length > 0).Select(p => p.Replace('\\', '/')).ToList();

    private static string RootOfPath(string path) => string.Join('/', path.Split('/').Take(3));

    private static Case CaseNamed(string name) => LoadCases().Single(c => c.Name == name);

    [Fact]
    public void ModelSwitchInWindow_SomeWindowHoldsAssistantLinesOfTwoModels()
    {
        var c = CaseNamed("model-switch-in-window");
        var models = WindowsOf(c).Select(w => InWindow(c, w).Where(l => Prop(l.Json, "type") == "assistant").Select(l => Prop(l.Json.GetProperty("message"), "model"))
            .Where(m => m.Length > 0 && m != "<synthetic>").Distinct().Count()).ToList();
        models.Should().Contain(n => n >= 2, "the switch must sit INSIDE one round window, or E4's switch test passes on an empty window");
    }

    [Fact]
    public void WritesOnlyElsewhere_TheWindowHoldsAScratchWriteAndNoWriteIntoTheGatedRepository()
    {
        var c = CaseNamed("writes-only-elsewhere");
        foreach (var w in WindowsOf(c))
        {
            var writes = WritesIn(InWindow(c, w)).ToList();
            writes.Should().Contain(p => RootOfPath(p).EndsWith("/scratch", StringComparison.Ordinal), "the case is about a write the window does see, outside the repository");
            writes.Should().NotContain(p => RootOfPath(p) == RootOfPath(w.Repo), "no write may land in the gated repository");
        }
    }

    [Fact]
    public void TwoRepositories_SomeWindowHoldsWritesIntoBothRepositories()
    {
        var c = CaseNamed("two-repositories-one-session");
        var repos = WindowsOf(c).Select(w => WritesIn(InWindow(c, w)).Select(RootOfPath).Where(r => r.Contains("/repo-", StringComparison.Ordinal)).Distinct().Count()).ToList();
        repos.Should().Contain(n => n >= 2, "evidence in two repositories inside one window is what makes the attribution ambiguous");
    }

    [Fact]
    public void WriteIntoSubmoduleAndScratchpad_TheWindowHoldsBothAndTheSubmoduleIsNestedInTheRepository()
    {
        var c = CaseNamed("write-into-submodule-and-scratchpad");
        var w = WindowsOf(c).Single();
        var writes = WritesIn(InWindow(c, w)).ToList();
        writes.Should().Contain(p => p.StartsWith(w.Repo + "/p1000/", StringComparison.Ordinal), "a write inside the submodule path of the gated repository");
        writes.Should().Contain(p => RootOfPath(p).EndsWith("/scratch", StringComparison.Ordinal), "a write into the scratchpad, seen by the same window");
    }

    private static IEnumerable<(string Id, IReadOnlyList<long> Outputs)> MessagesWithSeveralLines(Case c) =>
        c.TranscriptFiles.SelectMany(f => c.Lines(f).Select(d => d.RootElement)
            .Where(l => Prop(l, "type") == "assistant" && l.TryGetProperty("message", out var m) && m.TryGetProperty("usage", out var u) && u.TryGetProperty("output_tokens", out _))
            .Select(l => (Id: Prop(l.GetProperty("message"), "id"), Out: l.GetProperty("message").GetProperty("usage").GetProperty("output_tokens").GetInt64()))
            .GroupBy(x => x.Id).Where(g => g.Count() > 1).Select(g => (g.Key, (IReadOnlyList<long>)g.Select(x => x.Out).ToList()))).ToList();

    [Fact]
    public void MessageAsSeveralLines_SomeMessageGrowsAndItsLastLineHoldsTheMaximum()
    {
        var messages = MessagesWithSeveralLines(CaseNamed("message-as-several-lines"));
        messages.Any(m => m.Outputs.Distinct().Count() > 1 && m.Outputs[^1] == m.Outputs.Max()).Should().BeTrue("a growing message whose last line holds the maximum");
    }

    [Fact]
    public void MessageLastLineBelowMax_SomeMessagesLastLineIsBelowItsMaximum()
    {
        var messages = MessagesWithSeveralLines(CaseNamed("message-last-line-below-max"));
        messages.Any(m => m.Outputs[^1] < m.Outputs.Max()).Should().BeTrue("a reader that takes the last line would undercount this message");
    }

    // ---- the policy itself: widening the allowlist alone must fail --------------------------------------------------
    [Fact]
    public void TheAllowlistIsExactlyTheApprovedPolicy()
    {
        var text = File.ReadAllText(Path.Combine(Root, "allowlist.json")).Replace("\r\n", "\n");
        var actual = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        actual.Should().Be(ApprovedAllowlistSha256,
            "allowlist.json is the privacy policy of every fixture: a change to it is a deliberate act, reviewed for what it newly admits, and recorded by updating ApprovedAllowlistSha256 in the same diff");
    }

    private static readonly string[] PrivateLookingProbes =
    [
        "private-client-canary", "hello there", "a@b.example", "C:/Users/someone/x.cs", "claude-private-client-canary", "2.1.0+private-client-canary",
        "opus[private]", "msg_PrivateClientCanary00", "toolu_PrivateClientCanary00", "deadbeefPRIVATE", "2026-10-15T09:00:00.000Z", "mcp__private__tool",
    ];

    [Fact]
    public void NoShapeEnumOrVocabularyAdmitsAPrivateLookingString()
    {
        var list = LoadAllowlist();
        var admitted = list.Fields.Where(f => f.Value is not ("int" or "bool" or "fixture-path"))
            .SelectMany(f => PrivateLookingProbes.Where(p => Accepts(list, f.Value, p, [])).Select(p => $"'{f.Key}' ({f.Value}) admits '{p}'")).ToList();
        admitted.Should().BeEmpty("a shape that admits free text would let a fixture carry it, whatever the hash says");
    }
}
