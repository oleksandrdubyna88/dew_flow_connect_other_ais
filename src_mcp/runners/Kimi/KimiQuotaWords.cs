using CoaiMcp.Core.Notices;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The one sentence a spent Kimi plan quota is told in (D3 of <c>todo/PLAN_a_spent_kimi_quota_is_not_a_lost_sign_in.md</c>) — pure, so the shim's cure,
/// the <c>VendorDiagnosis</c> row and the reviewer's reason are one string by one constant, not three copies that drift.
/// </summary>
/// <remarks>
/// The reset clause is a QUOTATION of what Kimi said, never parsed into a time: nothing here decides when to try again.
/// The sentence never says <c>kimi login</c> (signing in again does not help) and never says waiting is the only cure (an upgrade cures it too).
/// </remarks>
public static class KimiQuotaWords
{
    /// <summary>How the sentence opens.</summary>
    public const string Opening = "the Kimi plan quota is used up";

    /// <summary>How the sentence closes.</summary>
    public const string Closing = "signing in again will not help";

    /// <summary>What stands where the clause would, when Kimi named no reset.</summary>
    public const string NoReset = "the vendor named no reset";

    /// <summary>The words Kimi's own reset clause begins with.</summary>
    public const string ResetOpening = "Your quota will reset";

    /// <summary>The longest quoted clause, in characters.</summary>
    public const int ClauseCap = 160;

    /// <summary>The sentence without a clause: what the shim's cure and the <c>VendorDiagnosis</c> row say.</summary>
    public const string Fixed = Opening + " — " + Closing;

    /// <summary>The sentence with Kimi's reset clause quoted between its opening and its closing.</summary>
    public static string Said(string reason) => $"{Opening} — {Middle(reason)}; {Closing}";

    /// <summary>
    /// Kimi's reset clause out of <paramref name="reason"/> — from <see cref="ResetOpening"/> to the first period followed by whitespace — a space, a newline or a tab (so "reset in 2.5 hours."
    /// stays whole), or to the end of the text when it has no such period — redacted through the product's one redactor and capped. Empty when Kimi said none.
    /// </summary>
    public static string ClauseOf(string reason) =>
        reason.IndexOf(ResetOpening, StringComparison.OrdinalIgnoreCase) is var start and >= 0
            ? Redaction.SafeText(Cut(reason, start), ClauseCap)
            : string.Empty;

    private static string Middle(string reason) => ClauseOf(reason) is { Length: > 0 } clause ? $"\"{clause}\"" : NoReset;

    private static string Cut(string reason, int start)
    {
        var stop = SentenceEnd(reason, start);

        return (stop < 0 ? reason[start..] : reason[start..(stop + 1)]).Trim();
    }

    /// <summary>The index of the first period followed by whitespace of any kind — a space, a newline, a tab — from <paramref name="from"/>, or -1. A decimal point ("2.5") is not one.</summary>
    private static int SentenceEnd(string text, int from)
    {
        for (var dot = text.IndexOf('.', from); dot >= 0; dot = text.IndexOf('.', dot + 1))
        {
            if (dot + 1 < text.Length && char.IsWhiteSpace(text[dot + 1]))
            {
                return dot;
            }
        }

        return -1;
    }
}
