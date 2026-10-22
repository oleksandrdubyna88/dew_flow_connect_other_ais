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

    /// <summary>The words Kimi's own reset clause can begin with — the 5-hour form and the monthly form (kimi 2.3.0, 2026-10-21). The ONE list: <see cref="ClauseOf"/> searches it, nothing copies it.</summary>
    public static readonly IReadOnlyList<string> ResetOpenings = ["Your quota will reset", "Your quota will be refreshed"];

    /// <summary>The longest quoted clause, in characters.</summary>
    public const int ClauseCap = 160;

    /// <summary>The sentence without a clause: what the shim's cure and the <c>VendorDiagnosis</c> row say.</summary>
    public const string Fixed = Opening + " — " + Closing;

    /// <summary>The sentence with Kimi's reset clause quoted between its opening and its closing.</summary>
    public static string Said(string reason) => $"{Opening} — {Middle(reason)}; {Closing}";

    /// <summary>
    /// Kimi's reset clause out of <paramref name="reason"/> — from the earliest of <see cref="ResetOpenings"/> to the first period followed by whitespace — a space, a newline or a tab (so "reset in 2.5 hours."
    /// stays whole), or to a closing parenthesis it did not open (our own wrapper's, see <see cref="ClauseEnd"/>), or to the end of the text when it has neither — redacted through the product's one redactor and capped. Empty when Kimi said none.
    /// </summary>
    public static string ClauseOf(string reason) =>
        EarliestOpening(reason) is var start and >= 0
            ? Redaction.SafeText(Cut(reason, start), ClauseCap)
            : string.Empty;

    /// <summary>The index of the earliest reset opening in <paramref name="reason"/>, or -1 when it has none.</summary>
    private static int EarliestOpening(string reason) =>
        ResetOpenings.Select(opening => reason.IndexOf(opening, StringComparison.OrdinalIgnoreCase)).Where(index => index >= 0).DefaultIfEmpty(-1).Min();

    private static string Middle(string reason) => ClauseOf(reason) is { Length: > 0 } clause ? $"\"{clause}\"" : NoReset;

    private static string Cut(string reason, int start) => reason[start..ClauseEnd(reason, start)].Trim();

    /// <summary>
    /// Where the clause that begins at <paramref name="from"/> ends (exclusive): after the first period followed by whitespace of any kind — a space, a newline, a tab; a decimal point ("2.5") is not one — or
    /// BEFORE the first closing parenthesis the clause did not open, or at the end of the text. The clause is read out of a text that may be wrapped in our own punctuation — the runtime quotes the vendor's line as
    /// <c>(Kimi said: …)</c> and the shim's note as <c>(the agent's stderr: …)</c> — and a parenthesis the vendor's sentence never opened is the end of that wrapper, not a word Kimi said; one it did open
    /// (<c>reset in 2 hours (14:00 UTC).</c>) stays. Counted, never guessed: the depth is the only state.
    /// </summary>
    private static int ClauseEnd(string text, int from)
    {
        var depth = 0;
        for (var at = from; at < text.Length; at++)
        {
            depth += Depth(text[at]);
            if (depth < 0)
            {
                return at;
            }

            if (EndsASentence(text, at))
            {
                return at + 1;
            }
        }

        return text.Length;
    }

    private static int Depth(char c) => c switch { '(' => 1, ')' => -1, _ => 0 };

    private static bool EndsASentence(string text, int at) => text[at] == '.' && at + 1 < text.Length && char.IsWhiteSpace(text[at + 1]);
}
