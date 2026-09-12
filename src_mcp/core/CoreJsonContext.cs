using System.Text.Json.Serialization;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Core;

/// <summary>
/// Source-generated: the host publishes with reflection-free serialization.
/// </summary>
/// <remarks>
/// <para>One context for this assembly, and it lives at the ROOT of it rather than inside one of
/// its sub-namespaces. It used to sit in <c>Findings</c>, which was true while findings were the
/// only thing here that crossed a wire; the moment the role catalog needed the same serializer,
/// <c>Findings</c> had to import <c>Rounds</c> while <c>Rounds</c> imported <c>Findings</c> back —
/// a mutual dependency between two halves of the core that have nothing to say to each other.
/// Raised on this story's second code round.</para>
/// <para>From the root, the arrows all point one way: this file knows both sub-namespaces, and
/// neither knows the other. Nothing else changed — C# resolves a type from an enclosing namespace,
/// so <c>Findings</c> and <c>Rounds</c> reach <c>CoreJsonContext</c> with no <c>using</c> at all.</para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, AllowTrailingCommas = true)]
[JsonSerializable(typeof(RawReview))]
[JsonSerializable(typeof(RoleSeed))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
