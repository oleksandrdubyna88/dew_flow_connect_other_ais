using System.Text.Json;

namespace CoaiMcp.Server;

/// <summary>
/// What consults for one caller kind: a DEFINITION — vendor, runtime, model, endpoint and CLI path,
/// the consultant's own — or, with an empty <paramref name="Runtime"/>, a LEGACY reference to a
/// reviewer row by id.
/// </summary>
/// <param name="Vendor">The id: what names the vault entry, the usage ledger and every refusal.</param>
/// <param name="Model">Empty = the runtime's own default; for a legacy reference, the row's model.</param>
/// <param name="Runtime">Which CLI answers. Empty = a legacy reference, resolved by <see cref="ConsultantResolver"/>.</param>
/// <param name="BaseUrl">For a vendor riding the codex CLI. Empty = the CLI's own endpoint.</param>
/// <param name="ExecutablePath">Where the CLI is. Empty = look it up on PATH.</param>
/// <remarks>
/// <para>Until 2026-09-15 this was <c>(Vendor, Model)</c>, and <c>Vendor</c> named a REVIEWER row: the
/// consultant borrowed that row's runtime, endpoint and CLI path, and an empty model meant the row's
/// own. So a reviewer removed took the consultant with it, a reviewer switched off refused a
/// consultation nobody had switched off, and the shipped <c>codex → claude</c> was dead on every
/// machine without a <c>claude</c> reviewer row — the opening symptom of
/// <c>PLAN_the_consultant_has_its_own_vendors</c>. The panel's <c>ConsultantChoice</c> in
/// <c>consultSettings.ts</c> made this move first (story A1); this is its twin, field for field.</para>
/// <para>The three new fields default to EMPTY, and the shipped pairs below say nothing about them —
/// deliberately. <c>new("codex")</c> is a legacy reference, and a legacy reference resolves through the
/// reviewer rows on both halves by one rule (<see cref="ConsultantResolver"/>); that is what lets a
/// settings file written before this change keep working with no rewrite, and what keeps
/// <see cref="ConsultantRouting.Shipped"/> byte-for-byte a sibling plan's to move.</para>
/// </remarks>
public sealed record ConsultantChoice(
    string Vendor,
    string Model = "",
    string Runtime = "",
    string BaseUrl = "",
    string ExecutablePath = "")
{
    /// <summary>Whether this entry says which CLI answers — a definition — rather than naming a reviewer row.</summary>
    public bool IsDefinition => Runtime.Length > 0;
}

/// <summary>
/// The parsed <c>COAI_CONSULTANTS</c> setting: the map, what was wrong with it, and whether it could
/// be read AT ALL.
/// </summary>
/// <remarks>
/// <c>Unreadable</c> exists because the complaint was not enough. A <c>COAI_CONSULTANTS</c> that does
/// not parse used to leave the SHIPPED map in force and record a sentence in the panel's
/// "unrecognised" list — so a person who configured a consultant and mistyped the JSON had their
/// working tree sent to a vendor they had not chosen, with the only warning on a page they were not
/// looking at. This class's own doctrine, three lines further down, is that exactly this must not
/// happen. (CodeRabbit, on the pull request.)
/// </remarks>
public sealed record ConsultantsSetting(
    IReadOnlyDictionary<string, ConsultantChoice> Map,
    IReadOnlyList<string> Complaints,
    bool Unreadable = false);

/// <summary>
/// The consultant per CALLER kind — and the rule behind the shipped map.
/// </summary>
/// <remarks>
/// <para>A different vendor by default, because a stuck agent has a blind spot by definition and the
/// one model that cannot see it is the one that produced it. The same vendor is allowed as an
/// explicit choice — Fable for a Sonnet session is worth it because the model is stronger — and the
/// panel says so beside the row; the server cannot see the caller's MODEL, only its vendor.</para>
/// <para>Malformed JSON is the shipped map plus a sentence in <c>Unrecognised</c>, never half a map:
/// a consultant silently falling back to a vendor nobody chose is the failure this exists to avoid —
/// which is why a setting that does not PARSE now refuses the call outright rather than leaving the
/// shipped map quietly in force. (CodeRabbit, on the pull request, against a remark that still
/// described the behaviour this change replaced.)</para>
/// </remarks>
public static class ConsultantRouting
{
    public static IReadOnlyDictionary<string, ConsultantChoice> Shipped { get; } =
        new Dictionary<string, ConsultantChoice>(StringComparer.Ordinal)
        {
            [CallerIdentity.Claude] = new("codex"),
            [CallerIdentity.Codex] = new("claude"),
            [CallerIdentity.Gemini] = new("codex"),
            [CallerIdentity.Other] = new("codex"),
        };

    /// <summary>The choice for a caller kind: the configured one, else the shipped one, else <c>other</c>'s.</summary>
    public static ConsultantChoice For(IReadOnlyDictionary<string, ConsultantChoice> map, string kind) =>
        map.TryGetValue(kind, out var chosen) && chosen.Vendor.Length > 0 ? chosen
        : Shipped.TryGetValue(kind, out var shipped) ? shipped
        : Shipped[CallerIdentity.Other];

    public static ConsultantsSetting Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ConsultantsSetting(Shipped, []);
        }

        try
        {
            var rows = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.DictionaryStringConsultantDto)
                ?? throw new JsonException("it is the JSON value null rather than an object of caller kinds");

            return new ConsultantsSetting(Merge(rows), []);
        }
        catch (JsonException e)
        {
            return new ConsultantsSetting(
                Shipped,
                [$"COAI_CONSULTANTS could not be read ({e.Message.Split(" LineNumber:")[0].Trim()}) — consulting is refused until it is fixed"],
                Unreadable: true);
        }
    }

    /// <summary>The shipped map with every well-formed configured row laid over it. Unknown kinds are kept: a newer panel may know more of them.</summary>
    private static Dictionary<string, ConsultantChoice> Merge(Dictionary<string, ConsultantDto> rows)
    {
        var merged = new Dictionary<string, ConsultantChoice>(Shipped, StringComparer.Ordinal);
        foreach (var (kind, row) in rows)
        {
            if (row?.Vendor is { } vendor && vendor.Trim().Length > 0)
            {
                // Trimmed here, ONCE, and never null past this line: the DTO's nullables are the
                // wire's shape, not the server's. A settings file from before the three new fields
                // existed carries none of them, and reads as a legacy reference — empty runtime.
                merged[kind.Trim().ToLowerInvariant()] = new ConsultantChoice(
                    vendor.Trim(),
                    Trimmed(row.Model),
                    Trimmed(row.Runtime),
                    Trimmed(row.BaseUrl),
                    Trimmed(row.ExecutablePath));
            }
        }

        return merged;
    }

    private static string Trimmed(string? value) => value?.Trim() ?? string.Empty;
}
