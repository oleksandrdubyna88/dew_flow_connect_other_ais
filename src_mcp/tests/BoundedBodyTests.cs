using System.Text;
using CoaiMcp.Api;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// An answer is read up to a ceiling and no further — whether or not it declared its length.
/// </summary>
/// <remarks>
/// The api modes' own test drives the declared-length path through a real listener; these pin the
/// other one, where the count is taken while streaming, and the boundary itself.
/// </remarks>
public sealed class BoundedBodyTests
{
    [Fact]
    public async Task AnUndeclaredLength_OverTheCeiling_IsRefused()
    {
        using var content = new StreamContent(new MemoryStream(new byte[1025]));
        content.Headers.ContentLength = null;

        var text = await BoundedBody.ReadAsync(content, maxBytes: 1024, CancellationToken.None);

        text.Should().BeNull("the count passed the ceiling while streaming");
    }

    [Fact]
    public async Task ADeclaredLength_OverTheCeiling_IsRefusedUnread()
    {
        using var content = new StreamContent(new ThrowingStream());
        content.Headers.ContentLength = 2048;

        var text = await BoundedBody.ReadAsync(content, maxBytes: 1024, CancellationToken.None);

        text.Should().BeNull("a declared length over the ceiling is refused before a byte is read");
    }

    [Fact]
    public async Task ABodyAtTheCeiling_IsReadWhole()
    {
        var body = "é" + new string('x', 1022);
        using var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentLength = null;

        var text = await BoundedBody.ReadAsync(content, maxBytes: 1024, CancellationToken.None);

        text.Should().Be(body, "exactly the ceiling is inside it, and UTF-8 survives");
    }

    /// <summary>A stream that fails if anybody reads it.</summary>
    private sealed class ThrowingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new InvalidOperationException("the body was read");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the body was read");
    }
}
