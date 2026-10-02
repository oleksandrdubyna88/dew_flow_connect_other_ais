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
        try { return JsonSerializer.Deserialize(json, ServerJsonContext.Default.SecurityFindingDetails); }
        catch (JsonException) { return null; } // A damaged projection cannot prevent reading the round.
    }

    private static int EvidenceColumn(SqliteDataReader rows)
    {
        for (var column = 0; column < rows.FieldCount; column++)
            if (rows.GetName(column) == "security_evidence") return column;
        return -1;
    }
}
