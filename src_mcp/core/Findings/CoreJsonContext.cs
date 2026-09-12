using System.Text.Json.Serialization;
using CoaiMcp.Core.Rounds;

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
/// <remarks>
/// Two shapes: what a reviewer answers, and the seed the role catalog is loaded from
/// (<see cref="RoleSeed"/>, embedded in this assembly — see <c>RoleCatalog.Builtin</c>). The
/// seed's <c>why</c> field is prose for a person and has no member here; an unmapped property is
/// skipped, which is the serializer's default and the intended reading.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true)]
[JsonSerializable(typeof(RawReview))]
[JsonSerializable(typeof(RoleSeed))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
