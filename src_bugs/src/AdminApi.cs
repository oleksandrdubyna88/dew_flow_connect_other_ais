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
        app.MapGet("/admin/active", () => Active(contributors, admins));
    }

    private static IResult Keys(HttpContext http, Corpus corpus)
    {
        var read = AdminPaging.From(http.Request.Query);
        if (read is not AdminPaging.Read.Page(var paging))
        {
            return Refused(read);
        }

        var page = corpus.KeysPage(paging.Limit, paging.Before);

        return TypedResults.Ok(new AdminWire.KeysPage(
            [.. page.Select(Listed)],
            paging.Limit,
            corpus.KeysTotal(),
            More(page.Count, paging.Limit, page.Count > 0 ? page[^1].Cursor : null)));
    }

    private static IResult Trail(HttpContext http, Corpus corpus)
    {
        var read = AdminPaging.From(http.Request.Query);
        if (read is not AdminPaging.Read.Page(var paging))
        {
            return Refused(read);
        }

        var page = corpus.AuditTrail(paging.Limit, paging.Before);

        return TypedResults.Ok(new AdminWire.AuditPage(
            [.. page.Select(row => new AdminWire.AuditListed(
                row.Id, row.Who.Value, row.Action.Word(), row.Target.Value, row.At.Stored))],
            paging.Limit,
            More(page.Count, paging.Limit, page.Count > 0 ? page[^1].Id : null)));
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

    /// <summary>Who is sending right now, across BOTH limiters.</summary>
    /// <remarks>
    /// <para><b>Both, because the contributor limiter is the one the question is about.</b> The two
    /// settings are separate, so there are two <see cref="RateLimiter"/> instances and each holds
    /// only its own callers — handing this route the admin limiter alone would answer with
    /// administrators and no contributors, which is the inverse of what the endpoint is for. Every
    /// row carries its subject's prefix, so `key:…` and `admin-…` are told apart without a second
    /// field, and the merged list is ordered once, here.</para>
    /// <para>It reads live dictionaries and persists nothing; an idle window is absent rather than
    /// reported as zero.</para>
    /// </remarks>
    private static IResult Active(RateLimiter contributors, RateLimiter admins) =>
        TypedResults.Ok(new AdminWire.ActiveNow(
            [.. Sending(contributors)
                .Concat(Sending(admins))
                .OrderByDescending(row => row.InWindow)
                .ThenBy(row => row.Id, StringComparer.Ordinal)],
            (int)RateLimiter.WindowLength.TotalSeconds));

    private static IEnumerable<AdminWire.ActiveCaller> Sending(RateLimiter limiter) =>
        limiter.ActiveNow().Select(row => new AdminWire.ActiveCaller(row.Subject, row.InWindow, row.Limited));

    /// <summary>The cursor for the NEXT page, or null when this one was the last.</summary>
    /// <remarks>
    /// A full page is the only reason to believe another exists; a short one is the end. This is why
    /// there is no `hasMore` field — the cursor's presence IS the answer, and two fields that must
    /// agree are two fields that can disagree.
    /// </remarks>
    private static long? More(int returned, int limit, long? last) =>
        returned == limit ? last : null;

    private static AdminWire.KeyListed Listed(KeyRow row) =>
        new(
            row.Id.Value,
            row.Note,
            row.Created.Stored,
            row.Revoked?.Stored,
            row.LastSeen is LastSeen.In seen ? seen.Month.Value : null,
            row.Sent.Value,
            row.Waiting);

    private static IResult Refused(AdminPaging.Read read) =>
        read is AdminPaging.Read.Refused refused
            ? TypedResults.BadRequest(new Problem(refused.Why))
            : throw new InvalidOperationException("only a refusal reaches here");

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
