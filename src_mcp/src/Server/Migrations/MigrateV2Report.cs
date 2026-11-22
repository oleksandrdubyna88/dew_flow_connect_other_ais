using System.Text.Json.Serialization;

namespace CoaiMcp.Server.Migrations;

/// <summary>What a migration did or would do, per kind. A dry run and an apply over the same folder count the same.</summary>
internal sealed record MigrateV2Counts(
    int Sessions, int Cards, int Stores, int SettingsRemoved, int SettingsRenamed, int Unreadable, int Deferred);

/// <summary>One step: <c>kind</c> is session, card, store or settings; <c>id</c> the file, card id, store or settings key.</summary>
internal sealed record MigrateV2Step(string Kind, string Id, string Action, string Detail);

/// <summary>A held session whose person had ANSWERED the hold before the migration ran — the answer v1 never applied is overridden by good_enough.</summary>
internal sealed record MigrateV2AnsweredHold(string Session, string Card, string Decision);

/// <summary>
/// The one JSON object <c>--migrate-v2</c> prints on stdout, and what the library answers: <c>status</c> is <c>plan</c> for a dry run, then for an
/// apply <c>done</c>, <c>busy</c> (the lease is held), <c>partial</c> (objects deferred, no marker) or <c>failed</c>.
/// </summary>
internal sealed record MigrateV2Report(
    string Mode,
    bool Apply,
    string DataDir,
    string Status,
    MigrateV2Counts Counts,
    IReadOnlyList<MigrateV2AnsweredHold> AnsweredHolds,
    IReadOnlyList<MigrateV2Step> Steps)
{
    /// <summary>The refusal's sentence, for a status the caller cannot read off the counts (a wrong argument, a folder that cannot be used). Absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
}

/// <summary>The marker <c>migrations/v2.done</c>: this folder is migrated by tables with this hash.</summary>
internal sealed record MigrateV2Marker(int Schema, string TablesHash, string FinishedUtc, string Version);
