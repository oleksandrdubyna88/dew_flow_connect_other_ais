// mirrored from dew_flow_creds_for_devs src_minimalapi_server/src/Program.cs (:207-278, :1336-1367)
// @ 2026-09-05. The scheme wiring and the try-each-in-turn are that server's, because the trust
// decisions in them were paid for once already; the session branch is this product's own.
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace CoaiServer;

/// <summary>Who a caller is: an identity provider's token, or a session this server issued.</summary>
public static class Auth
{
    /// <summary>Where a session's email is stashed once its token has been recognised.</summary>
    /// <remarks>
    /// A claim rather than a side channel, so that everything downstream — the rate limiter's
    /// partition, the domain check, `whoami` — reads a caller the same way whatever authenticated
    /// them. The one place that cares which it was is `DELETE /api/session`, and it asks
    /// <see cref="SessionToken"/>.
    /// </remarks>
    public const string SessionScheme = "coai-session";

    /// <summary>The raw session token of the current request, for the one endpoint that revokes it.</summary>
    public const string SessionToken = "coai-session-token";

    public static void AddSchemes(
        IServiceCollection services,
        string? msTenant,
        IReadOnlyCollection<string> msAudiences,
        bool googleEnabled,
        IReadOnlyCollection<string> googleAudiences,
        string? localKey,
        bool localEnabled)
    {
        // Concrete schemes only; there is no default and no UseAuthentication — every endpoint
        // authorises by hand, which is what lets the caller be resolved before the rate limiter.
        var builder = services.AddAuthentication();

        if (!string.IsNullOrWhiteSpace(msTenant))
        {
            builder.AddJwtBearer("Microsoft", options =>
            {
                options.MetadataAddress =
                    $"https://login.microsoftonline.com/{msTenant}/v2.0/.well-known/openid-configuration";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers =
                    [
                        $"https://login.microsoftonline.com/{msTenant}/v2.0",
                        $"https://sts.windows.net/{msTenant}/",
                    ],
                    // Startup refuses a tenant with no audiences, so this is always on in a real
                    // deployment — see Startup.Guard for why this server is stricter than the vault.
                    ValidateAudience = msAudiences.Count > 0,
                    ValidAudiences = msAudiences,
                    ValidateLifetime = true,
                };
            });
        }

        if (googleEnabled)
        {
            builder.AddJwtBearer("Google", options =>
            {
                options.MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = ["https://accounts.google.com", "accounts.google.com"],
                    // Unconditional, which is what Microsoft's amounts to: Startup.Guard refuses a
                    // Google-enabled server with no audiences, so there is no longer a state in which
                    // this could be false. It WAS conditional, and the conditional was the defect —
                    // a configuration mistake turned the check off instead of stopping the server,
                    // silently, and a Google ID token minted for any other application would then
                    // have been accepted. (Product audit of 2026-09-09, finding 9.)
                    ValidateAudience = true,
                    ValidAudiences = googleAudiences,
                    ValidateLifetime = true,
                };
            });
        }

        if (localEnabled)
        {
            // Symmetric key, no cloud dependency: the tests and an air-gapped deployment. Anyone
            // holding this key can impersonate any allowed email, which is why startup refuses a
            // short one and why it is empty wherever a real identity provider exists.
            builder.AddJwtBearer("Local", options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = "coai-local",
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(localKey!)),
                    ValidateIssuerSigningKey = true,
                };
            });
        }
    }

    /// <summary>Which sign-ins a client may offer — exactly the schemes this server has.</summary>
    public static IReadOnlyList<string> ProvidersEnabled(string? msTenant, bool googleEnabled)
    {
        var providers = new List<string>();
        if (!string.IsNullOrWhiteSpace(msTenant))
        {
            providers.Add("microsoft");
        }

        if (googleEnabled)
        {
            providers.Add("google");
        }

        return providers;
    }

    /// <summary>
    /// The first thing that recognises this caller: a session token, then each configured scheme.
    /// </summary>
    /// <remarks>
    /// The session is tried FIRST because it is a hash lookup against a file, where the others are
    /// signature validation against a downloaded key set — and it is what almost every request
    /// carries once a person has signed in.
    /// </remarks>
    public static async Task<ClaimsPrincipal?> AuthenticateAnyAsync(
        HttpContext ctx,
        SessionStore sessions,
        string? msTenant,
        bool googleEnabled,
        bool localEnabled)
    {
        if (FromSession(ctx, sessions) is { } session)
        {
            return session;
        }

        if (!string.IsNullOrWhiteSpace(msTenant) && await ctx.AuthenticateAsync("Microsoft") is { Succeeded: true } ms)
        {
            return ms.Principal;
        }

        if (googleEnabled && await ctx.AuthenticateAsync("Google") is { Succeeded: true } google)
        {
            return google.Principal;
        }

        if (localEnabled && await ctx.AuthenticateAsync("Local") is { Succeeded: true } local)
        {
            return local.Principal;
        }

        return null;
    }

    /// <summary>
    /// A caller carrying a session token this server issued.
    /// </summary>
    /// <remarks>
    /// The principal it builds carries the email as an ordinary claim, so the domain allow-list is
    /// applied to a session on EVERY request exactly as it is to a token — which is the point: a
    /// person whose domain is removed must stop being served, not keep a week of access because
    /// they signed in first. Raised on this story's plan round.
    /// </remarks>
    private static ClaimsPrincipal? FromSession(HttpContext ctx, SessionStore sessions)
    {
        if (Bearer(ctx) is not { Length: > 0 } token
            || sessions.Validate(token, DateTimeOffset.UtcNow) is not { } record)
        {
            return null;
        }

        ctx.Items[SessionToken] = token;

        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("email", record.Email), new Claim("name", record.Name)],
            SessionScheme));
    }

    private static string? Bearer(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();

        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }

    /// <summary>
    /// The caller, or null with the status already set: 401 without an identity, 403 outside the
    /// company.
    /// </summary>
    /// <remarks>
    /// The order is fixed and is the whole authorisation model: the email comes from a verified
    /// token, the domain decides, and no route carries a user id for anyone to tamper with.
    /// </remarks>
    public static (string Email, string Name)? RequireCaller(
        HttpContext ctx,
        IReadOnlyCollection<string> allowedDomains,
        bool allowAnyDomain)
    {
        var email = TokenIdentity.Email(ctx.User);
        if (email is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return null;
        }

        if (!allowAnyDomain && !TokenIdentity.DomainAllowed(email, allowedDomains))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;

            return null;
        }

        return (email, TokenIdentity.Name(ctx.User) ?? string.Empty);
    }
}
