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
