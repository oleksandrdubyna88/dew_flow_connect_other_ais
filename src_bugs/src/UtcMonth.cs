using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiBugs;

/// <summary>A UTC calendar month, as <c>yyyy-MM</c> — the finest thing ever recorded about a contributor.</summary>
/// <remarks>
/// <para>A type rather than a string, because the string it holds IS the promise: a month, never a
/// day and never an hour. There is no way to make one from an instant except <see cref="Of"/>, which
/// keeps the month and discards the rest — so a column typed as this cannot quietly become a
/// timestamp. It stamps two columns: <c>api_keys.last_seen_month</c>, and since step 3
/// <c>quarantine.received_month</c>, both beside a <c>key_id</c>.</para>
/// <para>Produced from the <see cref="TimeProvider"/> the host injects, never from the machine's
/// clock, so a test can sit either side of a UTC month boundary and assert the STORED value.</para>
/// </remarks>
public sealed partial record UtcMonth
{
    /// <summary>The value as stored: four digits, a dash, two digits.</summary>
    public string Value { get; }

    private UtcMonth(string value) => Value = value;

    /// <summary>The month an instant falls in, in UTC.</summary>
    public static UtcMonth Of(DateTimeOffset instant) =>
        new(instant.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture));

    /// <summary>This month, from the injected clock.</summary>
    public static UtcMonth Now(TimeProvider clock) => Of(clock.GetUtcNow());

    /// <summary>What a stored value must look like. The tests assert it on the value READ BACK.</summary>
    /// <remarks>
    /// The month is bounded to <c>01</c>–<c>12</c>, not merely two digits. `^\d{4}-\d{2}$` accepted
    /// `2026-00` and `2026-13`, so <see cref="Read"/> answered <c>LastSeen.In</c> for a value this
    /// server could not have written — reporting corruption as an ordinary month. (CodeRabbit, #348.)
    /// </remarks>
    [GeneratedRegex(@"^\d{4}-(0[1-9]|1[0-2])$")]
    public static partial Regex Shape();

    /// <summary>
    /// A key's month read back from its column: empty is NEVER, a month is a month, and anything
    /// else is corruption, said so rather than rendered.
    /// </summary>
    /// <remarks>Never and a month are different facts, so the answer is a union and not a string a UI must know to read.</remarks>
    internal static LastSeen Read(string stored) =>
        stored.Length == 0 ? new LastSeen.Never()
        : Shape().IsMatch(stored) ? new LastSeen.In(new UtcMonth(stored))
        : throw new InvalidOperationException($"'{stored}' is not a month this server wrote");
}
