using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>
/// What this server can run: the operator's allowlist, each CLI's presence, and the accounts.
/// </summary>
public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(
        this WebApplication app, VendorCatalogHost catalog, SlotRegistry slots, VendorHealthCache health, CallerFilter gate)
    {
        // The CancellationToken is not decoration: a lambda whose ONLY parameter is HttpContext is
        // treated by the framework as a RequestDelegate, and a RequestDelegate returns Task, so the
        // IResult this handler returns was discarded and the endpoint answered 200 with an empty
        // body. Found by this story's own catalog tests. A second parameter makes it an ordinary
        // handler again, and RequestAborted is the one worth having.
        app.MapGet("/api/catalog", async (HttpContext ctx, CancellationToken ct) =>
        {
            var caller = ctx.CallerOf();
            var now = DateTimeOffset.UtcNow;
            var current = catalog.Current;
            // Probed in PARALLEL, and by runtime rather than by vendor. Sequentially, a catalog with
            // ten vendors waited for ten process launches one after another before answering; the
            // cache is keyed per runtime, so several vendors on one CLI also collapse to one probe.
            // (Three reviewers, code round.)
            var health_ = await Task.WhenAll(
                current.Vendors.Select(async v => (v.Id, Health: await health.OfAsync(v.Runtime, ct))));
            var byVendor = health_.ToDictionary(h => h.Id, h => h.Health, StringComparer.OrdinalIgnoreCase);

            var vendors = current.Vendors
                .Select(vendor => new CatalogVendorDto(
                    vendor.Id,
                    vendor.Runtime,
                    vendor.Models,
                    byVendor[vendor.Id],
                    Summarise(slots.SlotsOf(vendor), now)))
                .ToList();

            return Results.Json(
                new CatalogDto(Startup.Version, caller.IsAdmin, vendors, ErrorFor(current, catalog)),
                ServerJsonContext.Default.CatalogDto);
        }).RequireCaller(gate);
    }

    /// <summary>
    /// The account counts, which are the ONLY place this endpoint speaks about authentication.
    /// </summary>
    /// <remarks>
    /// The plan round raised this three times, from two vendors: the health probe runs the CLI with
    /// the SERVER's environment, not any slot's <c>HOME</c>, so it can only answer "is the binary
    /// there". If it were allowed to imply authentication, a vendor whose every account was signed
    /// out would show healthy and a caller would be told the problem is elsewhere. Presence comes
    /// from the probe; usability comes from here, and the two never mix.
    /// </remarks>
    private static SlotSummaryDto Summarise(IReadOnlyList<AccountSlot> slots, DateTimeOffset nowUtc) =>
        new(
            slots.Count,
            slots.Count(s => s.IsReady(nowUtc)),
            slots.Count(s => s.IsCoolingDown(nowUtc)),
            slots.Count(s => s.NeedsSignIn));

    /// <summary>The sentence the operator needs, or empty when nothing is wrong.</summary>
    private static string ErrorFor(VendorCatalog current, VendorCatalogHost host) =>
        current.Error.Length > 0 ? current.Error
        : current.Vendors.Count == 0 ? $"no vendors are configured — create {host.FilePath}"
        : string.Empty;
}

/// <summary>
/// Each CLI's presence and version, asked at most once a minute.
/// </summary>
/// <remarks>
/// <para><see cref="VendorProbe"/> LAUNCHES the CLI to ask its version. The panel polls this catalog
/// while it is open, so an uncached probe would start three processes per poll on a 3.8 GB box whose
/// memory is the binding constraint of the whole deployment.</para>
/// <para>Sixty seconds because the thing being cached changes only when somebody installs or upgrades
/// a CLI on the VM — an event measured in months. The TTL is short enough that an operator who just
/// fixed an install sees it on their next look, rather than being told to restart the server.</para>
/// </remarks>
public sealed class VendorHealthCache(IProcessLauncher launcher, TimeSpan? ttl = null)
{
    private readonly TimeSpan _ttl = ttl ?? TimeSpan.FromSeconds(60);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset AtUtc, VendorHealthDto Health)> _seen =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>One gate PER RUNTIME, not one for the whole cache.</summary>
    /// <remarks>
    /// A single semaphore made every catalog request wait behind whichever probe happened to be
    /// running — including requests for a different vendor, and including ones whose answer was
    /// already cached. Per runtime, a slow <c>codex --version</c> delays only codex, and it still
    /// collapses a burst of simultaneous first-requests into one launch. (codex and gemini, code round.)
    /// </remarks>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> _gates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times the CLI has actually been launched. Test-visible on purpose.</summary>
    internal int Probes;

    public async Task<VendorHealthDto> OfAsync(string runtime, CancellationToken ct = default)
    {
        if (Fresh(runtime) is { } cached)
        {
            return cached;
        }

        var gate = _gates.GetOrAdd(runtime, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // Checked again inside the gate: several callers can miss the cache at once, and only
            // the first of them should start a process.
            if (Fresh(runtime) is { } arrived)
            {
                return arrived;
            }

            Interlocked.Increment(ref Probes);
            // The vendor's PROVIDER and RUNTIME are both the runtime name here, and the base URL is
            // empty: a Team server runs the vendor's own CLI with the vendor's own sign-in, which is
            // exactly the identity `RuntimeResolution.NameOf` resolves to that CLI's adapter. No
            // vault key either — the accounts are OAuth sign-ins in slot directories, not API keys.
            var probed = await VendorProbe.RunAsync(
                launcher,
                new VendorIdentity(runtime, runtime, string.Empty),
                enabled: true,
                executablePath: string.Empty,
                model: string.Empty,
                hasVaultKey: false,
                ct: ct);
            var health = new VendorHealthDto(probed.CliFound, probed.Version, probed.Note);
            _seen[runtime] = (DateTimeOffset.UtcNow, health);

            return health;
        }
        finally
        {
            gate.Release();
        }
    }

    private VendorHealthDto? Fresh(string runtime) =>
        _seen.TryGetValue(runtime, out var cached) && DateTimeOffset.UtcNow - cached.AtUtc < _ttl
            ? cached.Health
            : null;
}
