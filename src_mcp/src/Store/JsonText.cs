using System.Text.Json;

namespace CoaiMcp.Store;

/// <summary>
/// The one text-free way the caller-transcript files read a property: ONE named property of an object, and only when it is a string.
/// </summary>
/// <remarks>
/// Used by <see cref="CallerTranscriptLocator"/> (the registry's <c>sessionId</c>), <see cref="WriteTools"/> (a write's
/// <c>file_path</c>) and <see cref="TranscriptLine"/> (every field the reader takes from a line), so the rule "a non-string, a missing
/// property and a non-object are all empty" is stated once. It never enumerates the object. Split into small steps to keep each at
/// cyclomatic complexity 4 or less.
/// </remarks>
internal static class JsonText
{
    /// <summary>The string value of <paramref name="name"/>, or empty for a non-object, a missing property or a non-string.</summary>
    public static string StringProperty(JsonElement element, string name) => StringOf(Child(element, name));

    /// <summary>The named property of an object, or a default element (kind <c>Undefined</c>) for a non-object or a missing property.</summary>
    public static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child) ? child : default;

    /// <summary>True only for a property that is the JSON literal <c>true</c>; absent and anything else are false.</summary>
    public static bool Flag(JsonElement element, string name) => Child(element, name).ValueKind == JsonValueKind.True;

    /// <summary>A non-negative integer property; absent, non-numeric, fractional or negative reads 0.</summary>
    public static long Count(JsonElement element, string name)
    {
        var value = Child(element, name);

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count) ? Math.Max(count, 0) : 0;
    }

    private static string StringOf(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
