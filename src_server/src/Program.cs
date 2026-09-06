using System.Security.Claims;
using System.Threading.RateLimiting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiServer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;

// coai-server — the Team server: one subscription per vendor, shared by everyone who signs in.
//
// This build is story 2.1 of todo/PLAN_team_server.md: the host, its authentication and its
// sessions. No vendors, no catalog, no jobs — those are 2.2 to 2.4, and everything else 404s.
//
// The pipeline's ORDER is the load-bearing part and is mirrored from the vault server, where each
// step was paid for: the contract version is judged before the token, so an old client is told to
// update rather than handed a 401 about a token that was never the problem; and the caller is
// resolved before the rate limiter, so the limiter partitions on an email rather than on the
// proxy's address — which once throttled an entire company as a single client.

// --healthcheck is the container's probe: this binary asking its own /api/health, because a
// chiselled image has no curl and nothing to run one with.
if (args is ["--healthcheck"])
{
    return await HealthProbe.RunAsync();
}

// `login` is a SUBCOMMAND of the same binary, run as `docker compose exec coai-server login codex a`.
// It never starts the web host: an interactive sign-in waits on a human at a terminal, and the one
// thing it must share with the running server is the account lock — which is a file, so it does.
if (args is ["login", ..])
{
    return await VendorLogin.RunAsync(
        args,
        Environment.GetEnvironmentVariable("Coai__DataDir") ?? "/data",
        Console.Out,
        // How long to wait for a review that is using the account. Configurable because the right
        // answer depends on how long this deployment's reviews run, and because an operator who
        // knows the box is idle should not be made to wait the default.
        int.TryParse(Environment.GetEnvironmentVariable("Coai__LoginWaitSeconds"), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null,
        // And how long the PERSON is given to finish the sign-in the CLI starts. A device flow that
        // nobody completes would otherwise hold the operator's terminal indefinitely.
        int.TryParse(Environment.GetEnvironmentVariable("Coai__LoginTimeoutSeconds"), out var signIn)
            ? TimeSpan.FromSeconds(signIn)
            : null);
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog(CoaiMcp.ServiceDefaults.CoaiLogging.CreateDewFlowLogger("coai-server"));

var config = builder.Configuration;
var dataDir = config["Coai:DataDir"] ?? "/data";
var allowedDomains = SplitCsv(config["Coai:AllowedDomains"]);
var allowAnyDomain = config.GetValue("Coai:AllowAnyDomain", false);
var admins = SplitCsv(config["Coai:Admins"]);
var sessionTtlDays = config.GetValue("Coai:SessionTtlDays", 7);
var requireHttps = config.GetValue("Coai:RequireForwardedHttps", true);
var minimumContract = config.GetValue("Coai:MinimumClientContract", ContractVersion.DefaultMinimumSupported);

var msTenant = config["Auth:Microsoft:Tenant"];
var msAudiences = SplitCsv(config["Auth:Microsoft:Audiences"]);
var msClientScope = (config["Auth:Microsoft:ClientScope"] ?? string.Empty).Trim();
var googleEnabled = config.GetValue("Auth:Google:Enabled", false);
var googleAudiences = SplitCsv(config["Auth:Google:Audiences"]);
var localKey = config["Auth:Local:SigningKey"];
var localEnabled = !string.IsNullOrWhiteSpace(localKey);

Startup.Guard(msTenant, msAudiences, googleEnabled, localEnabled, localKey, allowedDomains, allowAnyDomain, dataDir, sessionTtlDays);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // WHO may claim to be a proxy. An earlier draft cleared both lists, which trusts these headers
    // from anybody who can reach the socket — and a caller that reached it directly could then send
    // `X-Forwarded-Proto: https` to walk past the HTTPS check, or forge `X-Forwarded-For` to move
    // themselves into somebody else's rate-limit partition. Raised by two reviewers on this
    // change's code round.
    //
    // Loopback and the private ranges, and nothing else: this app publishes no public port — the
    // compose file binds it to 127.0.0.1 and the host nginx is the only thing in front — so a
    // PUBLIC address appearing here would mean the topology is not what this line assumes. It is
    // the same restriction the vault's nginx applies with `set_real_ip_from`, for the same reason.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var network in TrustedProxies.From(config["Coai:TrustedProxies"]))
    {
        options.KnownIPNetworks.Add(network);
    }
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        // The EMAIL, not the address. Everyone behind one company's proxy shares an address, and a
        // limiter partitioned on it is one bucket for the whole company — measured on the vault,
        // where one busy client throttled everybody. Anonymous callers fall back to the address,
        // which is all there is to partition them by.
        RateLimitPartition.GetFixedWindowLimiter(
            TokenIdentity.Email(ctx.User) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = config.GetValue("Coai:RateLimit:PermitLimit", 120),
                Window = TimeSpan.FromSeconds(config.GetValue("Coai:RateLimit:WindowSeconds", 10)),
                QueueLimit = 0,
            }));
});

Auth.AddSchemes(builder.Services, msTenant, msAudiences, googleEnabled, googleAudiences, localKey, localEnabled);

// The store is built before the host, so it cannot hold a logger yet — it holds a hop to one.
// Nothing calls the store before the assignment below, and after it every swallowed filesystem
// failure reaches the log instead of only a null.
Action<string, Exception>? reportSessionFailure = null;
Action<string>? reportCatalog = null;
var sessions = new SessionStore(
    dataDir,
    TimeSpan.FromDays(sessionTtlDays),
    (message, error) => reportSessionFailure?.Invoke(message, error));
builder.Services.AddSingleton(sessions);

// The vendor catalog and the accounts. The catalog re-reads vendors.json when it changes; the
// registry owns the cross-process lock that keeps two launches off one account.
var files = new JsonFileStore((message, error) => reportSessionFailure?.Invoke(message, error));
var catalog = new VendorCatalogHost(dataDir, message => reportCatalog?.Invoke(message));
var slotRegistry = new SlotRegistry(dataDir, files, (message, error) => reportSessionFailure?.Invoke(message, error));
var vendorHealth = new VendorHealthCache(new ProcessLauncher());

// The jobs, and the loop that runs them. The pump is a hosted service so it starts with the host and
// stops with it; an in-flight review dies with the container, which is what `lost` exists to report.
var jobs = new JobStore(
    config.GetValue("Coai:PerCallerQueued", 20),
    config.GetValue("Coai:PerCallerRunning", 3));
var jobRunner = new JobRunner(
    jobs,
    catalog,
    slotRegistry,
    new ReviewLauncher(new ProcessLauncher()),
    new UsageLedger(dataDir),
    (message, error) => reportSessionFailure?.Invoke(message, error));
builder.Services.AddSingleton(jobs);
builder.Services.AddHostedService(sp => new JobPump(
    jobs, catalog, jobRunner, sp.GetRequiredService<ILogger<JobPump>>()));

var app = builder.Build();
var log = app.Logger;
// Wired after Build() because that is when a logger exists; the store holds the delegate, so a
// failure that happens before this point simply has nowhere to go — and nothing runs before it.
reportSessionFailure = (message, error) => log.LogWarning(error, "{Message}", message);
reportCatalog = message => log.LogInformation("{Message}", message);

var swept = sessions.Sweep(DateTimeOffset.UtcNow);
if (swept > 0)
{
    log.LogInformation("swept {Count} expired session(s) at startup", swept);
}

// The catalog was built before the logger existed, so its first load had nowhere to report. Say
// what it holds now — a server whose vendors.json was refused at boot must not look identical in
// the log to one that loaded three vendors.
var loaded = catalog.Current;
log.LogInformation(
    "vendors: {Count} loaded from {Path}{Problem}",
    loaded.Vendors.Count,
    catalog.FilePath,
    loaded.Error.Length > 0 ? $" — REFUSED: {loaded.Error}" : string.Empty);

app.UseForwardedHeaders();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandler = async ctx =>
    {
        var error = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
        log.LogError(error, "unhandled error on {Method} {Path}", ctx.Request.Method, ctx.Request.Path);
        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await ctx.Response.WriteAsJsonAsync(new ErrorDto("internal error"), ServerJsonContext.Default.ErrorDto);
    },
});

// The contract version, decided BEFORE authentication so a client too old to be served is told
// THAT rather than being handed a 401 about a token that was never the problem. Every response
// carries the server's version, so a client learns it from a call it was already making.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers[ContractVersion.Header] = ContractVersion.Current.ToString();
    var decision = ContractVersion.Judge(ctx.Request.Headers[ContractVersion.Header], minimumContract);
    if (decision.Verdict == ContractVersion.Verdict.TooOld)
    {
        ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await ctx.Response.WriteAsync(decision.Reason);

        return;
    }

    await next();
});

if (requireHttps)
{
    // Behind a TLS-terminating proxy. The proxy ALWAYS sets X-Forwarded-Proto, so a request that
    // does not carry it did not come through the proxy — a missing header is treated exactly like
    // a plaintext one. (The vault shipped the other way round once, and omitting the header was a
    // one-line bypass.) Health is the one exemption: the container's own probe has no proxy in
    // front of it and carries no secret.
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api/health")
            || ctx.Request.Headers["X-Forwarded-Proto"].ToString().Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            await next();

            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsync("HTTPS required.");
    });
}

// Resolve the caller BEFORE the rate limiter runs — the limiter partitions on the email, and
// nothing else in this pipeline populates ctx.User: the endpoints authenticate by hand and there
// is no default scheme to give it one.
app.Use(async (ctx, next) =>
{
    var principal = await Auth.AuthenticateAnyAsync(ctx, sessions, msTenant, googleEnabled, localEnabled);
    if (principal is not null)
    {
        ctx.User = principal;
    }

    await next();
});

app.UseRateLimiter();

app.MapGet("/api/health", () => Results.Json(
    new HealthDto(true, Startup.Version), ServerJsonContext.Default.HealthDto));

// Anonymous by necessity — the caller has no token yet — and safe because a client id is public by
// construction: it appears in every authorization URL and in the audience of every token this
// server accepts. The panel reads the scope from here so nobody pastes it into settings by hand.
app.MapGet("/api/client-config", () => Results.Json(
    new ClientConfigDto(msClientScope, Auth.ProvidersEnabled(msTenant, googleEnabled)),
    ServerJsonContext.Default.ClientConfigDto));

// One gate for every authorised route, replacing the hand-written check story 2.1 repeated in each
// handler. A route that is not registered with it cannot ask who is calling — see CallerFilter.
var gate = new CallerFilter(allowedDomains, allowAnyDomain, admins);
app.MapSessionEndpoints(sessions, gate);
app.MapCatalogEndpoints(catalog, slotRegistry, vendorHealth, gate);
app.MapReviewEndpoints(
    jobs,
    catalog,
    gate,
    // How long a review may WAIT for a free account before giving up, having spent nothing. Not how
    // long the vendor may take — that is the caller's own timeoutSeconds, and the two are separate
    // clocks on purpose.
    TimeSpan.FromMinutes(config.GetValue("Coai:QueueWaitMinutes", 10)));

// Anything that is not the API does not exist here.
app.MapFallback(() => Results.NotFound());

try
{
    await app.RunAsync();

    return 0;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static List<string> SplitCsv(string? value) =>
    string.IsNullOrWhiteSpace(value)
        ? []
        : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

/// <summary>Exposed so the tests can drive the real app in-process.</summary>
public partial class Program;
