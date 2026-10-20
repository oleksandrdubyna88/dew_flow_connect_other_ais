using CoaiMcp.Core.Authors;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A <c>DateTime</c> read from a session file arrives with <c>Kind.Unspecified</c>; it is the UTC value it was written as
/// (utc-timestamps.md rule 1). <c>ToUniversalTime()</c> would move it by the reading machine's offset.
/// </summary>
public sealed class ARoundStartFromASessionFileIsReadAsUtcTests
{
    /// <summary>
    /// It cannot go red on a machine at +00:00 — the offset it guards against is then zero. The break-it (ToUniversalTime in place of
    /// SpecifyKind) is watched on this operator's machine, which is not.
    /// </summary>
    [Fact]
    public void AnUnspecifiedKind_KeepsItsHourAndGetsZeroOffset()
    {
        var read = new DateTime(2026, 10, 14, 12, 30, 0, DateTimeKind.Unspecified);

        var instant = UtcInstant.From(read);

        instant.Should().Be(new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero));
        instant!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AUtcKind_IsTheSameInstant() =>
        UtcInstant.From(new DateTime(2026, 10, 14, 12, 30, 0, DateTimeKind.Utc)).Should().Be(new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero));

    [Fact]
    public void ALocalKind_IsConvertedBecauseItsOffsetIsKnown()
    {
        var local = new DateTime(2026, 10, 14, 12, 30, 0, DateTimeKind.Local);

        UtcInstant.From(local)!.Value.UtcDateTime.Should().Be(local.ToUniversalTime());
    }

    [Fact]
    public void TheDefaultInstant_IsNotRecorded()
    {
        UtcInstant.From(default(DateTime)).Should().BeNull();
        UtcInstant.From((DateTime?)null).Should().BeNull();
    }
}
