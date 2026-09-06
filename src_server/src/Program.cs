using System.Security.Claims;
using System.Threading.RateLimiting;
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
var sessions = new SessionStore(
    dataDir,
    TimeSpan.FromDays(sessionTtlDays),
    (message, error) => reportSessionFailure?.Invoke(message, error));
builder.Services.AddSingleton(sessions);

var app = builder.Build();
var log = app.Logger;
// Wired after Build() because that is when a logger exists; the store holds the delegate, so a
// failure that happens before this point simply has nowhere to go — and nothing runs before it.
reportSessionFailure = (message, error) => log.LogWarning(error, "{Message}", message);

var swept = sessions.Sweep(DateTimeOffset.UtcNow);
if (swept > 0)
{
    log.LogInformation("swept {Count} expired session(s) at startup", swept);
}

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

app.MapSessionEndpoints(sessions, allowedDomains, allowAnyDomain, admins);

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
