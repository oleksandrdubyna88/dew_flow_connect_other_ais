using System.Text.Json;
using System.Text.Json.Serialization;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// The members of a coai-started Kimi tree that may still be alive, keyed by the sign-in and kept until a confirmed-empty re-listing clears it (DL9 of
/// <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>; A3 §6.1).
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

        AtomicFile.Write(path, JsonSerializer.Serialize(marker, KimiSurvivorJsonContext.Default.KimiSurvivorMarkerDto));
    }

    /// <summary>Reads the marker at <paramref name="path"/>.</summary>
    public static MarkerRead Read(string path)
    {
        if (path.Length == 0)
        {
            return new MarkerRead.Unreadable("no path: the sign-in folder could not be keyed, so there is no marker file to name");
        }

        try
        {
            return Parsed(File.ReadAllText(path));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new MarkerRead.Absent();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new MarkerRead.Unreadable($"{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>The report <c>--kimi-clear-marker</c> prints, as the one JSON object on stdout.</summary>
    public static string Json(ClearMarkerReport report) => JsonSerializer.Serialize(report, KimiSurvivorJsonContext.Default.ClearMarkerReport);

    /// <summary>Deletes the marker (a file already gone is fine).</summary>
    public static void Clear(string path) => File.Delete(path);

    /// <summary>The markers under <paramref name="lockRoot"/> that name <paramref name="ownedRoot"/> — at most a handful of files, and never the sign-in folder; an unreadable file is skipped here (the launch's own gate refuses it). <b>That is the safe direction:</b> a home that only an unreadable marker names can be disposed, but its sign-in is never launched on while that file stands — the gate reads the same file, cannot parse it, and is Blocked — so a skipped marker never lets a second Kimi in; at worst it lets an owned home (which holds no sign-in of its own, only the junction to it) be deleted.</summary>
    public static IReadOnlyList<StandingMarker> StandingFor(string lockRoot, string ownedRoot) =>
        [.. FilesUnder(lockRoot).Select(path => (Path: path, Read: Read(path)))
            .Where(found => found.Read is MarkerRead.Read read && SameRoot(read.Marker.OwnedRoot, ownedRoot))
            .Select(found => new StandingMarker(found.Path, ((MarkerRead.Read)found.Read).Marker))];

    /// <summary>The ordinal comparison <c>KimiHome.Alive</c> makes: trailing separators trimmed, case as written.</summary>
    internal static bool SameRoot(string a, string b) =>
        a.Length > 0 && string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.Ordinal);

    private static IEnumerable<string> FilesUnder(string lockRoot)
    {
        try
        {
            return Directory.Exists(lockRoot) ? Directory.GetFiles(lockRoot, "*" + Suffix) : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
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
