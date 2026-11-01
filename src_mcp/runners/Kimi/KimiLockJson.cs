using System.Text.Json.Serialization;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// What a sign-in's gate file records about itself, once, on its first take (A2 §2.5, F15 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): the IDENTITY it was keyed by and the folder
/// spelling the launch used — no secret, the folder is the person's own — so a sweep can tell, long after, that the folder is gone or is another folder now.
/// </summary>
public sealed record KimiLockRecord(string Identity, string Folder, DateTimeOffset WrittenUtc);

/// <summary>The source-generated serializer of <see cref="KimiLockRecord"/>: this binary is Native-AOT, so there is no reflection to fall back on.</summary>
[JsonSerializable(typeof(KimiLockRecord))]
internal sealed partial class KimiLockJsonContext : JsonSerializerContext;
