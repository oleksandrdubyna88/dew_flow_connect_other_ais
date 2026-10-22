using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Server;

namespace CoaiMcp.Store;

/// <summary>One reviewer row of a snapshot round: the provider as stored (a Team-server row is <c>&lt;server&gt;-&lt;vendor&gt;</c>) and the model, or empty.</summary>
public sealed record SnapshotReviewer(string Provider, string Model);

/// <summary>One round of a snapshot — what the counter needs to count it and to tell it from every other, and nothing else.</summary>
/// <param name="Id">The <c>rounds.id</c> AUTOINCREMENT id: meaningful only together with the store's id.</param>
/// <param name="Key"><c>rounds.round_key</c> (32 lower-case hex), or empty for a round written before step 21 or a skipped one.</param>
/// <param name="StartedUtc"><c>rounds.started_utc</c>, what the windows are cut on.</param>
/// <param name="Stage">The stage enum's own name (<c>PlanReview</c> …).</param>
/// <param name="Reviewers">One per <c>reviewers</c> row of the round.</param>
public sealed record SnapshotRound(long Id, string Key, string StartedUtc, string Stage, IReadOnlyList<SnapshotReviewer> Reviewers);

/// <summary>Who wrote a snapshot: the store id its OWNER computed from its own host and directory, and the facts it was computed from.</summary>
/// <param name="Id">Lower-case hex, 32 digits: <see cref="RoundAuthorsMerge.SourceIdOf(string, string, string)"/> over the facts below.</param>
/// <param name="Platform"><c>windows</c> | <c>linux</c> | <c>macos</c> | <c>other</c>.</param>
/// <param name="Host">The host's word — <c>wsl</c>, <c>windows</c>, <c>linux</c> …; tells WSL from a native Linux box.</param>
/// <param name="WslDistro"><c>WSL_DISTRO_NAME</c> when the host is WSL, else empty.</param>
/// <param name="Side">The data-side partition's name, usually empty.</param>
/// <param name="Directory">The owner's own absolute spelling of its data directory.</param>
public sealed record SnapshotStore(string Id, string Platform, string Host, string WslDistro, string Side, string Directory);

/// <summary>The document <c>round-counts-snapshot.json</c> holds. See <see cref="RoundCountsSnapshot"/>.</summary>
/// <param name="Schema">The handshake: bumped only on a change a reader of the previous schema would misread; an added field keeps it.</param>
/// <param name="WrittenUtc">The writer's clock at the write — the reader's <i>as of</i>. Never clamped.</param>
/// <param name="CoaiMcpVersion">The writing program's version, for the cure sentence of a newer schema.</param>
/// <param name="FromUtc"><c>WrittenUtc</c> minus seven days: the oldest round the file can hold.</param>
/// <param name="Truncated">True when the cap dropped rounds.</param>
/// <param name="Rounds">Newest first.</param>
public sealed record RoundCountsSnapshotDocument(
    int Schema,
    string WrittenUtc,
    string CoaiMcpVersion,
    SnapshotStore Store,
    string FromUtc,
    bool Truncated,
    IReadOnlyList<SnapshotRound> Rounds);

/// <summary>
/// Its OWN source-generated context rather than a line in <c>ServerJsonContext</c>, the shape <c>RoundAuthorsJson</c> has: a document read from
/// the other side's file is strict about what it needs (a missing required property and a null are refusals) and lenient about what it does
/// not know (an unknown property is ignored — that is how an added field keeps schema 1).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectRequiredConstructorParameters = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(RoundCountsSnapshotDocument))]
internal sealed partial class RoundCountsSnapshotJson : JsonSerializerContext;

/// <summary>Why a snapshot path yielded no store: <c>missing</c>, <c>unreadable</c> or <c>newer-schema</c>, and the sentence that says it.</summary>
public sealed record SnapshotRefusal(string Reason, string Detail);

/// <summary>
/// A snapshot read from a path: <see cref="Loaded"/> with its document, or <see cref="Refused"/> with the refusal — a closed hierarchy (the C# doctrine
/// §5, for a failure with several meaningful cases), so a refusal cannot carry a placeholder document and a document cannot carry a refusal.
/// </summary>
public abstract record SnapshotReading
{
    private SnapshotReading()
    {
    }

    /// <summary>The snapshot was read; <paramref name="Document"/> has every stamp spelt the column's way.</summary>
    public sealed record Loaded(RoundCountsSnapshotDocument Document) : SnapshotReading;

    /// <summary>The path yielded no store; <paramref name="Refusal"/> says why.</summary>
    public sealed record Refused(SnapshotRefusal Refusal) : SnapshotReading;

    public static SnapshotReading Of(RoundCountsSnapshotDocument document) => new Loaded(document);

    public static SnapshotReading Refuse(string reason, string detail) => new Refused(new SnapshotRefusal(reason, detail));
}

/// <summary>
/// The last seven days of a store's rounds as a plain JSON file beside <c>coai.db</c>, written by the OWNING side after every round it records
/// (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>) — what the OTHER side's counter reads instead of opening a database across the
/// <c>\\wsl.localhost</c> / drvfs boundary, where SQLite's WAL locks are not coordinated.
/// </summary>
/// <remarks>
/// <para><b>A counting record, in the clear.</b> Ids, keys, stamps, stage names, provider and model names: no plan text, no finding, no
/// caller, no session id. It crosses the boundary unprotected, so it holds only what a count needs.</para>
/// <para><b>Best effort, and never a reason for a round to fail.</b> <see cref="TryWrite(string, DateTime, Serilog.ILogger)"/> catches everything and
/// logs ONE warning naming the exception's TYPE only — a filesystem message carries the destination path, and with it a person's name. A snapshot
/// older than the latest round is a STALE store, visible through <c>writtenUtc</c>, and the next round's write cures it.</para>
/// <para><b>Written whole.</b> Through <see cref="AtomicFile"/>: a reader sees the old file or the new one, never a torn one.</para>
/// <para><b>Bounded</b> by time (seven days, the counter's longest window) and by count (<see cref="MaxRounds"/>, newest kept, <c>truncated</c> said).
/// A round with no reviewer row is not in it: the counter counts a round for a provider through its reviewer rows, so such a round is in no count.</para>
/// <para><b>Read leniently, and strictly.</b> <c>schema</c> is read first, as a bare property, so a newer document is named as newer rather than as
/// unreadable; an unknown property is ignored, a missing required one is a refusal, and every value that is echoed into an answer or keys a table
/// (the store id, a round key, each stamp) is checked against the shape this program writes.</para>
/// </remarks>
public static class RoundCountsSnapshot
{
    public const string FileName = "round-counts-snapshot.json";

    /// <summary>The most rounds a snapshot holds; past it the oldest are dropped and <c>truncated</c> is true.</summary>
    public const int MaxRounds = 5_000;

    /// <summary>The schema this program writes and the newest it reads.</summary>
    public const int Schema = 1;

    /// <summary>The reasons a snapshot path yields no store — the words the extension reads.</summary>
    public const string Missing = "missing";

    public const string Unreadable = "unreadable";

    public const string NewerSchema = "newer-schema";

    private const string NotASnapshot = "not a round-counts snapshot";

    /// <summary>Far past the ~100 KB a week holds and the ~1.5 MB the cap allows: a file larger than this is not one of ours.</summary>
    private const long MaxBytes = 16L * 1024 * 1024;

    private static readonly TimeSpan Week = RoundCounts.Windows[^1].Length;

    public static string PathIn(string dataDir) => Path.Combine(dataDir, FileName);

    /// <summary>
    /// Who this side is, computed by its OWNER from its own spelling (D1): the platform, the WSL distribution when it is one, and the canonical
    /// directory — never the directory alone, because two distributions share <c>/home/&lt;user&gt;/.local/share/coai-mcp</c>.
    /// </summary>
    public static SnapshotStore StoreOf(string dataDir, Func<string, string?> env)
    {
        var host = HostKinds.Current;
        var distro = host == HostKind.Wsl ? env("WSL_DISTRO_NAME") ?? string.Empty : string.Empty;
        var platform = RoundAuthorsMerge.PlatformFamily();

        return new SnapshotStore(
            RoundAuthorsMerge.SourceIdOf(platform, distro, dataDir), platform, HostKinds.Word(host), distro, PanelSettings.DataSide(env), Path.GetFullPath(dataDir));
    }

    /// <summary>Writes this store's snapshot after the round it just recorded; says so in one log line when it cannot, and never throws.</summary>
    public static void TryWrite(string dataDir, DateTime nowUtc, Serilog.ILogger log) =>
        TryWrite(dataDir, nowUtc, log, static (path, text) => AtomicFile.Write(path, text, AtomicFile.SharingRetry));

    /// <param name="write">The write itself — <see cref="AtomicFile"/> in production; a seam so a test can fail it half way.</param>
    internal static void TryWrite(string dataDir, DateTime nowUtc, Serilog.ILogger log, Action<string, string> write)
    {
        // A store whose database is not there (the projection could not open it) has nothing to leave behind.
        if (!File.Exists(Path.Combine(dataDir, RoundsDb.FileName)))
        {
            return;
        }

        try
        {
            write(PathIn(dataDir), Serialize(DocumentOf(dataDir, nowUtc, StoreOf(dataDir, Environment.GetEnvironmentVariable))));
        }
        catch (Exception e)
        {
            // The TYPE only: an I/O message names the destination, and the destination names the person.
            log.Warning("the round-counts snapshot could not be written: {Why}", e.GetType().Name);
        }
    }

    /// <summary>The document of a store as of <paramref name="nowUtc"/>: the same scan the counter makes, grouped by round, newest first, capped.</summary>
    internal static RoundCountsSnapshotDocument DocumentOf(string dataDir, DateTime nowUtc, SnapshotStore store)
    {
        var rounds = RoundsOf(RoundCounts.LaunchesIn(dataDir, nowUtc, store.Id).Launches);

        return new RoundCountsSnapshotDocument(
            Schema, RoundCounts.Stamp(nowUtc), Program.VersionText, store, RoundCounts.Stamp(nowUtc - Week), rounds.Count > MaxRounds, [.. rounds.Take(MaxRounds)]);
    }

    private static List<SnapshotRound> RoundsOf(IEnumerable<RoundCounts.Launch> launches) =>
        [.. launches
            .GroupBy(launch => launch.RoundId)
            .Select(RoundOf)
            .OrderByDescending(round => round.StartedUtc, StringComparer.Ordinal)
            .ThenByDescending(round => round.Id)];

    private static SnapshotRound RoundOf(IGrouping<long, RoundCounts.Launch> reviewers) =>
        new(reviewers.Key, reviewers.First().RoundKey, reviewers.First().StartedUtc, reviewers.First().Stage, ReviewersOf(reviewers));

    /// <summary>Sorted, so the same rounds always make the same bytes.</summary>
    private static IReadOnlyList<SnapshotReviewer> ReviewersOf(IEnumerable<RoundCounts.Launch> launches) =>
        [.. launches
            .Select(launch => new SnapshotReviewer(launch.Provider, launch.Model))
            .OrderBy(reviewer => reviewer.Provider, StringComparer.Ordinal)
            .ThenBy(reviewer => reviewer.Model, StringComparer.Ordinal)];

    public static string Serialize(RoundCountsSnapshotDocument document) =>
        JsonSerializer.Serialize(document, RoundCountsSnapshotJson.Default.RoundCountsSnapshotDocument);

    /// <summary>
    /// A snapshot read from <paramref name="path"/>: the document (with every stamp spelt the column's way), or the reason it is not a store —
    /// <see cref="Missing"/>, <see cref="Unreadable"/> or <see cref="NewerSchema"/>. Never throws for a file problem.
    /// </summary>
    public static SnapshotReading Read(string path) => Read(path, ReadShared);

    /// <summary>
    /// The file opened the way every read of it is: for reading, sharing read, write and delete, so a reader never forbids a writer to open it.
    /// <see cref="File.ReadAllText(string)"/> opens with <see cref="FileShare.Read"/>, which does forbid that for as long as the reader holds
    /// the file. (A move-over, the owner's <see cref="AtomicFile"/> replace, is refused by Windows whatever the holder shared; that one is
    /// covered by <see cref="AtomicFile.SharingRetry"/> and by a read being milliseconds.) Internal so a test can hold the file exactly as a reader does.
    /// </summary>
    internal static FileStream OpenForRead(string path) => SharedRead.Open(path);

    private static string ReadShared(string path)
    {
        using var stream = OpenForRead(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

        return reader.ReadToEnd();
    }

    /// <param name="readText">The read itself — <see cref="ReadShared"/> in production; a seam so a test can fail it.</param>
    internal static SnapshotReading Read(string path, Func<string, string> readText) =>
        File.Exists(path) ? ReadText(path, readText) : SnapshotReading.Refuse(Missing, path);

    private static SnapshotReading ReadText(string path, Func<string, string> readText)
    {
        try
        {
            return new FileInfo(path).Length > MaxBytes ? SnapshotReading.Refuse(Unreadable, NotASnapshot) : Parsed(readText(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The TYPE only, and a sentence of ours: a filesystem message names the file it was resolved to, and the path names the person.
            // The `missing` refusal says the path the CALLER passed, which is theirs already.
            return SnapshotReading.Refuse(Unreadable, $"the file could not be read ({e.GetType().Name})");
        }
    }

    /// <summary>The schema is read FIRST and leniently, so that a document a newer program wrote is named as newer, not as unreadable.</summary>
    private static SnapshotReading Parsed(string text) =>
        SchemaOf(text) switch
        {
            { Known: false } or { Number: < 1 } => SnapshotReading.Refuse(Unreadable, NotASnapshot),
            { Number: > Schema } newer => SnapshotReading.Refuse(NewerSchema, NewerSentence(newer)),
            _ => Document(text),
        };

    private static string NewerSentence(SchemaFact newer) =>
        $"schema {newer.Number} — this coai-mcp reads schema {Schema}; update coai-mcp on this side"
        + (newer.Version.Length > 0 ? $" (the file was written by coai-mcp {newer.Version})" : string.Empty);

    private readonly record struct SchemaFact(bool Known, int Number, string Version);

    private static SchemaFact SchemaOf(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);

            return FactOf(document.RootElement);
        }
        catch (JsonException)
        {
            return new SchemaFact(false, 0, string.Empty);
        }
    }

    private static SchemaFact FactOf(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && IntegerOf(root, "schema") is { } number
            ? new SchemaFact(true, number, TextOf(root, "coaiMcpVersion"))
            : new SchemaFact(false, 0, string.Empty);

    private static int? IntegerOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static string TextOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static SnapshotReading Document(string text)
    {
        try
        {
            return Checked(JsonSerializer.Deserialize(text, RoundCountsSnapshotJson.Default.RoundCountsSnapshotDocument));
        }
        catch (JsonException)
        {
            return SnapshotReading.Refuse(Unreadable, NotASnapshot);
        }
    }

    private static SnapshotReading Checked(RoundCountsSnapshotDocument? parsed) =>
        parsed is null || Misshapen(parsed) is { Length: > 0 }
            ? SnapshotReading.Refuse(Unreadable, NotASnapshot)
            : SnapshotReading.Of(Normalised(parsed));

    /// <summary>
    /// The shapes this program writes that the JSON types alone cannot say: an id of 32 lower-case hex digits (it is echoed into the answer and keys a
    /// dictionary), a round key that is empty or 32 of them, every stamp an ISO-8601 instant, and no more rounds than the cap. Empty when well shaped.
    /// </summary>
    internal static string Misshapen(RoundCountsSnapshotDocument document) =>
        ShapeChecks.Select(check => check(document)).FirstOrDefault(why => why.Length > 0) ?? string.Empty;

    /// <summary>NullFault is first: the later checks dereference every round, and a check that crashes is not a refusal.</summary>
    private static readonly Func<RoundCountsSnapshotDocument, string>[] ShapeChecks = [NullFault, IdFault, KeyFault, StampFault, SizeFault];

    /// <summary>
    /// The nullable annotations reject a null property but let a null ELEMENT of a list through, so a <c>null</c> round, a null reviewers array
    /// or a null reviewer is checked here, at the boundary, before anything dereferences it.
    /// </summary>
    private static string NullFault(RoundCountsSnapshotDocument document) =>
        document.Rounds.All(HasNoNull) ? string.Empty : "a round or a reviewer is null";

    private static bool HasNoNull(SnapshotRound? round) =>
        round?.Reviewers is { } reviewers && reviewers.All(reviewer => reviewer is not null);

    private static string IdFault(RoundCountsSnapshotDocument document) =>
        IsStoreId(document.Store.Id) ? string.Empty : "the store id is not 32 lower-case hex digits";

    private static bool IsStoreId(string id) => id.Length == Server.RoundKeys.Length && id.All(char.IsAsciiHexDigitLower);

    private static string KeyFault(RoundCountsSnapshotDocument document) =>
        document.Rounds.All(round => round.Key.Length == 0 || Server.RoundKeys.IsWellFormed(round.Key)) ? string.Empty : "a round key is not 32 lower-case hex digits";

    private static string StampFault(RoundCountsSnapshotDocument document) =>
        Stamps(document).All(stamp => Program.TryParseInstant(stamp, out _)) ? string.Empty : "a stamp is not an ISO-8601 instant";

    private static IEnumerable<string> Stamps(RoundCountsSnapshotDocument document) =>
        [document.WrittenUtc, document.FromUtc, .. document.Rounds.Select(round => round.StartedUtc)];

    private static string SizeFault(RoundCountsSnapshotDocument document) =>
        document.Rounds.Count <= MaxRounds ? string.Empty : "more rounds than a snapshot holds";

    /// <summary>Every stamp in the column's own spelling, because the windows compare them as text.</summary>
    private static RoundCountsSnapshotDocument Normalised(RoundCountsSnapshotDocument document) =>
        document with
        {
            WrittenUtc = Spelt(document.WrittenUtc),
            FromUtc = Spelt(document.FromUtc),
            Rounds = [.. document.Rounds.Select(round => round with { StartedUtc = Spelt(round.StartedUtc) })],
        };

    private static string Spelt(string stamp) =>
        Program.TryParseInstant(stamp, out var at) ? RoundCounts.Stamp(at.UtcDateTime) : stamp;
}
