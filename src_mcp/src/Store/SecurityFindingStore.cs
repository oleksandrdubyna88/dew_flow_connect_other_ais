using System.Text.Json;
using CoaiMcp.Core.Security;
using CoaiMcp.Server;
using Microsoft.Data.Sqlite;

namespace CoaiMcp.Store;

internal static class SecurityFindingStore
{
    internal static string Write(Core.Findings.Finding finding) =>
        finding.Reproduction is null && finding.AttackEvidence is null && finding.CapReason.Length == 0 && finding.AlsoSeenBy.IsDefaultOrEmpty
            ? string.Empty : JsonSerializer.Serialize(SecurityFindingDetails.Of(finding),
                ServerJsonContext.Default.SecurityFindingDetails);

    internal static SecurityFindingDetails? Read(SqliteDataReader rows)
    {
        var column = EvidenceColumn(rows);
        if (column < 0) return null; // Older databases predate the optional projection.
        var json = rows.GetString(column);
        if (json.Length == 0) return null;
        // A damaged projection cannot prevent reading the round — and must not pass for a finding that
        // never carried evidence either, so what was lost is said where the evidence would have been.
        try { return JsonSerializer.Deserialize(json, ServerJsonContext.Default.SecurityFindingDetails) ?? Damaged("it reads as null"); }
        catch (JsonException e) { return Damaged(e.Message); }
    }

    private static SecurityFindingDetails Damaged(string why) => new(null, string.Empty, [])
    {
        Unreadable = $"stored security evidence could not be read and is not shown: {why}",
    };

    private static int EvidenceColumn(SqliteDataReader rows)
    {
        for (var column = 0; column < rows.FieldCount; column++)
            if (rows.GetName(column) == "security_evidence") return column;
        return -1;
    }
}
