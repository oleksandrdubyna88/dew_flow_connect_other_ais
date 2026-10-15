using System.Text.Json.Serialization;
using CoaiMcp.Runners.Reviewers;

namespace CoaiServer;

/// <summary>The fixed codes a gauge capture is suspended or skipped with — never a vendor's sentence.</summary>
/// <remarks>
/// Story 6.1a of todo/PLAN_every_round_is_counted.md: the reader logs allow-listed numbers and fixed codes, never a raw
/// protocol message, stderr, an account id or a machine name (research/RESULTS_vendor_quota_gauges.md §2.6).
/// </remarks>
public static class GaugeCodes
{
    /// <summary>The read met an authentication error: the capture stops and stays suspended until a later launch on the slot succeeds.</summary>
    public const string AuthRefused = "auth-refused";

    /// <summary>The app-server asked its client to refresh the tokens (<c>chatgptAuthTokens/refresh</c>): never answered, capture suspended.</summary>
    public const string RefreshRequested = "refresh-requested";

    /// <summary>The app-server asked for a login: never answered, capture suspended.</summary>
    public const string LoginRequested = "login-requested";

    /// <summary>The access token's expiry is nearer than the reader's lifetime plus the margin — nothing was spawned.</summary>
    public const string TokenNearExpiry = "token-near-expiry";

    /// <summary>The platform cannot list a reader's descendants, so the tree could not be confirmed — nothing was spawned.</summary>
    public const string PlatformUnsupported = "platform-unsupported";

    /// <summary>The marker could not be written before the spawn — nothing was spawned.</summary>
    public const string MarkerNotWritten = "marker-not-written";

    /// <summary>The slot is signed out, blocked, or its capture is suspended — nothing was spawned.</summary>
    public const string SlotNotReadable = "slot-not-readable";

    /// <summary>The read answered nothing this parser could read: no result, or a result without a limit.</summary>
    public const string Unreadable = "unreadable";

    /// <summary>The reader answered an error that was not an authentication error.</summary>
    public const string ReadError = "read-error";

    /// <summary>The reader did not answer within its lifetime, or the host stopped it.</summary>
    public const string NoAnswer = "no-answer";

    /// <summary>The app-server sent a request this reader does not answer — stopped, not suspended.</summary>
    public const string UnexpectedRequest = "unexpected-request";
}

/// <summary>One reported window of one limit — identified by the limit's id and the window's duration, never by its slot in the answer.</summary>
/// <param name="LimitId">The vendor's limit identity (<c>codex</c> on the measured plan).</param>
/// <param name="Kind"><c>primary</c> or <c>secondary</c> — where the vendor put it, kept for the record.</param>
/// <param name="UsedPercent">Of the window, as reported.</param>
/// <param name="WindowDurationMins">The window's length — null when the vendor did not say, never 0.</param>
/// <param name="ResetsAtUtc">The instant the window resets, UTC (<c>O</c>) — empty when the vendor did not say.</param>
public sealed record GaugeWindowDto(string LimitId, string Kind, double UsedPercent, long? WindowDurationMins, string ResetsAtUtc);

/// <summary>
/// One reading of the vendor's own gauge, as the slot keeps it: one overwritten snapshot (story 6.1a).
/// </summary>
/// <param name="ObservedUtc">When the answer arrived.</param>
/// <param name="PlanType">The vendor's plan word (<c>prolite</c>), or empty.</param>
/// <param name="RateLimitReachedType">The vendor's word when a limit is hit, or empty when none is.</param>
/// <param name="OrdinaryUsageAllowed">The vendor's own verdict — null when it did not say; never inferred from a percentage.</param>
/// <param name="ResetCreditsAvailable">Free resets the vendor granted, or null when it did not say.</param>
/// <param name="Windows">Every window every limit reported — an absent secondary window is simply not here, never 0.</param>
/// <param name="Stale">True once a later capture failed: the numbers are the last the vendor said, not the current ones.</param>
public sealed record GaugeReadingDto(
    DateTimeOffset ObservedUtc,
    string PlanType,
    string RateLimitReachedType,
    bool? OrdinaryUsageAllowed,
    int? ResetCreditsAvailable,
    IReadOnlyList<GaugeWindowDto> Windows = null!,
    bool Stale = false)
{
    /// <summary>Never null, whatever a deserialiser did with the property.</summary>
    public IReadOnlyList<GaugeWindowDto> Windows { get; init; } = Windows ?? [];

    /// <summary>The window the vendor filed as primary for the preferred limit, or null when none was reported.</summary>
    /// <remarks>A view over <see cref="Windows"/>, never written: the reading is its windows (TheGaugeIsWrittenOnceTests).</remarks>
    [JsonIgnore]
    public GaugeWindowDto? Primary => Windows.FirstOrDefault(window => window.Kind == "primary");

    /// <summary>The window the vendor filed as secondary for the preferred limit, or null when none was reported.</summary>
    [JsonIgnore]
    public GaugeWindowDto? Secondary => Windows.FirstOrDefault(window => window.Kind == "secondary");

    /// <summary>
    /// This reading as the ledger line carries it — EVERY window of EVERY limit, as the slot keeps it (epic 6's code round:
    /// the first shape kept the preferred limit's two windows only, and a second limit never reached the derivation).
    /// </summary>
    public GaugeOnLine OnLine() =>
        new(
            ObservedUtc.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            PlanType,
            [.. Windows.Select(window => new GaugeWindowOnLine(window.LimitId, window.Kind, window.WindowDurationMins, window.UsedPercent, window.ResetsAtUtc))]);
}

/// <summary>What the gauge capture answered, for the ledger and the log — a closed set.</summary>
public abstract record GaugeCaptured
{
    private GaugeCaptured()
    {
    }

    /// <summary>A reading arrived and was stored.</summary>
    public sealed record Read(GaugeReadingDto Reading) : GaugeCaptured;

    /// <summary>Nothing was spawned, for this <see cref="GaugeCodes"/> code.</summary>
    public sealed record Skipped(string Code) : GaugeCaptured;

    /// <summary>A reader ran and no reading came of it, for this code; the previous reading is marked stale.</summary>
    public sealed record Failed(string Code) : GaugeCaptured;

    /// <summary>The read met an authentication error or a refresh request: suspended on the slot, for this code.</summary>
    public sealed record Suspended(string Code) : GaugeCaptured;

    /// <summary>The reader's tree could not be confirmed gone: the slot is blocked, for this <see cref="BlockedReasons"/> code.</summary>
    public sealed record SlotBlocked(string Code) : GaugeCaptured;

    /// <summary>What the ledger line carries: the reading when there is one, else nothing.</summary>
    public GaugeOnLine OnLine => this is Read read ? read.Reading.OnLine() : GaugeOnLine.None;
}

/// <summary>Every shape the gauge keeps on disk, for the source-generated context.</summary>
[JsonSerializable(typeof(GaugeReadingDto))]
[JsonSerializable(typeof(GaugeWindowDto))]
internal sealed partial class GaugeJsonShapes : JsonSerializerContext;
