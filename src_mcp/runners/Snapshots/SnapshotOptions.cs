namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// The ceilings a snapshot is made under, and the names it leaves out.
/// </summary>
/// <remarks>
/// <para><b>The defaults are read off this repository</b> (S1's measurement, 2026-10-12): 2 737 files, a 38.8 MB tar,
/// 5.7 s to copy. Each ceiling is an order of magnitude above that — 50 000 files, 512 MB, 60 s — so a repository
/// several times this size still copies, and a build-output directory somebody forgot to ignore is refused with the
/// number rather than copied for a minute into a reviewer's working folder.</para>
/// <para>A ceiling is a refusal with the number, never a partial copy: the file and byte counts are known from the
/// listing before a byte is copied, and the time ceiling deletes what it had copied so far.</para>
/// </remarks>
public sealed record SnapshotOptions
{
    /// <summary>512 MB — thirteen times this repository's tar.</summary>
    public const long DefaultMaxBytes = 512L * 1024 * 1024;

    /// <summary>50 000 files — eighteen times this repository's count.</summary>
    public const int DefaultMaxFiles = 50_000;

    /// <summary>A minute — ten times this repository's copy.</summary>
    public static readonly TimeSpan DefaultMaxElapsed = TimeSpan.FromSeconds(60);

    public static SnapshotOptions Default { get; } = new();

    public long MaxBytes { get; init; } = DefaultMaxBytes;

    public int MaxFiles { get; init; } = DefaultMaxFiles;

    /// <summary>The whole making — the listing, the planning and the copy — measured from the call.</summary>
    public TimeSpan MaxElapsed { get; init; } = DefaultMaxElapsed;

    /// <summary>The names left out at every depth; <see cref="SnapshotExclusions.Default"/> covers both vendors.</summary>
    public SnapshotExclusions Exclusions { get; init; } = SnapshotExclusions.Default;

    /// <summary>
    /// A seam for the tests, called with the attempt number (1, then 2 on the retry) and the snapshot-relative path
    /// AFTER a file's size and time were read and BEFORE it is copied — the one moment a change must be seen — so a
    /// test can change the file under the copy and see the retry, then the refusal. Does nothing by default and is
    /// never called by the product's own code paths for any other reason.
    /// </summary>
    public Action<int, string> BeforeCopyOf { get; init; } = static (_, _) => { };
}

/// <summary>
/// The names a snapshot never carries, matched at EVERY depth and with the destination filesystem's case rule.
/// </summary>
/// <remarks>
/// <para>What the reviewed branch could use to configure the CLI that reads it: <c>.vibe</c> (hooks, agents, tools, an
/// MCP server), <c>.agents/skills</c> (skills), <c>AGENTS.md</c> in both spellings and <c>.kimi-code</c> (Kimi's project
/// instructions and config, D15), and <c>.git</c> itself. An entry with a separator is a PATH suffix (<c>.agents/skills</c>
/// matches <c>x/.agents/skills/one.md</c> and not <c>.agents/PROJECT.md</c>); one without is a NAME at any depth.</para>
/// <para>One shared default set rather than one per vendor: a Vibe launch gains nothing from seeing Kimi's config, and
/// the list is a parameter so a runtime that needs more can pass its own.</para>
/// </remarks>
public sealed record SnapshotExclusions(IReadOnlyList<string> Names)
{
    public static SnapshotExclusions Default { get; } = new([".git", ".vibe", ".agents/skills", ".kimi-code", "AGENTS.md", "agents.md"]);

    /// <summary>The first exclusion <paramref name="relativePath"/> (forward slashes) meets at any depth, or empty.</summary>
    /// <remarks>
    /// Each component of the path is compared as Windows would OPEN it — trailing dots and spaces stripped — so
    /// <c>.vibe./config.toml</c> meets <c>.vibe</c>. The planner refuses such a name before it asks here
    /// (<see cref="SnapshotPaths.Unsafe"/>); this is the same rule applied where a name is matched rather than placed,
    /// for every later caller (S4's shim refuses excluded names on a read request by this method).
    /// </remarks>
    public string Matching(string relativePath, StringComparison rule)
    {
        var parts = relativePath.Split('/').Select(Folded).ToArray();

        return Names.FirstOrDefault(name => Meets(parts, Normalised(name).Split('/'), rule)) ?? string.Empty;
    }

    private static string Normalised(string name) => name.Trim().Replace('\\', '/').Trim('/');

    /// <summary>A path component as Windows opens it: without trailing dots and spaces.</summary>
    private static string Folded(string component) => component.TrimEnd('.', ' ');

    /// <summary>Whether the exclusion's components appear, in order, anywhere in the path's.</summary>
    private static bool Meets(string[] parts, string[] name, StringComparison rule) =>
        name.Length > 0
        && parts.Length >= name.Length
        && Enumerable.Range(0, parts.Length - name.Length + 1).Any(start => SameFrom(parts, start, name, rule));

    private static bool SameFrom(string[] parts, int start, string[] name, StringComparison rule) =>
        name.Select((component, i) => string.Equals(parts[start + i], component, rule)).All(same => same);
}
