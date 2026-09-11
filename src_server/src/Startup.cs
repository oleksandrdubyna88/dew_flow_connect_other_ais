using System.Reflection;

namespace CoaiServer;

/// <summary>
/// The states this server refuses to start in, and why each one is worse than not starting.
/// </summary>
/// <remarks>
/// Mirrored in spirit from the vault server's own guards (`Program.cs:322-349`) and stricter in one
/// place — see the audiences guard. A misconfiguration that starts is a server that answers 401 or
/// 403 to everything with nothing in the log connecting the two.
/// </remarks>
public static class Startup
{
    public static string Version =>
        typeof(Startup).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public static void Guard(
        string? msTenant,
        IReadOnlyCollection<string> msAudiences,
        bool googleEnabled,
        IReadOnlyCollection<string> googleAudiences,
        bool localEnabled,
        string? localKey,
        IReadOnlyCollection<string> allowedDomains,
        bool allowAnyDomain,
        string dataDir,
        int sessionTtlDays)
    {
        if (string.IsNullOrWhiteSpace(msTenant) && !googleEnabled && !localEnabled)
        {
            throw new InvalidOperationException(
                "No authentication scheme configured — set Auth:Microsoft:Tenant, Auth:Google:Enabled, "
                + "or Auth:Local:SigningKey. Refusing to start a server that would 401 every request.");
        }

        // STRICTER than the vault, deliberately. That server only warns, because it predates its own
        // app registration and had deployments in the field without one. This server mints SESSIONS:
        // without an audience check, any Microsoft token issued to anyone in the tenant for any
        // third-party application would buy a seven-day pass to the company's paid subscriptions.
        // Raised on this story's plan round, and the registration exists from day one, so there is
        // nothing to be lenient about.
        if (!string.IsNullOrWhiteSpace(msTenant) && msAudiences.Count == 0)
        {
            throw new InvalidOperationException(
                "Auth:Microsoft:Tenant is set but Auth:Microsoft:Audiences is empty, so any token from "
                + "the tenant would be accepted — including one minted for another application. Set it "
                + "to '<client-id>,api://<client-id>' from the app registration.");
        }

        // The SAME defect, one identity provider over, and it went unnoticed because the paragraph
        // above was written about Microsoft and nobody carried it across. A Google ID token is handed
        // to every application a person signs into with Google, so without an audience check any
        // third-party app a colleague ever used could present that colleague here — the issuer, the
        // signature, the lifetime, the allowed domain and email_verified are all still checked, which
        // is why this is a privilege path rather than anonymous access, and why it reads as safe.
        // Found by the product audit of 2026-09-09 (finding 9) rather than by anybody using it.
        if (googleEnabled && googleAudiences.Count == 0)
        {
            throw new InvalidOperationException(
                "Auth:Google:Enabled is true but Auth:Google:Audiences is empty, so a Google token "
                + "minted for ANY application would be accepted here. Set it to the OAuth client id(s) "
                + "this server is the audience for.");
        }

        // The local scheme signs identities with a SHARED SECRET: whoever holds the key can present
        // any email an allowed domain covers, with no Microsoft and no Google. That is exactly what
        // makes it right for the tests and for an air-gapped deployment — and an identity bypass
        // beside a real provider, because the company's own sign-in becomes optional for anyone who
        // has the key.
        //
        // `Auth.cs` has said "it is empty wherever a real identity provider exists" since day one.
        // That was a convention, and a convention does not fail; the client/server contract suite
        // made this scheme a routine, documented path, so its reviewer asked for the sentence to
        // become a start-up failure. `coai.remsoft.dev` was checked when this was written and
        // carries no local key: this closes a door rather than an incident.
        if (localEnabled && (!string.IsNullOrWhiteSpace(msTenant) || googleEnabled))
        {
            throw new InvalidOperationException(
                "Auth:Local:SigningKey is set alongside a real identity provider. The local scheme "
                + "signs identities with a shared secret, so anyone holding that key could sign in "
                + "as anyone in an allowed domain without going near Microsoft or Google. Remove "
                + "the key, or remove the provider — this server will not run both.");
        }

        if (allowedDomains.Count == 0 && !allowAnyDomain)
        {
            throw new InvalidOperationException(
                "Coai:AllowedDomains is empty. Set it to your company domain(s), or set "
                + "Coai:AllowAnyDomain=true to explicitly run without a domain boundary.");
        }

        if (localEnabled && System.Text.Encoding.UTF8.GetByteCount(localKey!) < 32)
        {
            // HMAC-SHA256 needs 256 bits. A shorter key registers without complaint, the host
            // starts, health reports OK — and every single request is rejected with 401 with
            // nothing in the log to connect the two.
            throw new InvalidOperationException(
                $"Auth:Local:SigningKey is {System.Text.Encoding.UTF8.GetByteCount(localKey!)} bytes; "
                + "HMAC-SHA256 requires at least 32. Generate one with: openssl rand -base64 48");
        }

        if (sessionTtlDays <= 0)
        {
            throw new InvalidOperationException(
                $"Coai:SessionTtlDays is {sessionTtlDays}; a session must outlive its own issuing. "
                + "Every token would be expired before it reached the caller.");
        }

        GuardDataDir(dataDir);
    }

    /// <summary>
    /// The data directory must exist and be writable, checked by WRITING — sessions live there, and
    /// a server that cannot store one hands out credentials it will refuse a second later.
    /// </summary>
    private static void GuardDataDir(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            var probe = Path.Combine(dataDir, $".writable-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Coai:DataDir '{dataDir}' is not writable ({e.Message}). On a fresh host a "
                + "bind-mounted directory belongs to root while this runs as uid 10001 — the compose "
                + "stack's init service is what chowns it.");
        }
    }
}

/// <summary>This binary asking its own health endpoint — the container has no curl.</summary>
public static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await http.GetAsync($"http://127.0.0.1:{port}/api/health");

            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return 1;
        }
    }
}
