using CoaiMcp.Core.Notices;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// How much of a vendor's own reason the ledger and the log keep: all of it, redacted, up to a safety cap.
/// </summary>
/// <remarks>
/// <para>Story 3.3 of todo/PLAN_every_round_is_counted.md. A refusal is read back from the ledger and the log long after
/// the round, and both used to carry the summary's cut — 160 characters of agy's
/// <c>AGY_ERROR: {"short_error":"RESOURCE_EXHAUSTED (code 429): … Resets in 110h12m4…</c>, which is exactly the half
/// the plan's measurement needed. A table cell may still shorten what it SHOWS; what is WRITTEN is whole.</para>
/// <para><b>Why a cap at all, and why this one.</b> The longest vendor reason recorded in this repository is agy's
/// structured refusal, under 300 characters (research/RESULTS_vendor_quota_gauges.md §3.1). A cap ten times past that
/// costs nothing for a real sentence and still stops a runaway — a stack trace or a whole JSON body printed on one line
/// — from making every ledger line it lands on kilobytes long. It sits just under <see cref="Redaction.DetailLimit"/>,
/// the redactor's own bound for "exception text, an HTTP body, a stack", so the two caps never disagree about a line.</para>
/// </remarks>
public static class VendorReason
{
    /// <summary>The most of one reason a ledger line or a log line keeps — far past any sentence a vendor has written.</summary>
    public const int SafetyCap = 4_000;

    /// <summary>The text whole, or its first <see cref="SafetyCap"/> characters and an ellipsis.</summary>
    public static string Capped(string text) => text.Length <= SafetyCap ? text : $"{text[..SafetyCap]}…";

    /// <summary>
    /// A reason as the ledger and the log write it: through the product's one redactor (<see cref="Redaction.SafeText"/>
    /// — control characters, key blocks, credential-named values, the shapes recognisable on sight), cut at the cap.
    /// </summary>
    public static string Written(string reason) => Redaction.SafeText(reason, SafetyCap);
}
