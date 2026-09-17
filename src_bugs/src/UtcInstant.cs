using System.Globalization;

namespace CoaiBugs;

/// <summary>An exact instant, UTC, as stored: ISO-8601 round-trip.</summary>
/// <remarks>
/// <para>Only where the promise allows an exact time: an administrator's action, a key's creation
/// and end, a promotion. Nothing about a contributor's USE of a key is ever one of these — that is
/// <see cref="UtcMonth"/>, and the two types are what keep a column from quietly becoming the other
/// kind.</para>
/// <para>From the injected clock, never the machine's, and always UTC whatever offset it was handed.</para>
/// </remarks>
public sealed record UtcInstant
{
    /// <summary>The instant, at offset zero.</summary>
    public DateTimeOffset At { get; }

    /// <summary>What is written: <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>.</summary>
    public string Stored => At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private UtcInstant(DateTimeOffset at) => At = at.ToUniversalTime();

    /// <summary>An instant, normalised to UTC.</summary>
    public static UtcInstant Of(DateTimeOffset at) => new(at);

    /// <summary>Now, from the injected clock.</summary>
    public static UtcInstant Now(TimeProvider clock) => Of(clock.GetUtcNow());

    /// <summary>
    /// An instant read back from a column this type wrote. A value it did not write is corruption,
    /// and is said so rather than guessed at.
    /// </summary>
    internal static UtcInstant Read(string stored) =>
        DateTimeOffset.TryParseExact(
            stored, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? new(at)
            : throw new InvalidOperationException($"'{stored}' is not an instant this server wrote");

    /// <summary>Whether a string is an instant this type could have written. Answers; never throws.</summary>
    /// <remarks>
    /// For a value that arrived from OUTSIDE — half of a paging cursor a client echoed back — where
    /// a malformed one is a 400 and not corruption. <see cref="Read"/> throws because a column this
    /// server wrote cannot legitimately hold anything else; the two callers want opposite things
    /// from the same parse, which is why both exist.
    /// </remarks>
    internal static bool Reads(string candidate) =>
        DateTimeOffset.TryParseExact(
            candidate, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _);
}
