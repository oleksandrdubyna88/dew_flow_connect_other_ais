using System.Text;
using FluentAssertions;
using Xunit;

namespace CoaiServer.Tests;

/// <summary>
/// A token set that parses but is not shaped like one is an UNKNOWN expiry with one of the file's fixed codes — never an
/// exception that reaches the capture's generic catch and marks the last reading stale.
/// </summary>
public sealed class AMalformedTokenIsAnUnknownExpiryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coai-expiry-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void AStringExp_IsNoExpiry() => ExpiryOfPayload("""{"exp":"1791964091"}""").Should().Be(new Expiry.Unknown(CodexAuthExpiry.NoExpiry));

    [Fact]
    public void AnArrayPayload_IsNoExpiry() => ExpiryOfPayload("[1]").Should().Be(new Expiry.Unknown(CodexAuthExpiry.NoExpiry));

    [Theory]
    [InlineData(253402300800)]
    [InlineData(-62135596801)]
    public void AnExpOutsideTheCalendar_IsNoExpiry(long seconds) =>
        ExpiryOfPayload($$"""{"exp":{{seconds}}}""").Should().Be(new Expiry.Unknown(CodexAuthExpiry.NoExpiry));

    [Fact]
    public void AnArrayAuthFile_IsUnreadable()
    {
        Write("[]");

        CodexAuthExpiry.Of(_dir).Should().Be(new Expiry.Unknown(CodexAuthExpiry.FileUnreadable));
    }

    [Fact]
    public void AValidExp_IsKnown()
    {
        var at = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
        Write(GaugeFixtures.AuthJson(at));

        CodexAuthExpiry.Of(_dir).Should().Be(new Expiry.Known(at));
    }

    private Expiry ExpiryOfPayload(string payload)
    {
        var jwt = $"{Base64Url("{\"alg\":\"none\"}")}.{Base64Url(payload)}.";
        Write("{\"auth_mode\":\"chatgpt\",\"tokens\":{\"access_token\":\"" + jwt + "\"},\"last_refresh\":\"2026-10-11T00:00:00Z\"}");

        return CodexAuthExpiry.Of(_dir);
    }

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.Combine(_dir, ".codex"));
        File.WriteAllText(CodexAuthExpiry.AuthFileOf(_dir), json);
    }

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
