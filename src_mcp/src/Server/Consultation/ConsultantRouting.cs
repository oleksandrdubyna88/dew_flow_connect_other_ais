using System.Text.Json;

namespace CoaiMcp.Server;

/// <summary>Which vendor row consults for one caller kind, and on which model (empty = the row's own).</summary>
public sealed record ConsultantChoice(string Vendor, string Model = "");

/// <summary>The parsed `COAI_CONSULTANTS` setting: the map, and any sentence about what could not be read.</summary>
public sealed record ConsultantsSetting(IReadOnlyDictionary<string, ConsultantChoice> Map, IReadOnlyList<string> Complaints);

/// <summary>
/// The consultant per CALLER kind — and the rule behind the shipped map.
/// </summary>
/// <remarks>
/// <para>A different vendor by default, because a stuck agent has a blind spot by definition and the
/// one model that cannot see it is the one that produced it. The same vendor is allowed as an
/// explicit choice — Fable for a Sonnet session is worth it because the model is stronger — and the
/// panel says so beside the row; the server cannot see the caller's MODEL, only its vendor.</para>
/// <para>Malformed JSON is the shipped map plus a sentence in <c>Unrecognised</c>, never half a map:
/// a consultant silently falling back to a vendor nobody chose is the failure this exists to avoid.</para>
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
                [$"COAI_CONSULTANTS could not be read ({e.Message.Split(" LineNumber:")[0].Trim()}) — the shipped consultants are in force"]);
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
