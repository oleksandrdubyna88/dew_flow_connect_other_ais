using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace CoaiMcp.Server;

/// <summary>
/// The mechanics every one-JSON-file-per-record directory in the data directory repeated by hand (S4b item 13): the id
/// guard, the read under the turn, the UTC stamp and its parse, and the delete that says when it could not delete.
/// </summary>
/// <remarks>
/// <para>One copy for <see cref="QuestionConsultStore"/>, <see cref="ConsultationStore"/>, <see cref="QuestionPhaseStore"/>
/// and <see cref="EscalationRetention"/>, which each held their own — and they had begun to differ: one delete warned, one
/// did not; one parse fell back to "now", another to nothing.</para>
/// <para>What stays with each store is what is genuinely its own: its record type, its sweep's rules, and the sentence
/// naming what a delete was about.</para>
/// </remarks>
internal static partial class RecordFiles
{
    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex RecordId();

    /// <summary>
    /// Whether an id handed in is a FILE NAME here: shaped like one of ours, a GUID's 32 hex digits. Takes
    /// <c>string?</c> because ids are also read off deserialised records, where an absent one is null.
    /// </summary>
    public static bool IsRecordId(string? id) => id is not null && RecordId().IsMatch(id);

    /// <summary>A UTC instant as every record here writes it — round-trip, invariant.</summary>
    public static string Stamp(DateTime utc) => utc.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>A stamp read back as UTC, or nothing when it is absent or is no instant.</summary>
    public static DateTime? Parsed(string? stamp) =>
        DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    /// <summary>A stamp read back as UTC, or <paramref name="fallback"/>.</summary>
    public static DateTime Parse(string? stamp, DateTime fallback) => Parsed(stamp) ?? fallback;

    /// <summary>The record in <paramref name="path"/>, read under its turn — null when absent, torn, busy or half-written.</summary>
    public static T? Read<T>(string path, JsonTypeInfo<T> type)
        where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var turn = SessionTurn.Take(path);

        return ReadHeld(path, type);
    }

    /// <summary>The record in <paramref name="path"/>, read by a caller that already HOLDS its turn — the turn is not re-entrant.</summary>
    public static T? ReadHeld<T>(string path, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(SharedRead.Text(path), type);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // torn, busy, or half-written: not a record, and the next read may find it whole
        }
    }

    /// <summary>
    /// The file removed — or, when the file system refuses, false and SAID: a retention that silently never runs is a
    /// directory that grows for ever with nothing anywhere reporting it. Retried on the next sweep.
    /// </summary>
    /// <param name="what">What the file is, as the warning names it — <c>question 1f…</c>, <c>abc.json</c>.</param>
    public static bool Delete(string path, Action<string>? warn, string what)
    {
        try
        {
            File.Delete(path);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn?.Invoke($"{what} is past retention and could not be removed: {e.Message}");

            return false;
        }
    }
}
