using System.Net;

namespace CoaiServer;

/// <summary>
/// Which addresses may claim, through `X-Forwarded-*`, to be speaking for somebody else.
/// </summary>
/// <remarks>
/// <para>Two headers rest on this and both matter: `X-Forwarded-Proto`, which the HTTPS check reads,
/// and `X-Forwarded-For`, which becomes the rate limiter's partition when no caller is known. Trust
/// them from anybody and a direct caller can walk past the first and move themselves out of the
/// second.</para>
/// <para>The default is loopback and the private ranges — the same restriction the vault's nginx
/// applies with `set_real_ip_from`, and for the same reason: this app publishes no public port, so a
/// public address appearing as the peer means the topology is not what the deployment assumes.</para>
/// </remarks>
public static class TrustedProxies
{
    /// <summary>Loopback, then RFC 1918. Everything a container behind a host proxy can be.</summary>
    public static IReadOnlyList<IPNetwork> Default { get; } =
    [
        new(IPAddress.Parse("127.0.0.0"), 8),
        new(IPAddress.IPv6Loopback, 128),
        new(IPAddress.Parse("10.0.0.0"), 8),
        new(IPAddress.Parse("172.16.0.0"), 12),
        new(IPAddress.Parse("192.168.0.0"), 16),
    ];

    /// <summary>
    /// `10.1.2.0/24, 192.168.5.0/24` — or the default when nothing readable was configured.
    /// </summary>
    /// <remarks>
    /// All or nothing, like every other list this family parses: a half-read set of trusted networks
    /// is a trust boundary nobody wrote, and it would fail open rather than closed.
    /// </remarks>
    public static IReadOnlyList<IPNetwork> From(string? csv)
    {
        var parts = (csv ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return Default;
        }

        var networks = new List<IPNetwork>(parts.Length);
        foreach (var part in parts)
        {
            if (!IPNetwork.TryParse(part, out var network))
            {
                return Default;
            }

            networks.Add(network);
        }

        return networks;
    }
}
