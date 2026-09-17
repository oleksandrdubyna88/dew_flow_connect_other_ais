using System.Net;
using FluentAssertions;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// Paging, which is keyset — and the arithmetic that made it so.
/// </summary>
/// <remarks>
/// <para><b>Why not `OFFSET`.</b> An offset is not insert-stable: issuing a key between the first
/// page and the second shifts every boundary after it, so a row is duplicated or hidden.
/// <see cref="StabilityAcrossAnInsert"/> is that exact sequence, and it is the test an offset
/// implementation cannot pass — with `skip=2` the second page would answer the row already seen.
/// On a table of tens of rows the performance argument for keyset never applies; correctness does.
/// </para>
/// <para><b>An illegal value is a 400 naming what was legal, never a silent clamp.</b> A clamp
/// answers a question nobody asked and hides the caller's bug until it matters. `limit=0` in
/// particular does not mean "everything" — a limit nobody can exceed is how a listing endpoint
/// quietly becomes a full-table read.</para>
/// <para><b>`total` is on the keys page and not on the audit</b>, and both halves are asserted on
/// the raw bytes: `api_keys` is tens of rows so counting is free, while the audit is bounded at
/// 50 000 and counting it on every page would scan the table while holding the corpus gate.</para>
/// </remarks>
[Collection("the-server")]
public sealed class TheAdminPagingTests
{
    private const string AdminKey = "an-administrators-key";

    /// <summary>
    /// A key issued between two page requests neither hides a row nor repeats one.
    /// </summary>
    [Fact]
    public async Task StabilityAcrossAnInsert()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);
        var first = await TheAdminRoutesTests.Issue(http, "first");
        var second = await TheAdminRoutesTests.Issue(http, "second");
        var third = await TheAdminRoutesTests.Issue(http, "third");

        var page = await TheAdminRoutesTests.Get<TheAdminRoutesTests.KeysPage>(http, "/admin/keys?limit=2");
        page.Items.Select(item => item.Id).Should().Equal([third.Id, second.Id], "newest first");
        page.NextBefore.Should().NotBeNull("a full page is a reason to believe there is another");

        // The insert that breaks an offset. It lands at the TOP of the ordering, so `skip=2` would
        // now start at the row this reader has already seen.
        var fourth = await TheAdminRoutesTests.Issue(http, "issued between the two requests");

        var next = await TheAdminRoutesTests.Get<TheAdminRoutesTests.KeysPage>(
            http, $"/admin/keys?limit=2&before={page.NextBefore}");

        next.Items.Select(item => item.Id).Should().Equal(
            [first.Id],
            "the cursor names a row, not a position: the page after `second` is `first`, whatever "
            + "was inserted at the top meanwhile");
        next.Items.Should().NotContain(item => item.Id == second.Id, "no row is served twice");
        next.Items.Should().NotContain(item => item.Id == fourth.Id, "nor does a later insert appear behind");
        next.NextBefore.Should().BeNull("a short page is the end");
        next.Total.Should().Be(4, "total is the table's, and it counts the new one");
    }

    /// <summary>Paging to the end terminates, even when the last page was exactly full.</summary>
    /// <remarks>
    /// The cursor's presence means "this page was full", which is the only evidence available
    /// without a second query — so a client following it asks once more and gets nothing. That the
    /// extra request is an EMPTY page rather than an error is what makes the loop safe to write.
    /// </remarks>
    [Fact]
    public async Task FollowingTheCursorTerminates()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);
        for (var at = 0; at < 4; at++)
        {
            await TheAdminRoutesTests.Issue(http, $"key {at}");
        }

        var seen = new List<string>();
        long? cursor = null;
        for (var request = 0; request < 10; request++)
        {
            var route = cursor is null ? "/admin/keys?limit=2" : $"/admin/keys?limit=2&before={cursor}";
            var page = await TheAdminRoutesTests.Get<TheAdminRoutesTests.KeysPage>(http, route);
            seen.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextBefore;
            if (cursor is null)
            {
                break;
            }
        }

        cursor.Should().BeNull("the walk must have finished rather than run out of requests");
        seen.Should().HaveCount(4).And.OnlyHaveUniqueItems("every row exactly once");
    }

    /// <summary>The audit pages the same way, by the row id that is also its cursor.</summary>
    [Fact]
    public async Task TheAuditPagesByItsOwnRowId()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);
        var first = await TheAdminRoutesTests.Issue(http, "first");
        await TheAdminRoutesTests.Issue(http, "second");

        var page = await TheAdminRoutesTests.Get<TheAdminRoutesTests.AuditPage>(http, "/admin/audit?limit=1");
        page.Items.Should().ContainSingle().Which.Target.Should().NotBe(first.Id, "newest first");
        page.NextBefore.Should().Be(page.Items[0].Id, "the cursor IS the row id");

        var next = await TheAdminRoutesTests.Get<TheAdminRoutesTests.AuditPage>(
            http, $"/admin/audit?limit=1&before={page.NextBefore}");

        next.Items.Should().ContainSingle().Which.Target.Should().Be(first.Id);
        next.Items[0].Id.Should().BeLessThan(page.Items[0].Id, "older rows have smaller ids");
    }

    /// <summary>`total` on the keys page, and nowhere on the audit.</summary>
    [Fact]
    public async Task TotalIsOnTheKeysPageAndNotOnTheAudit()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);
        await TheAdminRoutesTests.Issue(http, "a contributor");

        var keys = await Body(http, "/admin/keys");
        var audit = await Body(http, "/admin/audit");

        keys.Should().Contain("\"total\":1", "api_keys is tens of rows; counting it is free");
        audit.Should().NotContain(
            "\"total\"",
            "the audit is bounded at 50 000 and counting it on every page would scan the table "
            + "while holding the corpus gate");
        audit.Should().Contain("\"items\"").And.Contain("\"limit\"", "the rest of the page is there");
    }

    /// <summary>`limit=0` is refused, and the refusal says so in those words.</summary>
    [Fact]
    public async Task LimitZeroIsRefusedRatherThanMeaningEverything()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);

        foreach (var route in new[] { "/admin/keys?limit=0", "/admin/audit?limit=0" })
        {
            var reply = await http.GetAsync(route, TestContext.Current.CancellationToken);
            var why = await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            reply.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{route} must refuse");
            why.Should().Contain("0 is refused").And.Contain(
                Corpus.MostAuditPage.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "a refusal that does not name what was legal makes the caller guess");
        }
    }

    /// <summary>A limit past the cap is refused, not quietly reduced.</summary>
    [Fact]
    public async Task ALimitPastTheCapIsRefused()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);

        var reply = await http.GetAsync(
            $"/admin/keys?limit={Corpus.MostAuditPage + 1}", TestContext.Current.CancellationToken);

        reply.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a clamp hides the caller's bug");
    }

    /// <summary>A limit or cursor that is not a number is refused, with the value quoted back.</summary>
    [Fact]
    public async Task SomethingThatIsNotANumberIsRefused()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);

        foreach (var (route, quoted) in new[]
        {
            ("/admin/keys?limit=twenty", "twenty"),
            ("/admin/keys?before=yesterday", "yesterday"),
            ("/admin/keys?limit=-5", "-5"),
        })
        {
            var reply = await http.GetAsync(route, TestContext.Current.CancellationToken);
            var why = await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            reply.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{route} must refuse");
            why.Should().Contain(quoted, "the value that was wrong is the one thing worth quoting back");
        }
    }

    /// <summary>A cursor past the end is an empty page, because that is a client doing it right.</summary>
    [Fact]
    public async Task ACursorPastTheEndIsAnEmptyPageAndNotAnError()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);
        await TheAdminRoutesTests.Issue(http, "a contributor");

        var keys = await TheAdminRoutesTests.Get<TheAdminRoutesTests.KeysPage>(http, "/admin/keys?before=1");
        var audit = await TheAdminRoutesTests.Get<TheAdminRoutesTests.AuditPage>(http, "/admin/audit?before=1");

        keys.Items.Should().BeEmpty("paging off the end is the last request of a correct client");
        keys.NextBefore.Should().BeNull();
        keys.Total.Should().Be(1, "the table still has a row; this page just has none of it");
        audit.Items.Should().BeEmpty();
        audit.NextBefore.Should().BeNull();
    }

    /// <summary>Absent parameters mean the first page at the default size.</summary>
    [Fact]
    public async Task NoParametersMeansTheNewestPageAtTheDefaultSize()
    {
        using var server = new BugsServer(adminKeys: AdminKey);
        using var http = server.Bearing(AdminKey);
        await TheAdminRoutesTests.Issue(http, "a contributor");

        var page = await TheAdminRoutesTests.Get<TheAdminRoutesTests.KeysPage>(http, "/admin/keys");

        page.Limit.Should().Be(AdminPaging.DefaultLimit);
        page.Items.Should().ContainSingle();
    }

    private static async Task<string> Body(HttpClient http, string route)
    {
        var reply = await http.GetAsync(route, TestContext.Current.CancellationToken);
        reply.StatusCode.Should().Be(HttpStatusCode.OK, $"{route} must answer");

        return await reply.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }
}
