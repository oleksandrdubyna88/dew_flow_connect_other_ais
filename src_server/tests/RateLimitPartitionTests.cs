using System.Net;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// Two people behind one proxy are two callers, not one.
/// </summary>
/// <remarks>
/// The vault server was found in exactly the opposite state on a live deployment: its limiter ran
/// before anything populated the caller, fell back to the remote address — which behind a reverse
/// proxy is the PROXY's address for everyone alive — and one busy client throttled the whole
/// company. The cure is the pipeline order, and this is the test that would have caught it.
/// Asked for by name on this story's plan round.
/// </remarks>
[Collection(ServerCollection.Name)]
public sealed class RateLimitPartitionTests
{
    [Fact]
    public async Task OneBusyCaller_DoesNotSpendAnotherCallersQuota()
    {
        using var server = new TeamServer(new Dictionary<string, string?>
        {
            ["Coai__RateLimit__PermitLimit"] = "5",
            ["Coai__RateLimit__WindowSeconds"] = "60",
        });

        using var busy = server.ClientFor($"busy@{TeamServer.Domain}");
        using var quiet = server.ClientFor($"quiet@{TeamServer.Domain}");

        // Both arrive from the same address — this is one process — so anything partitioning on
        // the address would put them in one bucket.
        var refused = false;
        for (var i = 0; i < 12; i += 1)
        {
            var response = await busy.GetAsync("/api/whoami", TestContext.Current.CancellationToken);
            refused |= response.StatusCode == HttpStatusCode.TooManyRequests;
        }

        refused.Should().BeTrue("a limit that never refuses is not a limit");

        (await quiet.GetAsync("/api/whoami", TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK,
                "the quiet caller spent nothing, and the busy one's quota is not theirs to lose");
    }
}
