// mirrored from dew_flow_creds_for_devs src_minimalapi_server/tests/VaultServer.cs @ 2026-09-05
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Hosts the real server in-process on a throwaway data directory.
/// </summary>
/// <remarks>
/// <para>Configuration goes through PROCESS ENVIRONMENT VARIABLES rather than
/// <c>WithWebHostBuilder</c>, and that is not a style choice: <c>Program.cs</c> reads
/// <c>builder.Configuration</c> before <c>Build()</c>, so anything a factory adds during
/// <c>ConfigureWebHost</c> lands too late to be seen. Environment variables are already in the
/// builder's configuration by then — the vault server learned this and its harness is copied with
/// the lesson.</para>
/// <para>The consequence is that the variables are process-global, which is why every test class
/// joins one non-parallel collection and each test owns its own server for its duration.</para>
/// </remarks>
internal sealed class TeamServer : WebApplicationFactory<Program>
{
    /// <summary>32+ bytes: HMAC-SHA256 refuses a shorter key, and so does this server's startup.</summary>
    public const string LocalSigningKey = "coai-server-test-signing-key-32b!!";

    public const string Domain = "example.com";

    private readonly Dictionary<string, string?> _restore = [];

    public string DataDir { get; }

    public TeamServer(IDictionary<string, string?>? overrides = null)
    {
        DataDir = Path.Combine(Path.GetTempPath(), "coai-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDir);

        var settings = new Dictionary<string, string?>
        {
            ["Coai__DataDir"] = DataDir,
            ["Coai__AllowedDomains"] = Domain,
            ["Coai__AllowAnyDomain"] = "false",
            ["Coai__RequireForwardedHttps"] = "false",
            ["Coai__Admins"] = $"boss@{Domain}",
            ["Coai__SessionTtlDays"] = "7",
            ["Auth__Local__SigningKey"] = LocalSigningKey,
            ["Auth__Microsoft__Tenant"] = "",
            ["Auth__Microsoft__Audiences"] = "",
            ["Auth__Google__Enabled"] = "false",
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }
        }

        foreach (var (key, value) in settings)
        {
            _restore[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    /// <summary>A client that presents <paramref name="email"/>'s token on every call.</summary>
    public HttpClient ClientFor(string email, string? name = null) =>
        WithToken(Tokens.For(email, LocalSigningKey, name));

    public HttpClient WithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        foreach (var (key, value) in _restore)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            Directory.Delete(DataDir, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that will not delete is not a test failure.
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ServerCollection
{
    /// <summary>
    /// One at a time: the harness sets process-global environment variables, so two servers
    /// starting at once would configure each other.
    /// </summary>
    public const string Name = "coai-server";
}
