using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Server.Migrations;

/// <summary>
/// What the v2 migration moves out of a data folder, as DATA: the settings keys a release removed or renamed, and the stores it archives
/// (E1.S4 of <c>v2/epoch-1/e1-gate-simplify/stories/s4-migrate-v2/SPEC.md</c>, risk 1).
/// </summary>
/// <remarks>
/// <para>Rows, not code, because later stories of the epoch add to them: E3 removes the cadence and the question phase, and each removal adds its
/// rows and a fixture. <see cref="Hash"/> goes into the <c>migrations/v2.done</c> marker, so a build whose tables differ from the marker's runs the
/// migration again on the next start — and the run is idempotent by object, so a second run over a migrated folder changes nothing.</para>
/// <para>Settings keys are the flat <c>COAI_*</c> names of <c>settings.json</c>, compared without case (the reader's own dictionary is).</para>
/// </remarks>
/// <param name="RemovedSettings">Keys no v2 build reads: moved out of <c>settings.json</c> into <c>settings.v1-removed.json</c>.</param>
/// <param name="RenamedSettings">Keys a release renamed, old then new: rewritten under the new name; when both exist the new one wins.</param>
/// <param name="Stores">Directories under the data folder that v2 archives to <c>migrations/v1-archive/</c> by name.</param>
internal sealed record MigrationV2Tables(
    IReadOnlyList<string> RemovedSettings,
    IReadOnlyList<(string Old, string New)> RenamedSettings,
    IReadOnlyList<string> Stores)
{
    /// <summary>
    /// What this build ships with. Removed by E1.S1: the escalation wait and the exhaustion policy. Removed by E3.S2: the autonomous switch, the operator commands and
    /// their models, the split-plan, split-with-Fable, gate-per, stop-local-when-quiet, deal-plan, deal-code, rotate-prompts and prompts-per-round settings, the security lane,
    /// the question consultant's mode and free batches, and the cadence. Renamed by E3.S3: <c>COAI_CONSULT_*</c> to <c>COAI_CONS_*</c> and <c>COAI_QCONSULT_*</c> to
    /// <c>COAI_CONSASK_*</c> — every key the server reads under the new name; the server still reads the old one while the new one is unset, so a folder nobody migrated
    /// keeps working, and one that was migrated has only the new names.
    /// </summary>
    public static MigrationV2Tables Default { get; } = new(
        [
            "COAI_ON_EXHAUSTED",
            "COAI_ESCALATION_MINUTES",
            "COAI_ESCALATION_SECONDS",
            "COAI_AUTONOMOUS",
            "COAI_COMMANDS",
            "COAI_COMMAND_MODELS",
            "COAI_SPLIT_PLAN",
            "COAI_SPLIT_WITH_FABLE",
            "COAI_GATE_PER",
            "COAI_STOP_LOCAL_WHEN_QUIET",
            "COAI_DEAL_PLAN",
            "COAI_DEAL_CODE",
            "COAI_ROTATE_PROMPTS",
            "COAI_PROMPTS_PER_ROUND",
            "COAI_SECURITY_LANE",
            "COAI_QCONSULT_MODE",
            "COAI_QCONSULT_FREE_BATCHES",
            "COAI_CADENCE_MODE",
            "COAI_CADENCE_EVERY",
            "COAI_CADENCE_RISK_THRESHOLD",
            "COAI_CADENCE_RISK_MAX",
        ],
        [
            ("COAI_CONSULT_ENABLED", "COAI_CONS_ENABLED"),
            ("COAI_CONSULT_TURNS", "COAI_CONS_TURNS"),
            ("COAI_CONSULT_CALLS_PER_SESSION", "COAI_CONS_CALLS_PER_SESSION"),
            ("COAI_CONSULT_IDLE_MINUTES", "COAI_CONS_IDLE_MINUTES"),
            ("COAI_QCONSULT_ENABLED", "COAI_CONSASK_ENABLED"),
            ("COAI_QCONSULT_ROWS", "COAI_CONSASK_ROWS"),
            ("COAI_QCONSULT_PROMPTS", "COAI_CONSASK_PROMPTS"),
            ("COAI_QCONSULT_ROOTS", "COAI_CONSASK_ROOTS"),
            ("COAI_QCONSULT_ROW_MINUTES", "COAI_CONSASK_ROW_MINUTES"),
            ("COAI_QCONSULT_QUESTIONS_PER_SESSION", "COAI_CONSASK_QUESTIONS_PER_SESSION"),
        ],
        ["qphase", "cadence"]);

    /// <summary>A fingerprint of the rows, in the marker: sixteen hex digits of the SHA-256 of their canonical text.</summary>
    public string Hash
    {
        get
        {
            var text = string.Join(
                '\n',
                RemovedSettings.Order(StringComparer.Ordinal).Select(key => "removed:" + key)
                    .Concat(RenamedSettings.Order().Select(rename => $"renamed:{rename.Old}>{rename.New}"))
                    .Concat(Stores.Order(StringComparer.Ordinal).Select(store => "store:" + store)));

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
        }
    }
}
