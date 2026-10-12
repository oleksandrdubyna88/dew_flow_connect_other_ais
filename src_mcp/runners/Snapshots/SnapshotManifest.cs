namespace CoaiMcp.Runners.Snapshots;

/// <summary>A path the tree has and the snapshot could not present as reviewed source, and why.</summary>
/// <param name="Path">Snapshot-relative, forward slashes.</param>
/// <param name="Why">The sentence the prompt can show: an unpopulated submodule, an LFS pointer.</param>
public sealed record SnapshotUnavailable(string Path, string Why);

/// <summary>
/// What one snapshot holds: the paths copied, the paths left out, the paths that could not be presented, and the size.
/// </summary>
/// <remarks>
/// <para>Every path is snapshot-relative with forward slashes, and — because the snapshot keeps the tree's own layout —
/// is also the repository-relative path. That identity is what <see cref="ReviewSnapshot.MapBack"/> rests on: a
/// finding's absolute path inside the snapshot maps back ONLY when the manifest holds it (D6, citations).</para>
/// <para><see cref="CaseInsensitive"/> is what the DESTINATION filesystem answered when probed, not what the operating
/// system is assumed to do: the exclusions and the collision rule were applied with it, and the root containment of
/// <see cref="ReviewSnapshot.Resolve"/> uses it again.</para>
/// </remarks>
public sealed record SnapshotManifest(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Excluded,
    IReadOnlyList<SnapshotUnavailable> Unavailable,
    long Bytes,
    bool CaseInsensitive)
{
    /// <summary>The copied paths as a set: a read-serving shim asks <see cref="Holds"/> per request, and a scan of fifty thousand names per request is not an answer.</summary>
    private readonly HashSet<string> _held = new(Files, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="relative"/> (forward slashes) is a copied file — ordinal, the manifest's own spelling.</summary>
    public bool Holds(string relative) => _held.Contains(relative);

    /// <summary>By value, list contents included — the synthesized equality would compare the lists (and the set above) by reference.</summary>
    public bool Equals(SnapshotManifest? other) =>
        other is not null
        && Bytes == other.Bytes
        && CaseInsensitive == other.CaseInsensitive
        && SameLists(other);

    /// <summary>The three lists of both, element by element — the paths ordinally.</summary>
    private bool SameLists(SnapshotManifest other) =>
        Files.SequenceEqual(other.Files, StringComparer.Ordinal)
        && Excluded.SequenceEqual(other.Excluded, StringComparer.Ordinal)
        && Unavailable.SequenceEqual(other.Unavailable);

    public override int GetHashCode() => HashCode.Combine(Files.Count, Excluded.Count, Unavailable.Count, Bytes, CaseInsensitive);
}
