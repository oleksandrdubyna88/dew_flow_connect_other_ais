using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CoaiBugs;

/// <summary>
/// The five admin routes. Authentication and the limit are the gate's; this is the work.
/// </summary>
/// <remarks>
/// <para>Every handler is reached only through <see cref="AdminGate"/>, which is a prefix match on
/// `/admin` so a route added later is protected by default rather than by being remembered.
/// <see cref="AdminGate.Of"/> throws if one is reached around the gate, because that is a wiring
/// defect and not a request.</para>
/// <para>Nothing here opens a transaction: `Corpus.Issue` and `Corpus.Revoke` each commit their
/// mutation, its audit row and the retention sweep together, so a second transaction around them
/// would only be a way to get that wrong.</para>
/// </remarks>
internal static partial class AdminApi
{
    /// <summary>The longest note accepted.</summary>
    public const int MostNote = 200;

    /// <summary>Wires the five routes.</summary>
    /// <param name="contributors">
    /// The limiter <c>/ingest</c> admits against — the one holding the activity this server is asked
    /// about. See <see cref="Active"/> for why <c>/admin/active</c> needs both.
    /// </param>
    /// <param name="admins">The limiter the admin gate admits against, by its own setting.</param>
    public static void MapAdmin(
        this WebApplication app,
        Corpus corpus,
        RateLimiter contributors,
        RateLimiter admins,
        ServerSecret secret,
        TimeProvider clock)
    {
        app.MapGet("/admin/keys", (HttpContext http) => Keys(http, corpus));
        app.MapPost("/admin/keys", (AdminWire.IssueRequest? body, HttpContext http) =>
            Issue(http, corpus, secret, clock, body));
        app.MapPost("/admin/keys/{id}/revoke", (string id, HttpContext http) => Revoke(http, corpus, clock, id));
        app.MapGet("/admin/audit", (HttpContext http) => Trail(http, corpus));
        app.MapGet("/admin/active", (HttpContext http) => Active(http, contributors, admins));
    }

    private static IResult Keys(HttpContext http, Corpus corpus)
    {
        var read = AdminPaging.From(http.Request.Query);
        if (read is not AdminPaging.Read.Page(var paging))
        {
            return Malformed(read.Refusal);
        }

        var token = KeysCursor.From(paging.Before);
        if (token is not KeysCursor.Read.Page(var cursor))
        {
            return Malformed(token.Refusal);
        }

        var page = corpus.KeysPage(paging.Limit, cursor);

        return TypedResults.Ok(new AdminWire.KeysPage(
            [.. page.Select(Listed)],
            paging.Limit,
            corpus.KeysTotal(),
            More(page.Count, paging.Limit, page.Count > 0 ? page[^1].Cursor.Token : string.Empty)));
    }

    private static IResult Trail(HttpContext http, Corpus corpus)
    {
        var read = AdminPaging.From(http.Request.Query);
        if (read is not AdminPaging.Read.Page(var paging))
        {
            return Malformed(read.Refusal);
        }

        var token = KeysCursor.Audit(paging.Before);
        if (token is not KeysCursor.Read<long>.Page(var before))
        {
            return Malformed(token.Refusal);
        }

        var page = corpus.AuditTrail(paging.Limit, before);

        return TypedResults.Ok(new AdminWire.AuditPage(
            [.. page.Select(row => new AdminWire.AuditListed(
                row.Id, row.Who.Value, row.Action.Word(), row.Target.Value, row.At.Stored))],
            paging.Limit,
            More(
                page.Count,
                paging.Limit,
                page.Count > 0 ? page[^1].Id.ToString(CultureInfo.InvariantCulture) : string.Empty)));
    }

    private static IResult Issue(
        HttpContext http, Corpus corpus, ServerSecret secret, TimeProvider clock, AdminWire.IssueRequest? body)
    {
        var note = body?.Note ?? string.Empty;
        if (WhyNoteIsRefused(note) is { Length: > 0 } why)
        {
            return TypedResults.BadRequest(new Problem(why));
        }

        // The key is minted here and printed ONCE. Nothing stores it: `Issue` takes its hash.
        var key = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var id = new KeyId(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)));
        var by = Audit.By(AdminGate.Of(http), clock);
        corpus.Issue(id, Corpus.HashOf(key, secret.Value), note, by);

        return TypedResults.Created(
            $"/admin/keys/{id.Value}",
            new AdminWire.Issued(id.Value, key, note, by.At.Stored));
    }

    private static IResult Revoke(HttpContext http, Corpus corpus, TimeProvider clock, string id) =>
        corpus.Revoke(new KeyId(id), Audit.By(AdminGate.Of(http), clock)) switch
        {
            Revoked.NoSuchKey => TypedResults.NotFound(new Problem($"no key {id} exists")),
            Revoked.Now now => TypedResults.Ok(new AdminWire.Revocation(id, now.At.Stored, true)),
            Revoked.Already already =>
                TypedResults.Ok(new AdminWire.Revocation(id, already.At.Stored, false)),
            _ => throw new InvalidOperationException("Revoked has no fourth case"),
        };

    /// <summary>Who is sending right now, across BOTH limiters, busiest first and BOUNDED.</summary>
    /// <remarks>
    /// <para><b>Both, because the contributor limiter is the one the question is about.</b> The two
    /// settings are separate, so there are two <see cref="RateLimiter"/> instances and each holds
    /// only its own callers — handing this route the admin limiter alone would answer with
    /// administrators and no contributors, which is the inverse of what the endpoint is for. Every
    /// row carries its subject's prefix, so `key:…` and `admin-…` are told apart without a second
    /// field, and the merged list is ordered once, here.</para>
    /// <para><b>Bounded, and it says when it truncated.</b> It answered every active subject, and a
    /// code round did the arithmetic the growth budget allows: a thousand live keys all sending
    /// inside one minute is a thousand records built, sorted and serialised for one request — the
    /// response grows with the flood it is being read to diagnose. So it takes the same `limit` as
    /// the listings and carries `total`, and because the rows are ordered busiest-first the bound
    /// keeps the only part anybody reads. `total` is free here: it is the length of a list already
    /// in memory, not a table scan, which is why this route has one where the audit does not.</para>
    /// <para>It reads live dictionaries and persists nothing; an idle window is absent rather than
    /// reported as zero.</para>
    /// </remarks>
    private static IResult Active(HttpContext http, RateLimiter contributors, RateLimiter admins)
    {
        var read = AdminPaging.From(http.Request.Query);
        if (read is not AdminPaging.Read.Page(var paging))
        {
            return Malformed(read.Refusal);
        }

        var sending = Sending(contributors).Concat(Sending(admins)).ToList();

        return TypedResults.Ok(new AdminWire.ActiveNow(
            [.. sending
                .OrderByDescending(row => row.InWindow)
                .ThenBy(row => row.Id, StringComparer.Ordinal)
                .Take(paging.Limit)],
            paging.Limit,
            sending.Count,
            (int)RateLimiter.WindowLength.TotalSeconds));
    }

    private static IEnumerable<AdminWire.ActiveCaller> Sending(RateLimiter limiter) =>
        limiter.ActiveNow().Select(row => new AdminWire.ActiveCaller(row.Subject, row.InWindow, row.Limited));

    /// <summary>The cursor for the NEXT page, or null when this one was the last.</summary>
    /// <remarks>
    /// <para>A full page is the only reason to believe another exists; a short one is the end. This
    /// is why there is no `hasMore` field — the cursor's presence IS the answer, and two fields that
    /// must agree are two fields that can disagree.</para>
    /// <para>A token, not a number, on BOTH listings even though the audit's is a decimal id
    /// underneath: a client's rule is "pass back what you were given", and one rule is easier to
    /// keep than two. It is also what stops a client computing a cursor, which is how
    /// `?before=-1` came to answer an empty page that read like the end of the list.</para>
    /// </remarks>
    private static string? More(int returned, int limit, string last) =>
        returned == limit && last.Length > 0 ? last : null;

    private static AdminWire.KeyListed Listed(KeyRow row) =>
        new(
            row.Id.Value,
            row.Note,
            row.Created.Stored,
            row.Revoked?.Stored,
            row.LastSeen is LastSeen.In seen ? seen.Month.Value : null,
            row.Sent.Value,
            row.Waiting);

    /// <summary>A 400 saying what was legal. One helper for every malformed parameter.</summary>
    /// <remarks>
    /// It was called `Refused`, which in this file sits beside a 401 refusal and a 429 refusal and
    /// therefore said nothing about which of the three it was. (Code round, local.) `Malformed` is
    /// only ever the request's own shape: the gate answers the other two and this is never reached
    /// for either.
    /// </remarks>
    private static IResult Malformed(string why) => TypedResults.BadRequest(new Problem(why));

    /// <summary>Why a note cannot be stored, or empty when it can.</summary>
    /// <remarks>
    /// <para>It catches ONE shape and no other: `bob@example.com` is refused and `Bob Smith` is not,
    /// which is the very mistake the guard exists for. It is worth having because it is free, and it
    /// is written down because a guard that stops one spelling invites the belief that the problem is
    /// handled. What actually protects the promise is that the note is OUR record of why a key exists
    /// and nothing reads it as identity.</para>
    /// </remarks>
    private static string WhyNoteIsRefused(string note) =>
        note.Length > MostNote
            ? $"a note is at most {MostNote} characters; this one is {note.Length}"
            : EmailShaped().IsMatch(note)
                ? "a note that looks like an email address is refused: it is OUR record of why a key "
                  + "exists — \"for the tuesday workshop\" — and never the holder's identity"
                : string.Empty;

    [GeneratedRegex(@"[^@\s]+@[^@\s]+\.[^@\s]+")]
    private static partial Regex EmailShaped();
}
