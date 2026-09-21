using System.Globalization;

namespace CoaiMcp.Core.Notices;

/// <summary>
/// One thing this server told a person — or would have, had there been a person — as it is written down.
/// </summary>
/// <remarks>
/// <para>The extension's <c>NotificationRecord</c>, field for field: four required, the rest
/// optional, and <see cref="More"/> for what a newer build knows and this one does not. The JSON
/// names are the extension's (<c>utc</c>, <c>class</c>, <c>source</c>, <c>code</c> …); <c>class</c>
/// is a C# keyword and the property is called <see cref="Class"/>, which changes nothing on the
/// wire — <see cref="ServerNoticeLine"/> spells every name itself.</para>
/// <para><b>An optional string that is empty is absent.</b> The extension's <c>given()</c> drops an
/// empty string before a record is ever built — "a record carries no empty strings" — and its parser
/// drops one on the way back in, so a line with <c>"subject":""</c> would not survive the round trip
/// the seam test asserts. This side makes the same promise at the same place: the line skips an
/// empty optional string exactly as it skips a null one.</para>
/// <para><b>The four required fields are refused empty.</b> <c>utc</c>, <c>class</c>, <c>source</c>
/// and <c>code</c> are what let a line be placed in time and grouped; the extension's parser returns
/// nothing for a line missing any of them, so a record that would produce such a line is a
/// programming error here, not a row there.</para>
/// </remarks>
public sealed record ServerNotice
{
    /// <summary>When, as the extension writes it: <c>2026-09-16T17:05:39.812Z</c>. See <see cref="Iso"/>.</summary>
    public required string Utc { get; init => field = RequiredAfterRedaction(value, nameof(Utc)); }

    /// <summary>What the person is supposed to DO about it — <c>refusal</c>, <c>failure</c>, <c>outcome</c> …</summary>
    public required string Class { get; init => field = RequiredAfterRedaction(value, nameof(Class)); }

    /// <summary>The module that raised it. Free text, grouped on.</summary>
    public required string Source { get; init => field = RequiredAfterRedaction(value, nameof(Source)); }

    /// <summary>The KEY — a literal from <see cref="ServerNoticeCodes"/>, never prose.</summary>
    public required string Code
    {
        get;
        init => field = ServerNoticeCodes.All.Contains(value)
            ? value
            : throw new ArgumentException($"'{value}' is not one of ServerNoticeCodes. The extension groups "
                + "notices by (code, subject), so a code carrying a round number or an error "
                + "sentence mints a new key every time and nothing ever coalesces — which is the "
                + "defect the literal `code` was introduced to prevent", nameof(Code));
    }

    /// <summary>The resource this one is about, when a <c>code</c> can be about more than one.</summary>
    public string? Subject { get; init; }

    public string? Title { get; init; }

    public string? Detail { get; init; }

    public string? Cure { get; init; }

    /// <summary>The button offered, when one was.</summary>
    public string? Action { get; init; }

    /// <summary>Every button offered, in order, when there was more than one.</summary>
    public string? Offered { get; init; }

    /// <summary>What the person pressed.</summary>
    public string? Answer { get; init; }

    /// <summary>An id minted once per host start. Not the pid: pids are reused.</summary>
    public string? Run { get; init; }

    public string? Repo { get; init; }

    public string? Branch { get; init; }

    public string? Session { get; init; }

    public string? Provider { get; init; }

    public string? Role { get; init; }

    public int? Pid { get; init; }

    /// <summary>This occurrence's ordinal for <c>(code, subject)</c> within this run. Diagnostic only.</summary>
    public int? Seq { get; init; }

    /// <summary>Which bound a <c>storm</c> record marks. Nothing else carries it.</summary>
    public int? Bound { get; init; }

    /// <summary>
    /// Fields this build has a value for and no name for — strings and finite numbers only.
    /// </summary>
    /// <remarks>
    /// <para>Flattened into the line BEFORE redaction, so a field added by a newer server is cleaned
    /// exactly like the ones this build knows. Checked here, at construction, rather than at the one
    /// road to disk: a value the line could not write is a programming error at the call site, and
    /// the call site is where the exception should land.</para>
    /// <para>Three refusals, each because JavaScript would otherwise write a different line from the
    /// same record. A key that names one of the record's own fields would REPLACE that field's value
    /// in <c>{...named, ...more}</c>; a key that is a canonical array index (<c>"7"</c>) would be moved
    /// to the front of the object by JavaScript's property order; an integer past 2^53 has no exact
    /// JavaScript representation. None of these is a thing this server would write on purpose, so
    /// each is refused rather than emulated.</para>
    /// </remarks>
    public IReadOnlyDictionary<string, object>? More
    {
        get;
        init => field = value is null ? null : CheckedAndFlattened(value);
    }

    /// <summary>
    /// The instant as <c>Date.prototype.toISOString</c> writes it: millisecond precision, always <c>Z</c>.
    /// </summary>
    /// <remarks>
    /// .NET's own round-trip format carries seven fractional digits, so a caller formatting the
    /// field by hand would write a line the extension reads correctly and the parity test does not.
    /// </remarks>
    public static string Iso(DateTimeOffset at) =>
        at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// A required field, checked against what will REACH THE LINE rather than what arrived.
    /// </summary>
    /// <remarks>
    /// <para>A reviewer found the gap: <c>Utc</c> set to a single control character is non-empty
    /// here, and <see cref="Redaction.SafeText"/> reduces it to nothing before it is written — so
    /// the server emits a record whose required field is empty, and the extension's parser returns
    /// nothing for the whole line. A notice nobody can read is worse than a notice that was never
    /// written, because the count on the page says one arrived.</para>
    /// <para>Checked at CONSTRUCTION, where the call site is, rather than at the write: a record
    /// that cannot be written is a programming error and the exception belongs where it was made.</para>
    /// </remarks>
    private static string RequiredAfterRedaction(string value, string field)
    {
        var survives = Redaction.SafeText(value, Redaction.TitleLimit);

        return survives.Length == 0
            ? throw new ArgumentException($"{field} is empty once it has been redacted, and the "
                + "extension's parser returns nothing for a line missing it", field)
            : Required(value, field);
    }

    private static string Required(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                $"{field} is required: the extension's parser drops a line without it, so the notice "
                + "would be written and never read", field)
            : value;

    private static IReadOnlyDictionary<string, object> CheckedAndFlattened(
        IReadOnlyDictionary<string, object> more)
    {
        var flat = new Dictionary<string, object>(more.Count, StringComparer.Ordinal);
        foreach (var (key, value) in more)
        {
            flat.Add(CheckedKey(key), CheckedValue(key, value));
        }

        // READ-ONLY on the way out, not merely typed as such. A reviewer pointed out that an
        // `IReadOnlyDictionary` holding a `Dictionary` can be cast back and added to after
        // construction — past every check above — which would put a duplicate `code` property into
        // the line or a value the writer refuses, and lose the notice either way.
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, object>(flat);
    }

    private static string CheckedKey(string key) =>
        key switch
        {
            "" => throw Refusal("an empty key", "has no name to be read back under"),
            _ when ServerNoticeLine.NamedFields.Contains(key) =>
                throw Refusal($"\"{key}\"", "is one of the record's own fields, which JavaScript would silently overwrite"),
            _ when IsArrayIndex(key) =>
                throw Refusal($"\"{key}\"", "is a canonical array index, which JavaScript would move to the front of the line"),
            _ => key,
        };

    private static object CheckedValue(string key, object value) =>
        value switch
        {
            string => value,
            int => value,
            long whole when whole is >= -JavaScriptSafeInteger and <= JavaScriptSafeInteger => value,
            long => throw Refusal($"\"{key}\"", "is an integer past 2^53, which JavaScript cannot hold exactly"),
            double finite when double.IsFinite(finite) => value,
            double => throw Refusal($"\"{key}\"", "is not a finite number"),
            _ => throw Refusal($"\"{key}\"", $"is a {value.GetType().Name}; only strings and finite numbers keep a line flat"),
        };

    /// <summary>2^53: the largest integer JavaScript's one number type holds exactly.</summary>
    private const long JavaScriptSafeInteger = 9007199254740992;

    /// <summary>ECMA-262's array index: <c>ToString(ToUint32(s)) == s</c> and not 2^32 − 1.</summary>
    private static bool IsArrayIndex(string key) =>
        uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
        && index != uint.MaxValue
        && index.ToString(CultureInfo.InvariantCulture) == key;

    private static ArgumentException Refusal(string what, string why) =>
        new($"More: {what} {why}", nameof(More));
}
