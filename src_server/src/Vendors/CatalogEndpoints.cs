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
            var vendors = new List<CatalogVendorDto>(current.Vendors.Count);

            foreach (var vendor in current.Vendors)
            {
                vendors.Add(new CatalogVendorDto(
                    vendor.Id,
                    vendor.Runtime,
                    vendor.Models,
                    await health.OfAsync(vendor.Runtime, ct),
                    Summarise(slots.SlotsOf(vendor), now)));
            }

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
    private readonly Dictionary<string, (DateTimeOffset AtUtc, VendorHealthDto Health)> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>How many times the CLI has actually been launched. Test-visible on purpose.</summary>
    internal int Probes { get; private set; }

    public async Task<VendorHealthDto> OfAsync(string runtime, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_seen.TryGetValue(runtime, out var cached) && DateTimeOffset.UtcNow - cached.AtUtc < _ttl)
            {
                return cached.Health;
            }

            Probes += 1;
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
            _gate.Release();
        }
    }
}
