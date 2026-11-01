using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The members of a coai-started Kimi tree that may still be alive, keyed by the sign-in and kept until a confirmed-empty re-listing clears it (DL9 of
/// <c>research/PLAN_kimi_and_vibe_on_linux.md</c>; A3 §6.1).
/// </summary>
/// <param name="Marker">The moved <see cref="ReaderMarkerDto"/>: phase (<c>spawning</c> / <c>running</c>), the SHIM as owner, and the tree — pid and start of each member. Reused, not copied.</param>
/// <param name="OwnedRoot">The coai-owned Kimi root (<c>--home</c>) — what <c>KimiHome.Alive</c> matches on, ordinal, trailing separator trimmed.</param>
/// <param name="SignInFolder">The sign-in folder's spelling — no secret, the person's own path, for the sentence.</param>
public sealed record KimiSurvivorMarkerDto(ReaderMarkerDto Marker, string OwnedRoot, string SignInFolder)
{
    /// <summary>The marker a file that cannot be read stands for: names nothing, so it is unidentified.</summary>
    public static KimiSurvivorMarkerDto Unreadable { get; } = new(new ReaderMarkerDto(ReaderPhases.Partial, 0, default, default), string.Empty, string.Empty);
}

/// <summary>What reading a marker file came to — a closed union, so "no marker" and "a marker that cannot be read" are never one answer.</summary>
public abstract record MarkerRead
{
    private MarkerRead()
    {
    }

    /// <summary>No file at the path.</summary>
    public sealed record Absent : MarkerRead;

    /// <summary>The marker, whole.</summary>
    public sealed record Read(KimiSurvivorMarkerDto Marker) : MarkerRead;

    /// <summary>A file that is there but cannot be read or parsed: not known to be safe to delete.</summary>
    public sealed record Unreadable(string Reason) : MarkerRead;
}

/// <summary>A marker found standing under the lock root, with its path.</summary>
public sealed record StandingMarker(string Path, KimiSurvivorMarkerDto Marker);

/// <summary>
/// What a look for the markers that name one home came to — a closed union, so "none stands" and "cannot tell whether one stands" are never one answer: only the first lets a home be disposed.
/// </summary>
public abstract record StandingMarkers
{
    private StandingMarkers()
    {
    }

    /// <summary>These markers name the home (none, when the lock root holds no marker of it and every marker there was read).</summary>
    public sealed record Found(IReadOnlyList<StandingMarker> Markers) : StandingMarkers;

    /// <summary>A marker or the lock root could not be read, so whether one names the home cannot be told: the home is kept.</summary>
    public sealed record Unverifiable(string Reason) : StandingMarkers;
}

/// <summary>
/// The survivor marker FILE: <c>&lt;KimiCredentialsLock.Root&gt;/&lt;key&gt;.survivors.json</c>, written whole or not at all (<see cref="AtomicFile"/>), in three phases by
/// <see cref="KimiTreeSupervisor"/> — <c>spawning</c> before the agent starts, <c>running [agent]</c> right after, <c>running [members]</c> before any signal — and deleted only on a
/// confirmed-empty re-listing.
/// </summary>
/// <remarks>The file name's key is <c>KimiCredentialsLock.KeyFor</c>'s (through <c>MarkerPathFor</c>): this class computes no identity of a folder, and never opens the sign-in folder.</remarks>
public static class KimiSurvivorMarker
{
    /// <summary>The suffix every marker file carries.</summary>
    public const string Suffix = ".survivors.json";

    /// <summary>Writes the marker whole; throws what a file write throws (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>) — the caller names it.</summary>
    public static void Write(string path, KimiSurvivorMarkerDto marker)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        AtomicFile.Write(path, Serialized(marker));
    }

    /// <summary>
    /// Reads the marker at <paramref name="path"/> WITHOUT trusting the path (<see cref="BoundedFile"/>): a link is never followed, more than 64 KiB is never read, and a failure is said by its type and code — never by a
    /// resolved path. Each of those is <see cref="MarkerRead.Unreadable"/>, which keeps the sign-in blocked and the home kept.
    /// </summary>
    public static MarkerRead Read(string path) => path.Length == 0
        ? new MarkerRead.Unreadable("no path: the sign-in folder could not be keyed, so there is no marker file to name")
        : BoundedFile.ReadRegular(path) switch
        {
            BoundedRead.Absent => new MarkerRead.Absent(),
            BoundedRead.Text text => Parsed(text.Value),
            BoundedRead.Refused refused => new MarkerRead.Unreadable(refused.Failure),
            _ => new MarkerRead.Unreadable("a read this marker reader has no word for"),
        };

    /// <summary>The report <c>--kimi-clear-marker</c> prints, as the one JSON object on stdout.</summary>
    public static string Json(ClearMarkerReport report) => JsonSerializer.Serialize(report, KimiSurvivorJsonContext.Default.ClearMarkerReport);

    /// <summary>
    /// What the providers answer reports for the marker at <paramref name="markerPath"/> (the file <c>KimiCredentialsLock.MarkerPathFor</c> names for THIS sign-in, so never another's): nothing when no file stands or the
    /// path is empty; the path and the pids it names otherwise — empty pids for a marker that names nothing or cannot be read, which still blocks the sign-in. A marker whose own shim is still ALIVE is a turn in progress, not a
    /// stuck one (the same question the launch gate asks first, <see cref="KimiSurvivorGate.OwnerLiveness"/>), and is not reported; a shim that cannot be checked leaves it standing.
    /// </summary>
    public static IReadOnlyList<KimiMarkerReport> StandingReportFor(string markerPath, Func<ProcessIdentity, Liveness> liveness) =>
        markerPath.Length == 0
            ? []
            : Read(markerPath) switch
            {
                MarkerRead.Read read => Reported(markerPath, read.Marker, liveness),
                MarkerRead.Unreadable => [new KimiMarkerReport(markerPath, [])],
                _ => [],
            };

    private static IReadOnlyList<KimiMarkerReport> Reported(string path, KimiSurvivorMarkerDto marker, Func<ProcessIdentity, Liveness> liveness) =>
        KimiSurvivorGate.OwnerLiveness(marker, liveness) is Liveness.Alive
            ? []
            : [new KimiMarkerReport(path, [.. marker.Marker.Tree.Select(member => member.Pid)])];

    /// <summary>Deletes the marker (a file already gone is fine).</summary>
    public static void Clear(string path) => File.Delete(path);

    /// <summary>
    /// Whether the file at <paramref name="path"/> STILL holds exactly <paramref name="judged"/> — the marker a caller read, checked and is about to delete. A turn that starts in that window writes a fresh marker, and
    /// deleting it would lose the one record that keeps the sign-in blocked; a file that vanished, cannot be read or reads differently is not the judged one. Compared by the JSON this class writes, so two reads of one file are equal.
    /// </summary>
    public static bool Holds(string path, KimiSurvivorMarkerDto judged) =>
        Read(path) is MarkerRead.Read now && Serialized(now.Marker) == Serialized(judged);

    private static string Serialized(KimiSurvivorMarkerDto marker) => JsonSerializer.Serialize(marker, KimiSurvivorJsonContext.Default.KimiSurvivorMarkerDto);

    /// <summary>
    /// The markers under <paramref name="lockRoot"/> that name <paramref name="ownedRoot"/> — at most a handful of files, and never the sign-in folder. <b>Fail-closed</b> (the coai code gate, E5): when a candidate marker
    /// cannot be read, or the lock root cannot be listed, the answer is <see cref="StandingMarkers.Unverifiable"/> and <c>KimiHome.Alive</c> KEEPS the home — a marker coai cannot read may name any home, and the gate's
    /// refusal of the sign-in does not protect a home from a cleanup. A missing lock root, or a file that vanished between the listing and the read, is no marker.
    /// </summary>
    public static StandingMarkers StandingFor(string lockRoot, string ownedRoot) => FilesUnder(lockRoot) switch
    {
        LockFiles.Listed listed => Standing([.. listed.Paths.Select(path => new Candidate(path, Read(path)))], ownedRoot),
        LockFiles.Unlistable unlistable => new StandingMarkers.Unverifiable($"the lock root could not be listed ({unlistable.Why})"),
        _ => new StandingMarkers.Unverifiable("the lock root could not be judged"),
    };

    private sealed record Candidate(string Path, MarkerRead Read);

    private static StandingMarkers Standing(IReadOnlyList<Candidate> candidates, string ownedRoot)
    {
        var named = candidates.Where(candidate => candidate.Read is MarkerRead.Read read && SameRoot(read.Marker.OwnedRoot, ownedRoot))
            .Select(candidate => new StandingMarker(candidate.Path, ((MarkerRead.Read)candidate.Read).Marker)).ToList();

        return named.Count > 0 ? new StandingMarkers.Found(named) : Unverified(candidates);
    }

    /// <summary>None names the home: unless a marker could not be read — then it might, and nothing can be said.</summary>
    private static StandingMarkers Unverified(IReadOnlyList<Candidate> candidates) =>
        candidates.FirstOrDefault(candidate => candidate.Read is MarkerRead.Unreadable) is { Read: MarkerRead.Unreadable unreadable } stuck
            ? new StandingMarkers.Unverifiable($"{System.IO.Path.GetFileName(stuck.Path)} could not be read ({unreadable.Reason})")
            : new StandingMarkers.Found([]);

    /// <summary>The ordinal comparison <c>KimiHome.Alive</c> makes: trailing separators trimmed, case as written.</summary>
    internal static bool SameRoot(string a, string b) =>
        a.Length > 0 && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.Ordinal);

    private abstract record LockFiles
    {
        private LockFiles()
        {
        }

        public sealed record Listed(IReadOnlyList<string> Paths) : LockFiles;

        public sealed record Unlistable(string Why) : LockFiles;
    }

    private static LockFiles FilesUnder(string lockRoot)
    {
        try
        {
            return new LockFiles.Listed(Directory.Exists(lockRoot) ? Directory.GetFiles(lockRoot, "*" + Suffix) : []);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new LockFiles.Unlistable(e.GetType().Name);
        }
    }

    private static MarkerRead Parsed(string text)
    {
        try
        {
            return JsonSerializer.Deserialize(text, KimiSurvivorJsonContext.Default.KimiSurvivorMarkerDto) is { Marker: not null } dto
                ? new MarkerRead.Read(dto)
                : new MarkerRead.Unreadable("not a Kimi survivor marker: no marker object in it");
        }
        catch (JsonException e)
        {
            return new MarkerRead.Unreadable($"not valid JSON ({e.Message})");
        }
    }
}

/// <summary>The marker's JSON, source-generated (no reflection: this binary is Native-AOT), camelCase.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(KimiSurvivorMarkerDto))]
[JsonSerializable(typeof(ClearMarkerReport))]
internal sealed partial class KimiSurvivorJsonContext : JsonSerializerContext;

/// <summary>What <c>--kimi-clear-marker</c> prints on stdout: one JSON object for the panel.</summary>
public sealed record ClearMarkerReport(string Path, string Outcome, int Exit, IReadOnlyList<ClearMarkerMember> Members);

/// <summary>One member of a marker and what the check found: <c>alive</c>, <c>gone</c> or <c>unknown</c>, with the reason.</summary>
public sealed record ClearMarkerMember(int Pid, DateTime StartedUtc, string Liveness, string Detail);

/// <summary>What the providers answer reports for a Kimi row whose sign-in has a survivor marker standing: where the marker is (a path <c>--kimi-clear-marker</c> accepts) and the pids it names.</summary>
public sealed record KimiMarkerReport(string Path, IReadOnlyList<int> Pids);
