using System.Text.Json;
using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>What the server TOOK from a request's round fields, and what it dropped, with the reason for each.</summary>
/// <param name="Round">The client's round id in lower case — empty when the request sent none, or sent one that was dropped.</param>
/// <param name="Attempt">The client's word for the request inside its turn (<see cref="RoundFields.ClientAttempts"/>) — empty likewise.</param>
/// <param name="RemainingSeconds">
/// The seconds the client said were left on its own deadline at submit — <c>0</c> when it sent none or sent one that was
/// dropped. Positive otherwise; nothing here caps it or acts on it (story 4.3 of todo/PLAN_every_round_is_counted.md).
/// </param>
/// <param name="Dropped">One note per field that was SENT and not taken — never one for a field that was absent.</param>
public sealed record TakenRound(string Round, string Attempt, int RemainingSeconds, IReadOnlyList<FieldNoteDto> Dropped);

/// <summary>
/// The server's reading of a request's counting fields — <c>round</c>, <c>attempt</c>, <c>remainingSeconds</c> — story 4.2
/// of <c>todo/PLAN_every_round_is_counted.md</c>.
/// </summary>
/// <remarks>
/// <para><b>A counting field never fails a review.</b> The three are bound as raw JSON (<see cref="JsonElement"/>) and
/// read here, by hand: a typed <c>int?</c> or <c>string?</c> on <see cref="ReviewRequestDto"/> refuses <c>"oops"</c>,
/// <c>{}</c>, <c>1.5</c> or an overflow while the body is BOUND, before any note could be written (risk amendment 3 of
/// the plan — measured: the in-process host answered 500 for each). A malformed value is dropped with a note naming
/// the field, exactly as <see cref="ClientOptions"/> drops an effort; the review runs.</para>
/// <para><b>Absent is an old client</b>, and so is a JSON <c>null</c> — the same reading <c>effort: null</c> gets. It is
/// stored empty, never defaulted and never minted, and it gets no note: a client older than the fields would otherwise
/// be told on every review that something it never sent was dropped.</para>
/// <para>A note describes what arrived — a short value as it was, a long one by its length — and never echoes a long
/// value back: the body may be megabytes, and the note is shown to a person.</para>
/// </remarks>
public static class RoundFields
{
    /// <summary>A round id is 128 random bits written as hex — what the client mints (story 4.1).</summary>
    public const int RoundIdLength = 32;

    /// <summary>The words a CLIENT sends. <c>rotated</c> is the Team server's own and is not one of them.</summary>
    public static IReadOnlyList<string> ClientAttempts { get; } =
        [LaunchAttempts.First, LaunchAttempts.Retry, LaunchAttempts.Repair, LaunchAttempts.FollowUp];

    private const string RoundField = "round";
    private const string AttemptField = "attempt";
    private const string RemainingField = "remainingSeconds";

    /// <summary>How long a sent value may be and still be quoted in its note.</summary>
    private const int ShownLength = 40;

    public static TakenRound Take(ReviewRequestDto request)
    {
        var round = Read(request.Round, RoundField, RoundOf, string.Empty);
        var attempt = Read(request.Attempt, AttemptField, AttemptOf, string.Empty);
        var remaining = Read(request.RemainingSeconds, RemainingField, RemainingOf, 0);

        return new TakenRound(round.Value, attempt.Value, remaining.Value, [.. round.Dropped, .. attempt.Dropped, .. remaining.Dropped]);
    }

    /// <summary>One field's value, and the note when it was sent and not taken.</summary>
    private readonly record struct Reading<T>(T Value, FieldNoteDto[] Dropped);

    /// <summary>A value that was sent is read; an absent one or a JSON null is <paramref name="empty"/>, with no note.</summary>
    private static Reading<T> Read<T>(JsonElement? sent, string field, Func<JsonElement, (T Value, string Refusal)> read, T empty)
    {
        if (sent is not { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } element)
        {
            return new Reading<T>(empty, []);
        }

        var (value, refusal) = read(element);

        return refusal.Length == 0
            ? new Reading<T>(value, [])
            : new Reading<T>(empty, [new FieldNoteDto(field, refusal)]);
    }

    /// <summary>
    /// Exactly 32 hex characters, kept in lower case — a client writing upper case must not make one round two in a
    /// count. Anything else is dropped; nothing is minted in its place, because a server-made id would count a round
    /// the client never had.
    /// </summary>
    private static (string Value, string Refusal) RoundOf(JsonElement sent) =>
        sent.ValueKind == JsonValueKind.String && sent.GetString() is { Length: RoundIdLength } id && id.All(char.IsAsciiHexDigit)
            ? (id.ToLowerInvariant(), string.Empty)
            : (string.Empty, $"{Shown(sent)} is not a round id ({RoundIdLength} hexadecimal characters) — the review runs, and its lines name no round");

    /// <summary>One of the client's four words, as spelt — the ledger reads them by name, so a near miss is not one.</summary>
    private static (string Value, string Refusal) AttemptOf(JsonElement sent) =>
        sent.ValueKind == JsonValueKind.String && sent.GetString() is { } word && ClientAttempts.Contains(word, StringComparer.Ordinal)
            ? (word, string.Empty)
            : (string.Empty, $"{Shown(sent)} is not an attempt a client sends ({string.Join(", ", ClientAttempts)}) — the review runs without one");

    /// <summary>A positive whole number of seconds that fits an <see cref="int"/>: no fraction, no exponent, no string.</summary>
    private static (int Value, string Refusal) RemainingOf(JsonElement sent) =>
        sent.ValueKind == JsonValueKind.Number && sent.TryGetInt32(out var seconds) && seconds > 0
            ? (seconds, string.Empty)
            : (0, $"{Shown(sent)} is not a positive whole number of seconds — the review runs as one whose client sent none");

    /// <summary>What arrived, in a few words: a short printable value as it was, anything else by its shape.</summary>
    private static string Shown(JsonElement sent) => sent.ValueKind switch
    {
        JsonValueKind.String => Quoted(sent.GetString() ?? string.Empty),
        JsonValueKind.Number => sent.GetRawText().Length <= ShownLength ? sent.GetRawText() : $"a number of {sent.GetRawText().Length} characters",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        _ => sent.GetRawText(),
    };

    private static string Quoted(string text) =>
        text.Length <= ShownLength && !text.Any(char.IsControl) ? $"'{text}'" : $"a string of {text.Length} characters";
}
