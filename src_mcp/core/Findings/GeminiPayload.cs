using System.Text.Json;

namespace CoaiMcp.Core.Findings;

/// <summary>What extraction produced — the payload, or a named reason there is none.</summary>
public abstract record ExtractOutcome
{
    public sealed record Payload(string Json) : ExtractOutcome;

    /// <summary>Nothing arrived at all. Distinct from an empty findings list, which is a clean review.</summary>
    public sealed record Empty : ExtractOutcome;

    public sealed record NoJson(string Reason) : ExtractOutcome;

    private ExtractOutcome() { }
}

/// <summary>
/// Two layers come off a Gemini answer before the schema's JSON appears.
/// </summary>
/// <remarks>
/// <para><b>1. Its own envelope.</b> <c>gemini -o json</c> returns Gemini's object (response plus
/// stats); the model's answer is the <c>response</c> field inside it.</para>
/// <para><b>2. The model's habits.</b> Asked for JSON in a prompt, it habitually fences the answer
/// in <c>```json … ```</c>, sometimes with a sentence of introduction.</para>
/// <para>Extraction is the outermost <b>balanced</b> <c>{…}</c> by brace counting from the first
/// <c>{</c> — never first-<c>{</c>-to-last-<c>}</c>, which swallows trailing prose that happens to
/// contain a brace and produces a parse error that reads like a model failure. The scan respects
/// string literals, so a brace inside a finding's text does not end the object.</para>
/// </remarks>
public static class GeminiPayload
{
    public static ExtractOutcome Extract(string raw)
    {
        var text = Unenvelope(raw).Trim();
        if (text.Length == 0)
        {
            return new ExtractOutcome.Empty();
        }

        text = Unfence(text);

        return BalancedObject(text) is { } json
            ? new ExtractOutcome.Payload(json)
            : new ExtractOutcome.NoJson("no balanced JSON object in the answer");
    }

    /// <summary>The <c>-o json</c> envelope: an object whose <c>response</c> string is the answer.</summary>
    private static string Unenvelope(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return raw;
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("response", out var response) &&
                   response.ValueKind == JsonValueKind.String
                ? response.GetString() ?? string.Empty
                : raw;
        }
        catch (JsonException)
        {
            // Not a parseable envelope — fences or prose around the payload; later layers handle it.
            return raw;
        }
    }

    /// <summary>The first fenced block's content, when the answer is fenced at all.</summary>
    private static string Unfence(string text)
    {
        var open = text.IndexOf("```", StringComparison.Ordinal);
        if (open < 0)
        {
            return text;
        }

        var contentStart = text.IndexOf('\n', open);
        if (contentStart < 0)
        {
            return text;
        }

        var close = text.IndexOf("```", contentStart, StringComparison.Ordinal);
        return close < 0 ? text[contentStart..] : text[contentStart..close];
    }

    /// <summary>The outermost balanced object from the first <c>{</c>, string-literal-aware.</summary>
    internal static string? BalancedObject(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++; // the escaped character can never open or close anything
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}' when --depth == 0:
                    return text[start..(i + 1)];
            }
        }

        return null; // opened but never closed — unbalanced is no JSON at all
    }
}
