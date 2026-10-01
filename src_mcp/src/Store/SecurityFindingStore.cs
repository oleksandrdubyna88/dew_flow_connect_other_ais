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

    internal static string Column(SqliteConnection db) => RoundsQuery.HasColumn(db, "findings", "security_evidence")
        ? "security_evidence" : "''";

    internal static SecurityFindingDetails? Read(SqliteDataReader rows)
    {
        var json = rows.GetString(rows.GetOrdinal("security_evidence"));
        if (json.Length == 0) return null;
        try { return JsonSerializer.Deserialize(json, ServerJsonContext.Default.SecurityFindingDetails); }
        catch (JsonException) { return null; } // A damaged projection cannot prevent reading the round.
    }
}
