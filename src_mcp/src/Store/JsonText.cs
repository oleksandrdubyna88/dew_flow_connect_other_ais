using System.Text.Json;

namespace CoaiMcp.Store;

/// <summary>
/// The one text-free way the caller-transcript files read a property: ONE named property of an object, and only when it is a string.
/// </summary>
/// <remarks>
/// Used by <see cref="CallerTranscriptLocator"/> (the registry's <c>sessionId</c>) and <see cref="WriteTools"/> (a write's
/// <c>file_path</c>), so the rule "a non-string, a missing property and a non-object are all empty" is stated once. It never
/// enumerates the object. Split into three steps to keep each at cyclomatic complexity 4 or less.
/// </remarks>
internal static class JsonText
{
    /// <summary>The string value of <paramref name="name"/>, or empty for a non-object, a missing property or a non-string.</summary>
    public static string StringProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object ? PropertyString(element, name) : string.Empty;

    private static string PropertyString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) ? StringOf(value) : string.Empty;

    private static string StringOf(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
