using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CoaiMcp.Runners.Files;

namespace CoaiMcp.Server;

/// <summary>
/// <c>&lt;dataDir&gt;/consultations/health/consultants.json</c> — the survey <c>--consultants</c> takes, written for the
/// OTHER side, and its rows' outcome fields kept current by every recorded outcome.
/// </summary>
/// <remarks>
/// <para><b>Why a recorded outcome rewrites it</b> (the whole-branch review of PLAN_the_consultant_works_on_every_vendor.md,
/// 2026-10-03, A1). A plain Windows window reads a WSL store's survey through <c>coai.alsoWatchDataDirectories</c> and
/// cannot run that side's binary, so the survey is all it has — and a survey was written only by <c>--consultants</c>
/// and once at server start, so a failure recorded in between never reached it. <see cref="Refresh"/> rewrites ONE
/// row's <c>lastAnswer</c>, <c>lastFailure</c>, <c>failureCurrent</c> and <c>healthUnreadable</c> from the health files
/// and leaves every other byte of the file's meaning as it was: the CLI facts are the probe's, taken at the survey's
/// <c>utc</c>, and nothing here re-probes. A file that is absent or unreadable is left alone — only a survey makes one.</para>
/// <para><b>Why the survey re-reads before it writes.</b> A survey's probes take up to a minute; an outcome recorded
/// while they ran would be erased by the survey's own write if its rows carried the health files as they were when it
/// began. <see cref="Written"/> re-reads every row's health files immediately before the atomic move.</para>
/// <para><b>The residual window</b>, stated where a caller reads it: the read of the health files and the move are two
/// operations, and nothing here is a lock. An outcome recorded inside that window — milliseconds — can be overwritten
/// by a survey or by another kind's refresh that read the file before it; the health files themselves are never lost,
/// and the next outcome or survey puts the row right.</para>
/// <para>The server stays the one source of truth: <see cref="ConsultantsReport.Outcomes"/> computes the fields for the
/// survey and for every refresh alike, and the panel shows them without deciding anything again.</para>
/// </remarks>
internal static class ConsultantsFile
{
    /// <summary>
    /// Writes <paramref name="answer"/> atomically — its rows' outcome fields re-read from the health files immediately
    /// before the move — and answers the text written, which is what <c>--consultants</c> prints.
    /// </summary>
    /// <remarks>A file that cannot be written is said through <paramref name="warn"/> and does not cost the answer.</remarks>
    public static string Written(string dataDir, ConsultantsAnswer answer, Action<string> warn)
    {
        var root = JsonSerializer.SerializeToNode(answer, ConsultantsJsonContext.Default.ConsultantsAnswer)!.AsObject();
        var health = new ConsultHealthStore(dataDir, warn);
        foreach (var row in Rows(root))
        {
            WithOutcomes(row, health);
        }

        var text = Text(root);
        Moved(dataDir, text, warn);

        return text;
    }

    /// <summary>
    /// Rewrites <paramref name="callerKind"/>'s outcome fields in the file from the health files — or does nothing when
    /// the file is absent, unreadable, or has no such row.
    /// </summary>
    public static void Refresh(string dataDir, string callerKind, ConsultHealthStore health, Action<string> warn)
    {
        if (Read(ConsultHealthPaths.ConsultantsFile(dataDir)) is not { } root
            || Rows(root).FirstOrDefault(row => TextIn(row, "callerKind") == callerKind) is not { } row)
        {
            return;
        }

        WithOutcomes(row, health);
        Moved(dataDir, Text(root), warn);
    }

    /// <summary>The file as an object with a <c>consultants</c> array — or null when it is absent or will not read as one.</summary>
    private static JsonObject? Read(string path)
    {
        try
        {
            return File.Exists(path) && JsonNode.Parse(SharedRead.Text(path)) is JsonObject root && root["consultants"] is JsonArray
                ? root
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<JsonObject> Rows(JsonObject root) =>
        root["consultants"] is JsonArray rows ? rows.OfType<JsonObject>() : [];

    /// <summary>The row's outcome fields, from the files as they are now, for the consultant the row names.</summary>
    private static void WithOutcomes(JsonObject row, ConsultHealthStore health)
    {
        var kind = TextIn(row, "callerKind");
        var said = ConsultantsReport.Outcomes(health.LastAnswer(kind), health.LastFailure(kind), new ConsultantIdentity(TextIn(row, "vendor"), TextIn(row, "model")));
        row["lastAnswer"] = said.LastAnswer is { } answer ? JsonSerializer.SerializeToNode(answer, ConsultantsJsonContext.Default.ConsultHealthAnswer) : null;
        row["lastFailure"] = said.LastFailure is { } failure ? JsonSerializer.SerializeToNode(failure, ConsultantsJsonContext.Default.ConsultHealthFailure) : null;
        row["failureCurrent"] = said.FailureCurrent;
        row["healthUnreadable"] = said.HealthUnreadable;
    }

    private static string TextIn(JsonObject row, string name) =>
        row[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    /// <summary>Indented, as the survey always was — through a writer, which needs no reflection under Native AOT.</summary>
    private static string Text(JsonNode root)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            root.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Moved(string dataDir, string text, Action<string> warn)
    {
        var path = ConsultHealthPaths.ConsultantsFile(dataDir);
        try
        {
            Directory.CreateDirectory(ConsultHealthPaths.HealthDirectory(dataDir));
            AtomicFile.Write(path, text, ConsultHealthPaths.SharingRetry);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warn($"consultants: {path} could not be written: {e.Message}");
        }
    }
}
