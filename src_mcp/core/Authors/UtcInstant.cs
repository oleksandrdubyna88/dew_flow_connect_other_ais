namespace CoaiMcp.Core.Authors;

/// <summary>
/// A session file's <c>DateTime</c> as the UTC instant it was written as (utc-timestamps.md rule 1): one place, so no caller
/// reaches for <c>ToUniversalTime()</c> on a value that already IS universal and moves it by the reading machine's offset.
/// </summary>
public static class UtcInstant
{
    /// <summary>
    /// <c>default</c> means <i>not recorded</i> and is null, never an instant. An unspecified kind is the UTC value it was written as
    /// (<c>SpecifyKind</c>); only a local kind, whose offset is known, is converted.
    /// </summary>
    public static DateTimeOffset? From(DateTime value) =>
        value.Ticks == 0 ? null : new DateTimeOffset(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DateTimeOffset? From(DateTime? value) => value is { } instant ? From(instant) : null;
}
