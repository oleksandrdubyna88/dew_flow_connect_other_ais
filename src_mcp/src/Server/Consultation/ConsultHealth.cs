using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoaiMcp.Server;

/// <summary>The last consultation a caller kind's consultant ANSWERED.</summary>
/// <remarks>
/// Every string null-normalised in its accessor, as on <see cref="ConsultationRecord"/>: the source-generated
/// deserializer skips initialisers, and a file another build wrote may lack a field this one knows.
/// </remarks>
public sealed record ConsultHealthAnswer
{
    public string Utc { get => field ?? string.Empty; init; } = string.Empty;

    public string ConsultationId { get => field ?? string.Empty; init; } = string.Empty;

    public string CallerKind { get => field ?? string.Empty; init; } = string.Empty;

    public string Vendor { get => field ?? string.Empty; init; } = string.Empty;

    public string Runtime { get => field ?? string.Empty; init; } = string.Empty;

    public string Model { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What that turn was SENT (<see cref="ConsultationRecord.Confinement"/>).</summary>
    public string Confinement { get => field ?? string.Empty; init; } = string.Empty;
}

/// <summary>The last consultation turn a caller kind's consultant FAILED, classified, with its cure.</summary>
public sealed record ConsultHealthFailure
{
    public string Utc { get => field ?? string.Empty; init; } = string.Empty;

    public string ConsultationId { get => field ?? string.Empty; init; } = string.Empty;

    public string CallerKind { get => field ?? string.Empty; init; } = string.Empty;

    public string Vendor { get => field ?? string.Empty; init; } = string.Empty;

    public string Runtime { get => field ?? string.Empty; init; } = string.Empty;

    public string Model { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>A word of <c>shared/consult-failure-kinds.json</c>.</summary>
    public string Kind { get => field ?? string.Empty; init; } = string.Empty;

    public string What { get => field ?? string.Empty; init; } = string.Empty;

    public string Cure { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>Where the transcript was kept — a path on THIS side's filesystem.</summary>
    public string Evidence { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary><c>windows</c>, <c>wsl</c>, <c>linux</c> or <c>macos</c> — which side wrote it, for a reader on the other.</summary>
    public string Side { get => field ?? string.Empty; init; } = string.Empty;

    /// <summary>What that turn was SENT (<see cref="ConsultationRecord.Confinement"/>); empty when it never launched.</summary>
    public string Confinement { get => field ?? string.Empty; init; } = string.Empty;
}

/// <summary>What is on disk for one of a caller kind's health files: nothing, its contents, or a file that cannot be read.</summary>
/// <remarks>
/// The shape <see cref="CheckOnDisk"/> has for the check, for the same reason (doctrine 4: absent is not unreadable): a
/// torn or locked file read as null was indistinguishable from a consultant that never failed, and the tab showed it as
/// healthy (the whole-branch review of PLAN_the_consultant_works_on_every_vendor.md, 2026-10-03, A4).
/// </remarks>
public abstract record HealthOnDisk<T>
    where T : class
{
    private HealthOnDisk() { }

    /// <summary>No such outcome was ever recorded for this caller kind.</summary>
    public sealed record None : HealthOnDisk<T>;

    public sealed record Found(T Value) : HealthOnDisk<T>;

    /// <summary>The file exists and could not be read or parsed — said, never mistaken for <see cref="None"/>.</summary>
    public sealed record Unreadable(string Why) : HealthOnDisk<T>;

    /// <summary>The contents, or null for either other case — the WIRE's shape, where "unreadable" travels in its own field.</summary>
    public T? ValueOrNull => this is Found found ? found.Value : null;

    /// <summary>Why the file could not be read, or empty.</summary>
    public string WhyUnreadable => this is Unreadable unreadable ? unreadable.Why : string.Empty;
}

/// <summary>The consultant a row of <c>--consultants</c> names now — what a failure must be OF to be that row's current state.</summary>
public sealed record ConsultantIdentity(string Vendor, string Model)
{
    public bool Names(string vendor, string model) =>
        string.Equals(Vendor, vendor, StringComparison.Ordinal) && string.Equals(Model, model, StringComparison.Ordinal);
}

/// <summary>The ONE rule for whether a caller kind's last failure is still a row's current state.</summary>
/// <remarks>
/// Nothing is deleted when a consultant recovers: the failure stays on disk beside the newer answer, and the reader
/// decides. Every server reader goes through this — the survey and every refresh of <c>consultants.json</c>
/// (<see cref="ConsultantsFile"/>) — and the panel shows the server's verdict rather than deciding it a second time
/// (PLAN_the_consultant_works_on_every_vendor.md, E2.5; the whole-branch review, A3).
/// </remarks>
public static class ConsultHealth
{
    /// <summary>
    /// Whether <paramref name="failure"/> is the current state of the row that names <paramref name="row"/>: it is THAT
    /// consultant's failure (vendor and model), and that same consultant has not answered since.
    /// </summary>
    /// <remarks>
    /// <para><b>Per consultant, not per caller kind</b> (the whole-branch review, A3). The files are kept per caller kind,
    /// so a row moved from codex to claude inherited codex's spent quota as its own current failure, and an answer by
    /// codex hid a claude failure it says nothing about.</para>
    /// <para><b>The residual.</b> The answer file keeps the LAST answer only: when the failing consultant answered and
    /// another consultant answered after it (the row switched away and back), the later answer replaced the one that
    /// cleared the failure, and the failure reads current until that consultant answers again.</para>
    /// <para>An answer file that cannot be read cannot clear a failure: the failure stays current, and the row says
    /// beside it that the file is unreadable. The stamps are ISO-8601 UTC (<see cref="ConsultationStore.Stamp"/>), which
    /// order as text.</para>
    /// </remarks>
    public static bool Current(HealthOnDisk<ConsultHealthAnswer> answer, HealthOnDisk<ConsultHealthFailure> failure, ConsultantIdentity row) =>
        failure is HealthOnDisk<ConsultHealthFailure>.Found { Value: var failed }
        && row.Names(failed.Vendor, failed.Model)
        && !AnsweredSince(answer, failed);

    /// <summary>The same consultant answered at or after the failure.</summary>
    private static bool AnsweredSince(HealthOnDisk<ConsultHealthAnswer> answer, ConsultHealthFailure failed) =>
        answer is HealthOnDisk<ConsultHealthAnswer>.Found { Value: var answered }
        && new ConsultantIdentity(failed.Vendor, failed.Model).Names(answered.Vendor, answered.Model)
        && string.CompareOrdinal(failed.Utc, answered.Utc) <= 0;

    /// <summary>The answer file's contents for an answered turn of <paramref name="record"/>.</summary>
    internal static ConsultHealthAnswer AnswerOf(ConsultationRecord record, string utc) => new()
    {
        Utc = utc,
        ConsultationId = record.Id,
        CallerKind = record.CallerKind,
        Vendor = record.Vendor,
        Runtime = record.Runtime,
        Model = record.Model,
        Confinement = record.Confinement,
    };

    /// <summary>The failure file's contents for a failed turn, as it was written to the record.</summary>
    internal static ConsultHealthFailure FailureOf(ConsultationFailed failed) => new()
    {
        Utc = failed.Next.UpdatedUtc,
        ConsultationId = failed.Next.Id,
        CallerKind = failed.Next.CallerKind,
        Vendor = failed.Next.Vendor,
        Runtime = failed.Next.Runtime,
        Model = failed.Next.Model,
        Kind = failed.Failure.Kind,
        What = failed.Failure.What(failed.Next.Vendor),
        Cure = failed.Failure.Cure,
        Evidence = failed.Failure.Evidence,
        Side = Side(),
        Confinement = failed.Next.Confinement,
    };

    /// <summary>The side this server runs on, as a health file names it.</summary>
    /// <remarks>
    /// The same decision the consultant limitations are looked up by (<see cref="Runners.Platform.HostKinds"/>), so a
    /// health file and the limitation shown beside it cannot disagree about which machine this is. A platform that is
    /// none of the four now reads <c>other</c>; it used to fall through to <c>linux</c>.
    /// </remarks>
    public static string Side() => Runners.Platform.HostKinds.Word(Runners.Platform.HostKinds.Current);
}

/// <summary>
/// The two health files per caller kind: <c>&lt;dataDir&gt;/consultations/health/&lt;kind&gt;.answer.json</c>
/// and <c>&lt;kind&gt;.failure.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Why files.</b> The panel must say, per caller kind, whether its consultant worked last time
/// and what to do if it did not — and a failed turn's reason was persisted nowhere it could read. These
/// are what <c>--consultants</c> (epic 4) and the Consultant tab read.</para>
/// <para><b>Atomic and newer-only.</b> Each write goes to a temporary file in the SAME directory and is moved
/// over the target, so a reader never meets half a file — and only when the file there now is older than
/// the one being written, so a slow server finishing late cannot overwrite a newer outcome another server
/// on the same data directory already recorded. <b>The residual</b>, stated where a caller reads it: the
/// age check and the move are two operations, so two writers racing inside that window can still land the
/// older one last. Nothing here is a lock, and an outcome lost that way is replaced by the next turn.</para>
/// <para><b>And the row in <c>consultants.json</c>, at once</b> (the whole-branch review, A1): every write here also
/// rewrites that caller kind's outcome fields in the survey the OTHER side reads (<see cref="ConsultantsFile.Refresh"/>)
/// — with no re-probe, and only when the file is there. Without it a plain Windows window went on showing a WSL
/// consultant as healthy until the next survey.</para>
/// <para>An orphaned <c>*.tmp</c> older than an hour is removed by the consultation sweep (<see cref="ConsultationRetention"/>).</para>
/// <para>Best effort, like the evidence: a health file that cannot be written is logged and never fails
/// the consultation it describes.</para>
/// </remarks>
public sealed class ConsultHealthStore(string dataDir, Action<string> warn)
{
    public string Directory => ConsultHealthPaths.HealthDirectory(dataDir);

    public void Answered(ConsultHealthAnswer answer)
    {
        WriteIfNewer(PathFor(answer.CallerKind, "answer"), answer.Utc, answer, ConsultHealthJsonContext.Default.ConsultHealthAnswer);
        ConsultantsFile.Refresh(dataDir, answer.CallerKind, this, warn);
    }

    public void Failed(ConsultHealthFailure failure)
    {
        WriteIfNewer(PathFor(failure.CallerKind, "failure"), failure.Utc, failure, ConsultHealthJsonContext.Default.ConsultHealthFailure);
        ConsultantsFile.Refresh(dataDir, failure.CallerKind, this, warn);
    }

    /// <summary>The last answer for this caller kind — none, found, or a file that cannot be read.</summary>
    public HealthOnDisk<ConsultHealthAnswer> LastAnswer(string callerKind) =>
        Read(PathFor(callerKind, "answer"), ConsultHealthJsonContext.Default.ConsultHealthAnswer);

    /// <summary>The last failure for this caller kind — none, found, or a file that cannot be read.</summary>
    public HealthOnDisk<ConsultHealthFailure> LastFailure(string callerKind) =>
        Read(PathFor(callerKind, "failure"), ConsultHealthJsonContext.Default.ConsultHealthFailure);

    private string PathFor(string callerKind, string what) =>
        Path.Combine(Directory, $"{Core.Rounds.FileName.Safe(callerKind)}.{what}.json");

    private void WriteIfNewer<T>(string path, string utc, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        try
        {
            if (string.CompareOrdinal(StampIn(path), utc) >= 0)
            {
                return;
            }

            System.IO.Directory.CreateDirectory(Directory);
            // AtomicFile's temp-and-move, with a short retry when a reader holds the file a move must replace.
            Runners.Files.AtomicFile.Write(path, JsonSerializer.Serialize(value, type), ConsultHealthPaths.SharingRetry);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            warn($"consultations: the health file {path} could not be written: {e.Message}");
        }
    }

    /// <summary>The stamp of the file there now, or empty when there is none — which every stamp is newer than.</summary>
    private static string StampIn(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(SharedRead.Text(path));

            return document.RootElement.TryGetProperty("utc", out var utc) && utc.ValueKind == JsonValueKind.String
                ? utc.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>Read with the panel's sharing (<see cref="SharedRead"/>), so this reader never blocks a writer's move.</summary>
    private static HealthOnDisk<T> Read<T>(string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class
    {
        try
        {
            return File.Exists(path) ? Parsed(SharedRead.Text(path), path, type) : new HealthOnDisk<T>.None();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new HealthOnDisk<T>.Unreadable($"{path} could not be read: {e.Message}");
        }
    }

    private static HealthOnDisk<T> Parsed<T>(string text, string path, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(text, type) is { } value
                ? new HealthOnDisk<T>.Found(value)
                : new HealthOnDisk<T>.Unreadable($"{path} holds nothing");
        }
        catch (JsonException e)
        {
            return new HealthOnDisk<T>.Unreadable($"{path} does not parse: {e.Message}");
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(ConsultHealthAnswer))]
[JsonSerializable(typeof(ConsultHealthFailure))]
internal sealed partial class ConsultHealthJsonContext : JsonSerializerContext;
