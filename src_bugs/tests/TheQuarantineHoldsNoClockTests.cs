using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// No table that carries <c>key_id</c> carries a clock time — the promise in its strong form.
/// </summary>
/// <remarks>
/// <para>The code round found what the first version of the promise missed: <c>quarantine</c> held
/// <c>received_utc</c>, an exact ISO-8601 instant, BESIDE <c>key_id</c>, so for any pair awaiting
/// review one could ask precisely which day and hour that contributor worked — while the promise
/// said no such question could be asked. The fix is to the DATA, not the wording: step 3 adds
/// <c>received_month</c>, the queue is ordered by <c>rowid</c>, and <c>received_utc</c> is written
/// empty for ever, because frozen step 1 forbids dropping it.</para>
/// <para><c>corpus.promoted_utc</c> stays exact, because <c>corpus</c> carries no <c>key_id</c>: a
/// promotion is a person's decision about a pair and is attributable to nobody who contributed. The
/// second test states that distinction structurally, so the rule it pins is "no table WITH a key id
/// has a clock", not "no clock anywhere" — and it derives the tables and columns from the schema
/// rather than naming them, so a fourth table with a key id is caught the day it is added.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheQuarantineHoldsNoClockTests
{
    private static readonly object OnePair = new
    {
        items = new[]
        {
            new
            {
                language = "CSharp",
                skeletonBefore = "method_1(var_1) { }",
                skeletonAfter = "method_1(var_1) { lock (var_2) { } }",
            },
        },
    };

    private static async Task<string> Ingested(BugsServer server)
    {
        var (key, _) = server.IssueKey();
        using var http = server.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

        var reply = await http.PostAsJsonAsync("/ingest", OnePair, TestContext.Current.CancellationToken);
        reply.StatusCode.Should().Be(HttpStatusCode.OK);

        return Path.Combine(server.DataDir, "coai-bugs.db");
    }

    [Fact]
    public async Task AnAcceptedPairCarriesTheMonthItArrivedAndNoClockTime()
    {
        using var server = new BugsServer();
        server.Clock.Set(new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero));

        var db = await Ingested(server);

        TestSql.Column(db, "SELECT received_utc FROM quarantine").Should().Equal(
            [string.Empty],
            "the exact time a contributor's pair arrived is what the promise says is never recorded; "
            + "the column is frozen in step 1 and is written empty");
        TestSql.Column(db, "SELECT received_month FROM quarantine").Should().Equal(
            ["2026-09"], "the month answers 'is this key alive' and nothing finer");
    }

    /// <summary>
    /// Every table carrying <c>key_id</c>, every <c>_utc</c> column in it, every row empty — read from
    /// the schema, never retyped.
    /// </summary>
    [Fact]
    public async Task NoTableThatCarriesAKeyIdCarriesAClockTime()
    {
        using var server = new BugsServer();
        var db = await Ingested(server);

        var tables = TestSql.Column(db, "SELECT name FROM sqlite_master WHERE type = 'table'");
        var carryingAKeyId = tables.Where(table => Columns(db, table).Contains("key_id")).ToList();
        carryingAKeyId.Should().Contain("quarantine", "the scan must find the one table that does, or it guards nothing");
        carryingAKeyId.Should().NotContain(
            "corpus", "corpus.promoted_utc is exact and stays: a promotion is attributable to nobody who contributed");

        foreach (var table in carryingAKeyId)
        {
            foreach (var clock in Columns(db, table).Where(column => column.EndsWith("_utc", StringComparison.Ordinal)))
            {
                var values = TestSql.Column(db, $"SELECT {clock} FROM {table}");
                values.Should().NotBeEmpty($"{table} must hold the row just ingested, or this proves nothing");
                values.Should().AllBe(string.Empty, $"{table}.{clock} sits beside key_id and must never hold a clock time");
            }
        }
    }

    private static IReadOnlyList<string> Columns(string db, string table) =>
        TestSql.Column(db, $"SELECT name FROM pragma_table_info('{table}')");
}
