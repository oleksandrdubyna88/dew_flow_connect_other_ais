using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The health of a <c>remote</c> vendor: whether its Team server answers, and whether that server
/// still offers the vendor this machine has configured.
/// </summary>
/// <remarks>
/// <para>Separate from <see cref="VendorProbe"/> on purpose. That probe runs a CLI and is pure
/// otherwise; this one reads a token off disk, makes a request, and remembers the answer. Folding a
/// network call into the process probe would also have given the Team server binary — which shares
/// <see cref="VendorProbe"/> — a code path it can never use.</para>
/// <para><b>The cache is not an optimisation, it is a rate limit.</b> The panel calls
/// <c>providers</c> whenever it is opened, and a person with three Team-server vendors would
/// otherwise send three requests per open to a server that is running other people's reviews.</para>
/// <para><b>Failure is cached for AT LEAST as long as success, and then longer.</b> The first draft
/// held a good answer for 60 s and a bad one for 15, which inverts backoff: a server that is down
/// gets asked four times as often as one that is up, and the moment it comes under load is the
/// moment every client starts polling it hardest. Raised twice on the plan round. Failures now back
/// off from <see cref="FirstBackoff"/> to <see cref="MaxBackoff"/>, doubling, and one success clears
/// it.</para>
/// </remarks>
public sealed class RemoteProbe(HttpClient http, Func<DateTime>? utcNow = null)
{
    /// <summary>How long a good answer stands before the server is asked again.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    /// <summary>The first wait after a failure — never shorter than <see cref="Fresh"/>.</summary>
    public static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(60);

    /// <summary>Where the doubling stops. A server down for an hour is asked six times, not 240.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(10);

    /// <summary>A probe is not a review: a server that cannot describe itself quickly is a finding.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, Cached> _cache = new();
    private readonly Func<DateTime> _now = utcNow ?? (() => DateTime.UtcNow);

    private sealed record Cached(VendorHealth Health, DateTime UntilUtc, TimeSpan Backoff);

    /// <summary>
    /// One remote vendor's health.
    /// </summary>
    /// <param name="dataDir">Where this machine keeps its Team-server tokens.</param>
    public async Task<VendorHealth> RunAsync(
        VendorIdentity vendor, bool enabled, string dataDir, CancellationToken ct = default)
    {
        var server = TeamServerAuth.Normalise(vendor.BaseUrl);
        var token = TeamServerAuth.ReadToken(TeamServerAuth.TokenPath(dataDir, server));
        if (WithoutAsking(server, enabled, token) is { } answer)
        {
            return answer;
        }

        // The token is part of the cache key — as a hash, and only so that signing in again is not
        // held behind a backoff earned by the token that was replaced.
        //
        // And so is whether the name was RECORDED, because two different rows can produce one
        // `VendorOnServer`: a row that records `codex`, and a row called `codex` that records
        // nothing and falls back to its id. They ask the server the same question and deserve
        // different sentences back, so they must not share one entry. Found by the automated
        // reviewer on the pull request that introduced the second sentence.
        var key = $"{server} {vendor.VendorOnServer} {vendor.HasRecordedRemoteVendor} "
            + TeamServerAuth.Fingerprint(token);
        if (Remembered(key) is { } fresh)
        {
            return fresh;
        }

        var (health, ok) = await AskAsync(server, vendor, token, enabled, ct);

        return Remember(key, health, ok);
    }

    /// <summary>
    /// The answers that need no request at all.
    /// </summary>
    /// <remarks>
    /// The "not signed in" one is deliberately NOT cached below: it is a fact about a file on this
    /// machine and it changes the instant somebody signs in, so a cached one would leave the panel
    /// telling a person to sign in for a minute after they just did.
    /// </remarks>
    private static VendorHealth? WithoutAsking(string server, bool enabled, string token) =>
        (server.Length == 0, enabled, token.Length == 0) switch
        {
            (true, _, _) => new VendorHealth(enabled, false, "", "unavailable",
                "this Team server vendor has no server URL — set one in the panel's Team servers section"),
            (false, false, _) => new VendorHealth(false, false, "", "unavailable", "disabled in settings"),
            (false, true, true) => new VendorHealth(enabled, false, "", "unavailable",
                RemoteAsk.NotSignedInMessage(server)),
            _ => null,
        };

    /// <summary>A cached answer that has not expired, or null.</summary>
    private VendorHealth? Remembered(string key) =>
        _cache.TryGetValue(key, out var cached) && _now() < cached.UntilUtc ? cached.Health : null;

    /// <summary>Keep this answer for as long as it deserves, and hand it back.</summary>
    private VendorHealth Remember(string key, VendorHealth health, bool ok)
    {
        _cache.TryGetValue(key, out var previous);
        var backoff = ok ? TimeSpan.Zero : Next(previous?.Backoff ?? TimeSpan.Zero);
        _cache[key] = new Cached(health, _now() + (ok ? Fresh : backoff), backoff);

        return health;
    }

    /// <summary>The next wait after a failure: the first one, or double the last, up to the cap.</summary>
    private static TimeSpan Next(TimeSpan previous) =>
        previous <= TimeSpan.Zero
            ? FirstBackoff
            : previous * 2 > MaxBackoff ? MaxBackoff : previous * 2;

    private async Task<(VendorHealth Health, bool Ok)> AskAsync(
        string server, VendorIdentity vendor, string token, bool enabled, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TeamServerAuth.Endpoint(server, "api/catalog"));
            request.Headers.Add("Authorization", "Bearer " + token);
            request.Headers.Add(RemoteAsk.ContractHeader, RemoteAsk.ContractVersion.ToString());
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var response = await http.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            return response.IsSuccessStatusCode
                ? (Read(server, vendor, body, enabled), true)
                : (Refused(server, (int)response.StatusCode, body, enabled), false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return (new VendorHealth(enabled, false, "", "unavailable",
                RemoteAsk.UnreachableMessage(server, e.Message)), false);
        }
    }

    /// <summary>
    /// A status the server gave instead of a catalog.
    /// </summary>
    /// <remarks>
    /// 401 and 403 are told apart from an outage and from each other: one is fixed by signing in
    /// again, one cannot be fixed by the person at all, and neither is the server being down. A
    /// single "unreachable" for all three sends somebody to restart a server that is running
    /// perfectly. (Accepted finding, plan round.)
    /// </remarks>
    private static VendorHealth Refused(string server, int status, string body, bool enabled)
    {
        var note = status switch
        {
            401 => RemoteAsk.RejectedMessage(server),
            403 => RemoteAsk.ForbiddenMessage(server),
            426 => RemoteAsk.TooOldMessage(server, body),
            _ => RemoteAsk.UnexpectedMessage(server, status, body),
        };

        // `CliFound` is "the thing that runs reviews answered". A server that returned 401 IS there,
        // which is why an auth refusal is not reported as an absent one.
        return new VendorHealth(enabled, status is 401 or 403 or 426, "", "unavailable", note);
    }

    /// <summary>
    /// What the catalog said about this vendor.
    /// </summary>
    /// <remarks>
    /// The slot counts come from the server, which already computed them for its own queue — so the
    /// note can say <i>why</i> a vendor is unusable rather than that it is. "2 accounts, all needing
    /// sign-in" is something the operator can act on; "unavailable" is not.
    /// </remarks>
    private static VendorHealth Read(string server, VendorIdentity vendor, string body, bool enabled) =>
        Parse(body) is not { } catalog
            ? new VendorHealth(enabled, false, "", "unavailable", RemoteAsk.UnreadableMessage(server, body))
            : catalog.Vendors?.FirstOrDefault(v =>
                    string.Equals(v.Id, vendor.VendorOnServer, StringComparison.OrdinalIgnoreCase))
                is not { } offered
                ? NotOffered(server, vendor, catalog, enabled)
                : Offered(server, catalog, offered, enabled);

    private static RemoteCatalog? Parse(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, RemoteCatalogContext.Default.RemoteCatalog);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Naming the offer is the difference between "unavailable" and a person fixing a typo.</summary>
    /// <remarks>
    /// And naming WHICH name was tried is the difference between a typo and a malformed row. A row
    /// that records no <c>remoteVendor</c> is asked about under its own id, and that id may be one
    /// the panel generated as <c>&lt;server&gt;-&lt;vendor&gt;</c> — <c>remsoftdev-claude</c> — which
    /// nothing here can tell apart from a name somebody typed on purpose. So the note reports which
    /// name was used and why, and claims nothing about where it came from. That distinction matters:
    /// the panel wrote no <c>remoteVendor</c> at all until 2026-09-07, so the plain sentence was the
    /// message every Team-server reviewer produced.
    /// </remarks>
    private static VendorHealth NotOffered(
        string server, VendorIdentity vendor, RemoteCatalog catalog, bool enabled)
    {
        var offered = catalog.Vendors is { Count: > 0 }
            ? string.Join(", ", catalog.Vendors.Select(v => v.Id))
            : "nothing";
        var asked = vendor.VendorOnServer;
        var plain = $"the Team server at {server} does not offer a vendor called '{asked}' — it offers {offered}";
        // Both halves of the unnamed sentence are FACTS, and the second draft of it was not: it said
        // the id was "not a name anybody typed", which is true of `remsoftdev-claude` and false of a
        // hand-written row somebody called `codex`. Nothing here can tell those apart, and the
        // repository's own test for the plain message was the fixture that proved it — a row with no
        // recorded name whose id is a perfectly plausible one. So this says what was used and why,
        // and lets the reader decide which case they are in.
        var note = vendor.HasRecordedRemoteVendor
            ? plain
            : $"{plain}. This row does not record which vendor its server knows it by, so its own id "
                + "was used as the name; if it came from a Team server, remove it and add the "
                + "reviewer again.";

        return new VendorHealth(enabled, true, catalog.ServerVersion ?? "", "unavailable", note);
    }

    /// <summary>The vendor is there; whether it can review is what its slots say.</summary>
    private static VendorHealth Offered(
        string server, RemoteCatalog catalog, RemoteCatalogVendor vendor, bool enabled)
    {
        var slots = vendor.Slots;
        var ready = slots?.Ready ?? 0;
        var version = $"server {catalog.ServerVersion}";

        return ready > 0
            ? new VendorHealth(enabled, true, version, "server token",
                $"{ready} of {slots!.Total} account(s) ready on the Team server at {server}")
            : new VendorHealth(enabled, true, version, "unavailable", NoSlotNote(server, slots));
    }

    /// <summary>Why nothing is ready — the two reasons have different cures and different owners.</summary>
    private static string NoSlotNote(string server, RemoteSlots? slots)
    {
        var total = slots?.Total ?? 0;
        if (total == 0)
        {
            return $"the Team server at {server} has no accounts configured for this vendor — ask the operator";
        }

        return (slots!.NeedsSignIn > 0, slots.CoolingDown > 0) switch
        {
            // Cooling down comes back by itself; needing sign-in never does. When both are true the
            // one a person must act on is the one worth printing.
            (true, _) => $"all {total} account(s) on the Team server at {server} are signed out — "
                + "the operator must sign them in on the server",
            (false, true) => $"all {total} account(s) on the Team server at {server} are rate-limited "
                + "right now — they come back by themselves",
            _ => $"no account on the Team server at {server} is ready right now",
        };
    }
}

internal sealed record RemoteSlots(int Total, int Ready, int CoolingDown, int NeedsSignIn);

internal sealed record RemoteCatalogVendor(string? Id, string? Runtime, RemoteSlots? Slots);

/// <summary>
/// The client's half of the catalog contract — the fields this probe reads and no others.
/// </summary>
/// <remarks>
/// Declared here rather than shared with the server's <c>CatalogDto</c> for the same reason the
/// contract header exists: these are two binaries that ship separately, and a client that could only
/// parse the catalog it was compiled beside would have to be updated in lockstep with every server.
/// A field added server-side is ignored here; a field removed leaves a default rather than a throw.
/// </remarks>
internal sealed record RemoteCatalog(
    string? ServerVersion, bool IsAdmin, IReadOnlyList<RemoteCatalogVendor>? Vendors, string? Error);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RemoteCatalog))]
internal sealed partial class RemoteCatalogContext : JsonSerializerContext;
