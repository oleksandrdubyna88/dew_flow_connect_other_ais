using System.Globalization;
using System.Text.Json.Serialization;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

/// <summary>What <c>--round-counts</c> answers: every window, from one read of the rounds database.</summary>
/// <param name="NowUtc">The instant every window ends at — the clock, or the <c>--now</c> a test fixed.</param>
/// <param name="ModelsKnownSince">
/// When the first round that recorded a reviewer model started, or null when none has: before it, a
/// per-model count cannot exist, and the client labels the model lines from that instant on.
/// Written as <c>null</c> rather than omitted — the contract says null, and an absent key is a different
/// thing to a reader than a present one.
/// </param>
/// <param name="Gauge">
/// Rounds per 1 % of each vendor gauge window, from the ONE derivation both halves use (story 6.2 of
/// research/PLAN_every_round_is_counted.md). On this machine it is always <see cref="RoundsPerPercent.NotReadHere"/>: local
/// capture (story 6.1b) is deferred, so no local line carries a reading — the extension shows a Team server's observation
/// only for a row that runs on that server, and says <i>not read on this machine</i> for any other. Trailing, so an older
/// extension reads the answer unchanged.
/// </param>
public sealed record RoundCountsAnswer(
    string NowUtc,
    IReadOnlyList<RoundCountsWindow> Windows,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ModelsKnownSince,
    GaugeAnswer Gauge = null!)
{
    /// <summary>Never null, whatever a deserialiser did with the property.</summary>
    public GaugeAnswer Gauge { get; init; } = Gauge ?? RoundsPerPercent.NotReadHere;
}

/// <summary>One trailing window, <c>(FromUtc, ToUtc]</c>, and the vendor rows that ran a round inside it.</summary>
public sealed record RoundCountsWindow(string Name, string FromUtc, string ToUtc, IReadOnlyList<RoundCountsRow> Rows);

/// <summary>One vendor row's rounds in one window.</summary>
/// <param name="Provider">The reviewer's provider exactly as stored — a Team-server row (<c>&lt;server&gt;-&lt;vendor&gt;</c>) is its own string.</param>
/// <param name="Rounds">DISTINCT rounds, by stage, that ran at least one reviewer of this provider.</param>
/// <param name="Reviewers">The reviewer rows of this provider in those rounds — the honest numerator of "reviewers per round".</param>
/// <param name="Models">Distinct rounds per model, over the reviewer rows that recorded one; a row written before step 20 adds to none.</param>
public sealed record RoundCountsRow(
    string Provider, RoundsByStage Rounds, int Reviewers, IReadOnlyList<ModelRounds> Models, IReadOnlyList<string> RoundKeys = null!, [property: JsonPropertyOrder(2)] int RoundsWithoutKey = 0)
{
    /// <summary>Never null, whatever a deserialiser did with the property. Ordered after every other property so the two new fields trail the answer's older ones.</summary>
    [JsonPropertyOrder(1)]
    public IReadOnlyList<string> RoundKeys { get; init; } = RoundKeys ?? [];
}

/// <summary>Distinct rounds per stage, under the names the client draws.</summary>
public sealed record RoundsByStage(int Plan, int Code, int Document, int Feature);

/// <summary>Distinct rounds in which this model was a reviewer's.</summary>
public sealed record ModelRounds(string Model, int Rounds);

/// <summary>
/// The local round counter: distinct rounds per vendor row and stage over three trailing windows, read
/// from <c>coai.db</c> and nothing else (story 2.1 of <c>research/PLAN_every_round_is_counted.md</c>).
/// </summary>
/// <remarks>
/// <para><b>What a round is here.</b> One <c>rounds</c> row, counted for a provider when at least one of
/// its <c>reviewers</c> rows names that provider — whatever the reviewer's status, because a launch that
/// failed or stood down still belongs to a round that ran. Three codex reviewers of one code round are
/// ONE round and three reviewers; that distinction is the whole reason the Spending tab's "N runs" could
/// not answer the operator's question (RESULTS_what_spends_the_subscription_2026-10-09.md).</para>
/// <para><b>Read-only, unpooled, one scan.</b> The same open as <see cref="RoundsQuery.Read"/>: a reader
/// never migrates — the extension refreshes this every minute and a one-shot that wrote would be a
/// writer racing the server — so a database from before step 20 is asked whether it HAS the model column
/// and counts its rounds without models when it does not. The longest window bounds the one query
/// (<c>rounds_by_time</c>, then <c>reviewers_by_round</c>), and the shorter windows are cut from its
/// rows in memory: a seven-day slice is a few hundred rounds, not the year of history the code round
/// refused to re-scan.</para>
/// <para><b>Windows are <c>(from, to]</c></b>, trailing to <c>now</c>, compared as text over the
/// column's own ISO-8601 UTC stamps — the way <c>--since</c> compares, and in ONE function, so the SQL's
/// outer bound and the in-memory cut cannot disagree about an edge. A round exactly five hours old is
/// in the last day and not in the last five hours.</para>
/// </remarks>
public static class RoundCounts
{
    /// <summary>The trailing windows, shortest first; the longest bounds the one scan.</summary>
    internal static readonly (string Name, TimeSpan Length)[] Windows =
    [
        ("5h", TimeSpan.FromHours(5)),
        ("24h", TimeSpan.FromHours(24)),
        ("7d", TimeSpan.FromDays(7)),
    ];

    /// <summary>One reviewer row of one round inside the longest window — what the scan returns and the windows are cut from.</summary>
    /// <param name="StoreId">The store the round was recorded in: a round's integer id means something only together with it.</param>
    /// <param name="Key">What a DISTINCT round is counted by: the round's own key when it has one, else <c>&lt;store&gt;#&lt;id&gt;</c> — never the bare integer id, which two stores share.</param>
    /// <param name="RoundKey">The <c>round_key</c> as recorded; empty for a round written before step 21 and for a skipped one.</param>
    internal sealed record Launch(string StoreId, string Key, string RoundKey, long RoundId, string StartedUtc, string Stage, string Provider, string Model)
    {
        /// <summary>The only way to make one: the identity rule (D4 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>) lives in this line.</summary>
        internal static Launch Of(string storeId, long roundId, string roundKey, string startedUtc, string stage, string provider, string model) =>
            new(storeId, roundKey.Length > 0 ? roundKey : $"{storeId}#{roundId}", roundKey, roundId, startedUtc, stage, provider, model);
    }

    /// <summary>What one read of a store's rounds database found: the reviewer rows of the last seven days, and when models began to be recorded.</summary>
    internal sealed record LaunchScan(IReadOnlyList<Launch> Launches, string? ModelsKnownSince);

    /// <summary>
    /// The read-only open of an existing <c>coai.db</c> that the readers of the counter and the round-authors report share: <c>Mode=ReadOnly</c> (a
    /// reader never migrates, and never creates a file), unpooled, and a five-second busy wait. Opened; the caller disposes it.
    /// </summary>
    internal static SqliteConnection OpenReadOnly(string file)
    {
        var db = new SqliteConnection($"Data Source={file};Pooling=False;Mode=ReadOnly;Default Timeout=5");
        try
        {
            db.Open();

            return db;
        }
        catch
        {
            db.Dispose();

            throw;
        }
    }

    /// <summary>Every window's counts as of <paramref name="nowUtc"/>; an empty answer when no database exists.</summary>
    /// <remarks>
    /// A missing file is not a failure: a machine that has never run a round has nought rounds, and the
    /// extension tells that from "unavailable" by the exit code and the parse. The file is never created
    /// here — <c>Mode=ReadOnly</c> refuses to, and the existence check keeps that refusal from reading
    /// as a disk that would not answer.
    /// </remarks>
    public static RoundCountsAnswer Read(string dataDir, DateTime nowUtc)
    {
        var scan = LaunchesIn(dataDir, nowUtc, RoundCountsSnapshot.StoreOf(dataDir, Environment.GetEnvironmentVariable).Id);

        return new RoundCountsAnswer(
            Stamp(nowUtc),
            [.. Windows.Select(window => Cut(window, nowUtc, scan.Launches))],
            scan.ModelsKnownSince);
    }

    /// <summary>
    /// The one read of a store's rounds that the plain counter, the merged counter and the snapshot all make, so that none of them can disagree
    /// about which rounds are "the last seven days" (D3 of <c>todo/PLAN_the_round_counter_tail_E6.md</c>). A store with no database has no rounds.
    /// </summary>
    /// <param name="storeId">The id the rounds are attributed to — it names the rounds that have no key of their own.</param>
    internal static LaunchScan LaunchesIn(string dataDir, DateTime nowUtc, string storeId)
    {
        var file = Path.Combine(dataDir, RoundsDb.FileName);
        if (!File.Exists(file))
        {
            return new LaunchScan([], null);
        }

        using var db = OpenReadOnly(file);
        var modelsRecorded = RoundsQuery.HasColumn(db, "reviewers", "model");
        var keysRecorded = RoundsQuery.HasColumn(db, "rounds", "round_key");
        var launches = Launches(db, storeId, Stamp(nowUtc - Windows[^1].Length), Stamp(nowUtc), modelsRecorded, keysRecorded);

        return new LaunchScan(launches, modelsRecorded ? ModelsKnownSince(db) : null);
    }

    private static List<Launch> Launches(SqliteConnection db, string storeId, string from, string to, bool modelsRecorded, bool keysRecorded)
    {
        using var read = db.CreateCommand();
        // The model and the key are SELECTED only when the file has the column: a database last written by a build
        // without step 20 (or 21) would otherwise fail the whole count on `no such column`, which is the page,
        // not a line of it. Every spelling is a literal written here; nothing a caller sends reaches them.
        read.CommandText = SqlLaunches(modelsRecorded ? "v.model" : "''", keysRecorded ? "r.round_key" : "''");
        read.Parameters.AddWithValue("$from", from);
        read.Parameters.AddWithValue("$to", to);
        using var rows = read.ExecuteReader();
        var launches = new List<Launch>();
        while (rows.Read())
        {
            launches.Add(Launch.Of(
                storeId, Columns.Id(rows, "id"), Columns.Text(rows, "round_key"), Columns.Text(rows, "started_utc"),
                Columns.Text(rows, "stage"), Columns.Text(rows, "provider"), Columns.Text(rows, "model")));
        }

        return launches;
    }

    /// <summary>The one scan: every reviewer row of every round that started inside <c>($from, $to]</c>.</summary>
    private static string SqlLaunches(string model, string key) => $"""
        SELECT r.id, r.started_utc, r.stage, v.provider, {model} AS model, {key} AS round_key
          FROM rounds r JOIN reviewers v ON v.round_id = r.id
         WHERE r.started_utc > $from AND r.started_utc <= $to
        """;

    internal static RoundCountsWindow Cut((string Name, TimeSpan Length) window, DateTime nowUtc, IReadOnlyList<Launch> launches)
    {
        var from = Stamp(nowUtc - window.Length);
        var to = Stamp(nowUtc);
        var inside = launches.Where(launch => Inside(launch.StartedUtc, from, to));

        return new RoundCountsWindow(
            window.Name, from, to,
            [.. inside.GroupBy(launch => launch.Provider, StringComparer.Ordinal).OrderBy(row => row.Key, StringComparer.Ordinal).Select(RowOf)]);
    }

    /// <summary>
    /// The one place a window's edges are compared: <c>(from, to]</c> — a round ON the starting edge
    /// belongs to the next window up, a round AT the end belongs to every window.
    /// </summary>
    /// <remarks>
    /// Textual, over the column's own <c>"O"</c> UTC stamps, exactly as <c>RoundsQuery</c> compares a
    /// <c>--since</c>; the SQL's <c>$from</c>/<c>$to</c> are the same stamps with the same operators.
    /// </remarks>
    internal static bool Inside(string started, string from, string to) =>
        string.CompareOrdinal(started, from) > 0 && string.CompareOrdinal(started, to) <= 0;

    private static RoundCountsRow RowOf(IGrouping<string, Launch> ofProvider) =>
        new(ofProvider.Key, StagesOf(ofProvider), ofProvider.Count(), ModelsOf(ofProvider), KeysOf(ofProvider), WithoutKey(ofProvider));

    /// <summary>The distinct keys of the rounds counted, in ordinal order — a round with no key is not listed, it is counted by <see cref="WithoutKey"/>.</summary>
    private static IReadOnlyList<string> KeysOf(IEnumerable<Launch> launches) =>
        [.. launches.Select(launch => launch.RoundKey).Where(key => key.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>How many of the rounds counted carry no key (written before step 21, or skipped): the rest of a row's keys cannot account for them.</summary>
    private static int WithoutKey(IEnumerable<Launch> launches) => Distinct(launches.Where(launch => launch.RoundKey.Length == 0));

    /// <summary>Distinct rounds per stage, by the enum's own names — a stage this build does not know counts under none.</summary>
    internal static RoundsByStage StagesOf(IEnumerable<Launch> launches)
    {
        var rounds = launches
            .GroupBy(launch => launch.Stage, StringComparer.Ordinal)
            .ToDictionary(stage => stage.Key, Distinct, StringComparer.Ordinal);

        return new RoundsByStage(
            Plan: rounds.GetValueOrDefault(nameof(Stage.PlanReview)),
            Code: rounds.GetValueOrDefault(nameof(Stage.CodeReview)),
            Document: rounds.GetValueOrDefault(nameof(Stage.DocumentReview)),
            Feature: rounds.GetValueOrDefault(nameof(Stage.FeatureReview)));
    }

    private static int Distinct(IEnumerable<Launch> launches) => launches.Select(launch => launch.Key).Distinct(StringComparer.Ordinal).Count();

    /// <summary>Distinct rounds per model, most rounds first; a reviewer that recorded no model is on no line.</summary>
    private static IReadOnlyList<ModelRounds> ModelsOf(IEnumerable<Launch> launches) =>
    [
        .. launches
            .Where(launch => launch.Model.Length > 0)
            .GroupBy(launch => launch.Model, StringComparer.Ordinal)
            .Select(model => new ModelRounds(model.Key, Distinct(model)))
            .OrderByDescending(model => model.Rounds)
            .ThenBy(model => model.Model, StringComparer.Ordinal),
    ];

    /// <summary>
    /// The query behind <see cref="ModelsKnownSince"/>: rounds oldest first through <c>rounds_by_time</c>, each one's
    /// reviewers looked up by round, stopping at the first that recorded a model. Its cost is the rounds written before
    /// step 20 — fixed once the step ran — not the whole history, which a <c>MIN</c> over the join read every minute.
    /// </summary>
    internal const string ModelsKnownSinceSql =
        "SELECT r.started_utc FROM rounds r "
        + "WHERE EXISTS (SELECT 1 FROM reviewers v WHERE v.round_id = r.id AND v.model <> '') "
        + "ORDER BY r.started_utc LIMIT 1";

    /// <summary>The first round, over the whole table, whose reviewer recorded a model — or nothing.</summary>
    private static string? ModelsKnownSince(SqliteConnection db)
    {
        using var read = db.CreateCommand();
        read.CommandText = ModelsKnownSinceSql;

        return read.ExecuteScalar() is string since ? since : null;
    }

    /// <summary>The column's own spelling of an instant — seven fractional digits and a <c>Z</c>.</summary>
    internal static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);
}
