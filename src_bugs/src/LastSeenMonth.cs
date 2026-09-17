using System.Globalization;
using System.Text.RegularExpressions;

namespace CoaiBugs;

/// <summary>The UTC calendar month a key was last used, as <c>yyyy-MM</c>.</summary>
/// <remarks>
/// <para>A type rather than a string, because the string it holds IS the promise: a month, never a
/// day and never an hour. There is no way to make one from an instant except <see cref="Of"/>,
/// which keeps the month and discards the rest — so a column typed as this cannot quietly become a
/// timestamp.</para>
/// <para>Produced from the <see cref="TimeProvider"/> the host injects, never from the machine's
/// clock, so a test can sit either side of a UTC month boundary and assert the STORED value.</para>
/// </remarks>
public sealed partial record LastSeenMonth
{
    /// <summary>The value as stored: four digits, a dash, two digits.</summary>
    public string Value { get; }

    private LastSeenMonth(string value) => Value = value;

    /// <summary>The month an instant falls in, in UTC.</summary>
    public static LastSeenMonth Of(DateTimeOffset instant) =>
        new(instant.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture));

    /// <summary>This month, from the injected clock.</summary>
    public static LastSeenMonth Now(TimeProvider clock) => Of(clock.GetUtcNow());

    /// <summary>What a stored value must look like. The tests assert it on the value READ BACK.</summary>
    [GeneratedRegex(@"^\d{4}-\d{2}$")]
    public static partial Regex Shape();
}
