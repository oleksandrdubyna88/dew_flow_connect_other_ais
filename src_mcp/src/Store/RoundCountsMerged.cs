using System.Text.Json.Serialization;
using CoaiMcp.Runners.Reviewers;
using static CoaiMcp.Store.RoundCounts;

namespace CoaiMcp.Store;

/// <summary>What <c>--round-counts-merged</c> answers: the plain counter's windows, over every store it could read, and which stores those were.</summary>
/// <param name="NowUtc">The one instant every window of every store ends at.</param>
/// <param name="ModelsKnownSince">THIS side's instant (D11 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>); null rather than omitted, as the plain answer's is.</param>
/// <param name="Stores">This side first (<c>live</c>), then each snapshot store in the order its first path was given.</param>
/// <param name="StoresNotIncluded">One per path that yielded no store, in argument order — never a zero.</param>
public sealed record RoundCountsMergedAnswer(
    string NowUtc,
    IReadOnlyList<MergedWindow> Windows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ModelsKnownSince,
    GaugeAnswer Gauge,
    IReadOnlyList<MergedStore> Stores,
    IReadOnlyList<StoreNotIncluded> StoresNotIncluded);

/// <summary>One trailing window, <c>(FromUtc, ToUtc]</c>, over every store.</summary>
public sealed record MergedWindow(string Name, string FromUtc, string ToUtc, IReadOnlyList<MergedRow> Rows);

/// <summary>One vendor row in one window, with the keys of the rounds it counted and each store's part of it.</summary>
/// <param name="RoundKeys">The DISTINCT non-empty keys of the rounds counted, across stores.</param>
/// <param name="KeyStore">Key to the id of the ONE store the round is counted in — this side if it holds it, else the lowest store id that does.</param>
/// <param name="RoundsWithoutKey">The rounds counted whose key is empty.</param>
/// <param name="Parts">The same count per store, in <c>stores[]</c> order; a store with no round for this provider here has none. They sum to the row.</param>
public sealed record MergedRow(
    string Provider,
    RoundsByStage Rounds,
    int Reviewers,
    IReadOnlyList<ModelRounds> Models,
    IReadOnlyList<string> RoundKeys,
    IReadOnlyDictionary<string, string> KeyStore,
    int RoundsWithoutKey,
    IReadOnlyList<StorePart> Parts);

/// <summary>One store's part of a row.</summary>
public sealed record StorePart(string Store, RoundsByStage Rounds, int Reviewers);

/// <summary>A store that was counted.</summary>
/// <param name="Live">True for this side, read from its database; false for a snapshot.</param>
/// <param name="Paths">Every snapshot path that reached this store, in the order given — two spellings of one file name both.</param>
/// <param name="AsOf">This side: now. A snapshot: its <c>writtenUtc</c>, never clamped to now.</param>
/// <param name="Quiet">True when the store wrote nothing in the last seven days: it counts as 0 <i>as of</i> its last write, not as unread.</param>
/// <param name="Conflicts">The rounds this store is counted for whose copy in another store has different contents (a different stage, reviewers or models).</param>
public sealed record MergedStore(
    string Id,
    bool Live,
    string Platform,
    string Host,
    string WslDistro,
    string Side,
    string Directory,
    IReadOnlyList<string> Paths,
    string AsOf,
    bool Quiet,
    bool Truncated,
    string CoaiMcpVersion,
    int Conflicts);

/// <summary>A snapshot path that yielded no store, and why: <c>missing</c>, <c>unreadable</c> or <c>newer-schema</c>.</summary>
public sealed record StoreNotIncluded(string Path, string Reason, string Detail);

/// <summary>
/// Two stores (or more), ONE count: this side's <c>coai.db</c> and every snapshot it is handed, as one population of rounds keyed by
/// <c>round_key</c> (E6.S1 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>).
/// </summary>
/// <remarks>
/// <para><b>One identity rule.</b> A round is its key; a round with no key is its store and its id (<see cref="Launch.Of"/>) — never the integer
/// id alone, which every store counts from 1. A key in more than one store is ONE round, counted in the first store that holds it (this side, then
/// the snapshot stores by store id, ordinal) and in no other, so the per-store parts always sum to the row. When the copies differ, that is said
/// (<see cref="MergedStore.Conflicts"/>) and the owner's copy is the one counted — the same set of stores gives the same answer whatever order the
/// paths were given in; only the listing in <c>stores[]</c> follows that order.</para>
/// <para><b>One store, however many names.</b> A snapshot whose id is this side's is this side reached through another spelling (its path is
/// joined, its rounds are not read: this side is read live); two snapshots with one id are one store, and the newer write is the one counted; on equal writes the document at the ordinally
/// smallest path is — a tie-break that, like the ownership above, depends on the inputs alone and never on the order the paths were given in.</para>
/// <para><b>One now.</b> Every store's rounds are cut by the plain counter's own <see cref="RoundCounts.Cut"/> at the caller's instant, never at a
/// snapshot's write.</para>
/// <para><b>A problem is a row, not an exit code.</b> A snapshot that cannot be read is in <c>storesNotIncluded</c> with its reason; a store quiet
/// for a week is counted (0, as of its write). Pure over what the reads return, so its tests need no process.</para>
/// </remarks>
public static class RoundCountsMerged
{
    private static readonly TimeSpan Week = Windows[^1].Length;

    /// <summary>A path and what reading it gave.</summary>
    internal sealed record Named(string Path, SnapshotReading Reading);

    /// <summary>A snapshot store as it is counted: every path that reached it, the document that is counted, and the path that document was read from.</summary>
    private sealed record Entry(IReadOnlyList<string> Paths, RoundCountsSnapshotDocument Counted, string CountedPath);

    private sealed record Folded(IReadOnlyList<string> ThisSidePaths, IReadOnlyList<Entry> Entries, IReadOnlyList<StoreNotIncluded> NotIncluded)
    {
        internal static Folded None { get; } = new([], [], []);
    }

    /// <summary>One store's reviewer rows of the last seven days.</summary>
    private sealed record Side(string StoreId, IReadOnlyList<Launch> Launches);

    /// <summary>Reads this side's database and every path, then merges. A path given twice is read once.</summary>
    /// <exception cref="Microsoft.Data.Sqlite.SqliteException">This side's database exists and will not read — the mode's 74.</exception>
    public static RoundCountsMergedAnswer Read(string dataDir, SnapshotStore thisSide, string version, IReadOnlyList<string> paths, DateTime nowUtc) =>
        Merge(
            LaunchesIn(dataDir, nowUtc, thisSide.Id),
            thisSide,
            version,
            [.. paths.Distinct(StringComparer.Ordinal).Select(path => new Named(path, RoundCountsSnapshot.Read(path)))],
            nowUtc);

    internal static RoundCountsMergedAnswer Merge(LaunchScan live, SnapshotStore thisSide, string version, IReadOnlyList<Named> readings, DateTime nowUtc)
    {
        var folded = readings.Aggregate(Folded.None, (so, named) => Fold(so, named, thisSide.Id));
        var sides = SidesOf(live, thisSide, folded, nowUtc);
        var owners = OwnersOf(sides);
        var counted = CountedBy(sides, owners);

        return new RoundCountsMergedAnswer(
            Stamp(nowUtc),
            [.. Windows.Select(window => WindowOf(window, nowUtc, counted, sides))],
            live.ModelsKnownSince,
            RoundsPerPercent.NotReadHere,
            StoresOf(thisSide, version, folded, nowUtc, ConflictsOf(sides, owners)),
            folded.NotIncluded);
    }

    // ---- which stores: identity folding -------------------------------------------------------------------------------------------

    private static Folded Fold(Folded so, Named named, string thisId) =>
        named.Reading switch
        {
            SnapshotReading.Refused refused => so with { NotIncluded = [.. so.NotIncluded, new StoreNotIncluded(named.Path, refused.Refusal.Reason, refused.Refusal.Detail)] },
            SnapshotReading.Loaded loaded when loaded.Document.Store.Id == thisId => so with { ThisSidePaths = [.. so.ThisSidePaths, named.Path] },
            SnapshotReading.Loaded loaded => so with { Entries = Joined(so.Entries, named.Path, loaded.Document) },
            _ => throw new InvalidOperationException("the union is closed"),
        };

    private static IReadOnlyList<Entry> Joined(IReadOnlyList<Entry> entries, string path, RoundCountsSnapshotDocument document)
    {
        var id = document.Store.Id;

        return entries.Any(entry => entry.Counted.Store.Id == id)
            ? [.. entries.Select(entry => entry.Counted.Store.Id == id ? Widened(entry, path, document) : entry)]
            : [.. entries, new Entry([path], document, path)];
    }

    /// <summary>
    /// The same store reached through one more path: the path is named, and the NEWER write is the one counted. A tie on <c>writtenUtc</c> (two spellings
    /// of one file normally tie, with identical contents) goes to the document at the ordinally smallest path, so the counted document is a function of
    /// the documents and their paths alone and never of the order the paths were given in.
    /// </summary>
    private static Entry Widened(Entry entry, string path, RoundCountsSnapshotDocument document) =>
        Supersedes(document, path, entry)
            ? new Entry([.. entry.Paths, path], document, path)
            : entry with { Paths = [.. entry.Paths, path] };

    private static bool Supersedes(RoundCountsSnapshotDocument document, string path, Entry entry) =>
        string.CompareOrdinal(document.WrittenUtc, entry.Counted.WrittenUtc) switch
        {
            > 0 => true,
            0 => string.CompareOrdinal(path, entry.CountedPath) < 0,
            _ => false,
        };

    // ---- the population: one key rule ---------------------------------------------------------------------------------------------

    private static IReadOnlyList<Side> SidesOf(LaunchScan live, SnapshotStore thisSide, Folded folded, DateTime nowUtc) =>
        [
            new Side(thisSide.Id, InTheWeek(live.Launches, nowUtc)),
            .. folded.Entries.Select(entry => new Side(entry.Counted.Store.Id, InTheWeek(LaunchesOf(entry.Counted), nowUtc))),
        ];

    private static IEnumerable<Launch> LaunchesOf(RoundCountsSnapshotDocument document) =>
        document.Rounds.SelectMany(round => round.Reviewers.Select(reviewer =>
            Launch.Of(document.Store.Id, round.Id, round.Key, round.StartedUtc, round.Stage, reviewer.Provider, reviewer.Model)));

    /// <summary>A snapshot holds rounds a week before ITS write; at the caller's now some are older still, and some are ahead of it.</summary>
    private static IReadOnlyList<Launch> InTheWeek(IEnumerable<Launch> launches, DateTime nowUtc) =>
        [.. launches.Where(launch => Inside(launch.StartedUtc, Stamp(nowUtc - Week), Stamp(nowUtc)))];

    /// <summary>Key to the first store that holds it: this side, then the snapshot stores by id — never by the order the paths were given in.</summary>
    private static Dictionary<string, string> OwnersOf(IReadOnlyList<Side> sides)
    {
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var launch in OwnershipOrder(sides).SelectMany(side => side.Launches).Where(launch => launch.RoundKey.Length > 0))
        {
            owners.TryAdd(launch.RoundKey, launch.StoreId);
        }

        return owners;
    }

    /// <summary>This side is always first in <paramref name="sides"/>; the rest are put in id order, so one set of stores has one owner per key.</summary>
    private static IEnumerable<Side> OwnershipOrder(IReadOnlyList<Side> sides) =>
        sides.Take(1).Concat(sides.Skip(1).OrderBy(side => side.StoreId, StringComparer.Ordinal));

    /// <summary>Every store's rounds, minus the copies another store owns. A keyless round is its store's own.</summary>
    private static IReadOnlyList<Launch> CountedBy(IReadOnlyList<Side> sides, IReadOnlyDictionary<string, string> owners) =>
        [.. sides.SelectMany(side => side.Launches).Where(launch => launch.RoundKey.Length == 0 || owners[launch.RoundKey] == launch.StoreId)];

    private static Dictionary<string, int> ConflictsOf(IReadOnlyList<Side> sides, IReadOnlyDictionary<string, string> owners) =>
        sides.SelectMany(side => side.Launches)
            .Where(launch => launch.RoundKey.Length > 0)
            .GroupBy(launch => launch.RoundKey, StringComparer.Ordinal)
            .Where(Differs)
            .GroupBy(copies => owners[copies.Key], StringComparer.Ordinal)
            .ToDictionary(owned => owned.Key, owned => owned.Count(), StringComparer.Ordinal);

    private static bool Differs(IGrouping<string, Launch> copies) =>
        copies.GroupBy(launch => launch.StoreId, StringComparer.Ordinal).Select(SignatureOf).Distinct(StringComparer.Ordinal).Skip(1).Any();

    /// <summary>What a copy of a round says it was: its stage and its reviewers with their models.</summary>
    private static string SignatureOf(IEnumerable<Launch> copy) =>
        copy.First().Stage + "|" + string.Join(';', copy.Select(launch => $"{launch.Provider}/{launch.Model}").Order(StringComparer.Ordinal));

    // ---- the answer ---------------------------------------------------------------------------------------------------------------

    private static MergedWindow WindowOf((string Name, TimeSpan Length) window, DateTime nowUtc, IReadOnlyList<Launch> counted, IReadOnlyList<Side> sides)
    {
        var plain = Cut(window, nowUtc, counted);

        return new MergedWindow(plain.Name, plain.FromUtc, plain.ToUtc, [.. plain.Rows.Select(row => RowOf(row, plain, counted, sides))]);
    }

    private static MergedRow RowOf(RoundCountsRow row, RoundCountsWindow window, IReadOnlyList<Launch> counted, IReadOnlyList<Side> sides)
    {
        var inside = counted.Where(launch => launch.Provider == row.Provider && Inside(launch.StartedUtc, window.FromUtc, window.ToUtc)).ToList();

        return new MergedRow(
            row.Provider, row.Rounds, row.Reviewers, row.Models, row.RoundKeys, KeyStoreOf(row.RoundKeys, inside), row.RoundsWithoutKey, PartsOf(inside, sides));
    }

    private static Dictionary<string, string> KeyStoreOf(IReadOnlyList<string> keys, IEnumerable<Launch> inside)
    {
        var owners = inside.Where(launch => launch.RoundKey.Length > 0).GroupBy(launch => launch.RoundKey, StringComparer.Ordinal)
            .ToDictionary(round => round.Key, round => round.First().StoreId, StringComparer.Ordinal);

        return keys.ToDictionary(key => key, key => owners[key], StringComparer.Ordinal);
    }

    private static IReadOnlyList<StorePart> PartsOf(List<Launch> inside, IReadOnlyList<Side> sides) =>
        [.. sides.Select(side => (side.StoreId, Mine: inside.Where(launch => launch.StoreId == side.StoreId).ToList()))
            .Where(part => part.Mine.Count > 0)
            .Select(part => new StorePart(part.StoreId, StagesOf(part.Mine), part.Mine.Count))];

    private static IReadOnlyList<MergedStore> StoresOf(SnapshotStore thisSide, string version, Folded folded, DateTime nowUtc, IReadOnlyDictionary<string, int> conflicts) =>
        [
            new MergedStore(
                thisSide.Id, true, thisSide.Platform, thisSide.Host, thisSide.WslDistro, thisSide.Side, thisSide.Directory,
                folded.ThisSidePaths, Stamp(nowUtc), false, false, version, conflicts.GetValueOrDefault(thisSide.Id)),
            .. folded.Entries.Select(entry => StoreOf(entry, nowUtc, conflicts)),
        ];

    private static MergedStore StoreOf(Entry entry, DateTime nowUtc, IReadOnlyDictionary<string, int> conflicts)
    {
        var document = entry.Counted;
        var store = document.Store;

        return new MergedStore(
            store.Id, false, store.Platform, store.Host, store.WslDistro, store.Side, store.Directory, entry.Paths,
            document.WrittenUtc, IsQuiet(document, nowUtc), document.Truncated, document.CoaiMcpVersion, conflicts.GetValueOrDefault(store.Id));
    }

    /// <summary>Written no later than the start of the longest window: none of its rounds can be inside any window.</summary>
    private static bool IsQuiet(RoundCountsSnapshotDocument document, DateTime nowUtc) =>
        string.CompareOrdinal(document.WrittenUtc, Stamp(nowUtc - Week)) <= 0;
}
