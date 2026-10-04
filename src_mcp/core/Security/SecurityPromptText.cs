using System.Text;

namespace CoaiMcp.Core.Security;

/// <summary>What a security prompt's override text counts as.</summary>
public enum SecurityPromptTextState { None, Blank, Placeholder, Oversized, Written }

/// <summary>
/// The one rule for whether a security prompt's text can be sent: <see cref="SecurityRoster"/>'s read uses it, and
/// the extension's Security lane tab answers the same vectors (<c>shared/security-prompt-text-vectors.json</c>) so the
/// card it draws and the pairing the server runs cannot disagree (research/PLAN_the_security_tab_reads_at_a_glance.md).
/// </summary>
public static class SecurityPromptText
{
    public static SecurityPromptTextState Classify(string? text) =>
        text is null ? SecurityPromptTextState.None : ClassifyPresent(text);

    private static SecurityPromptTextState ClassifyPresent(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SecurityPromptTextState.Blank;
        if (Encoding.UTF8.GetByteCount(text) > SecurityContext.MaxPromptBytes) return SecurityPromptTextState.Oversized;
        return IsOnlyThePlaceholder(text) ? SecurityPromptTextState.Placeholder : SecurityPromptTextState.Written;
    }

    /// <summary>The operator's unfilled template: one <c>&lt;!-- OPERATOR: … --&gt;</c> comment and nothing after it.</summary>
    private static bool IsOnlyThePlaceholder(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith("<!-- OPERATOR:", StringComparison.Ordinal)
            && trimmed.IndexOf("-->", StringComparison.Ordinal) == trimmed.Length - 3;
    }
}
