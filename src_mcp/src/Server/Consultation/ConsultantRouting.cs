using System.Text.Json;

namespace CoaiMcp.Server;

/// <summary>Which vendor row consults for one caller kind, and on which model (empty = the row's own).</summary>
public sealed record ConsultantChoice(string Vendor, string Model = "");

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
                merged[kind.Trim().ToLowerInvariant()] = new ConsultantChoice(vendor.Trim(), row.Model?.Trim() ?? string.Empty);
            }
        }

        return merged;
    }
}
