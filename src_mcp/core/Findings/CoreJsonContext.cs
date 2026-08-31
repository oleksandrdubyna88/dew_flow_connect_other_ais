using System.Text.Json.Serialization;

namespace CoaiMcp.Core.Findings;

/// <summary>The wire shape both vendors are asked for. Nullable throughout — the model fills it.</summary>
internal sealed record RawReview(List<RawFinding>? Findings);

internal sealed record RawFinding(
    string? Severity,
    string? Category,
    string? File,
    int? Line,
    string? Title,
    string? Why,
    string? Fix);

/// <summary>Source-generated: the host publishes with reflection-free serialization.</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true)]
[JsonSerializable(typeof(RawReview))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
