using CoaiMcp.Core.Context;

namespace CoaiMcp.Runners.Context;

/// <summary>
/// Turns the numstats a code round already has into coverage × kind cells (E2.S1 §S1.4) — every changed file in
/// exactly one coverage and one kind, binaries with no line count, excluded files counted apart.
/// </summary>
/// <remarks>
/// Pure: values in, values out. "Excluded" is decided by set difference against the filtered numstat — what the
/// reviewers were actually given — never by re-matching the exclusion globs; one decider, git's. Paths are matched
/// ordinally (git's trees are case-sensitive). No member accepts two sizes: a delta is a range, never arithmetic.
/// </remarks>
public static class ChangePartition
{
    /// <summary>One unfiltered record, placed: its coverage, its kind and its lines (null for a binary).</summary>
    private sealed record Placed(string Coverage, string Kind, int? Added, int? Removed);

    /// <param name="filtered">The reviewable numstat of the range — WITH the exclusion pathspecs: what the reviewers were given.</param>
    /// <param name="unfiltered">Every record of the SAME range, no pathspecs.</param>
    /// <param name="elided">The paths the shaper named but did not show.</param>
    /// <param name="from">The commit the range starts at — <see cref="ScopeSize.From"/>.</param>
    public static ScopeSize Of(
        IReadOnlyList<CountedChange> filtered,
        IReadOnlyList<CountedChange> unfiltered,
        IReadOnlyCollection<string> elided,
        string from) =>
        Disagreement(filtered, unfiltered, elided).Length > 0
            ? ScopeSize.Unavailable(ScopeState.NumstatsDisagree, from)
            : new ScopeSize(ScopeState.Recorded, from, Cells(Placements(filtered, unfiltered, elided)));

    /// <summary>
    /// The delta scope: <see cref="Of"/> over the delta range's own two numstats, with the ROUND's elided set narrowed
    /// to the delta's reviewable paths first — a file elided in the whole diff and untouched since the predecessor is
    /// not a delta file, and handing it to <see cref="Of"/> would read as a wiring defect.
    /// </summary>
    public static ScopeSize OfDelta(
        IReadOnlyList<CountedChange> deltaFiltered,
        IReadOnlyList<CountedChange> deltaUnfiltered,
        IReadOnlyCollection<string> roundElided,
        string from) =>
        Of(deltaFiltered, deltaUnfiltered, NarrowedToDelta(deltaFiltered, roundElided), from);

    /// <summary>The round's elided paths that the delta's reviewable numstat names — the only elided files the delta can hold.</summary>
    public static IReadOnlyCollection<string> NarrowedToDelta(
        IReadOnlyList<CountedChange> deltaFiltered, IReadOnlyCollection<string> roundElided)
    {
        var reviewable = new HashSet<string>(deltaFiltered.Select(f => f.Change.Path), StringComparer.Ordinal);

        return [.. roundElided.Where(reviewable.Contains)];
    }

    /// <summary>Why the three inputs cannot be partitioned — one of three fixed sentences, no path in any — or empty.</summary>
    public static string Disagreement(
        IReadOnlyList<CountedChange> filtered,
        IReadOnlyList<CountedChange> unfiltered,
        IReadOnlyCollection<string> elided)
    {
        var claims = Claims(new Reviewable(filtered), unfiltered);
        var paths = filtered.Select(f => f.Change.Path).ToHashSet(StringComparer.Ordinal);

        return filtered.Any(f => !claims.ContainsKey(f)) ? "a reviewable file is not in the unfiltered numstat"
            : claims.Values.Any(n => n > 1) ? "two unfiltered records match one reviewable file"
            : elided.Any(e => !paths.Contains(e)) ? "an elided path is not a reviewable file"
            : string.Empty;
    }

    /// <summary>How many unfiltered records claim each reviewable record — by reference, never by value (two records can be equal).</summary>
    private static Dictionary<CountedChange, int> Claims(Reviewable reviewable, IReadOnlyList<CountedChange> unfiltered)
    {
        var claims = new Dictionary<CountedChange, int>(ReferenceEqualityComparer.Instance);
        foreach (var match in unfiltered.Select(reviewable.Match).OfType<CountedChange>())
        {
            claims[match] = claims.GetValueOrDefault(match) + 1;
        }

        return claims;
    }

    /// <summary>
    /// The reviewable numstat, indexed once per call so that finding the twin of an unfiltered record is a lookup — the first
    /// record in list order wins on every key, exactly as a scan from the front would answer.
    /// </summary>
    private sealed class Reviewable
    {
        private readonly Dictionary<string, CountedChange> _byPath = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CountedChange> _byOldName = new(StringComparer.Ordinal);

        public Reviewable(IReadOnlyList<CountedChange> filtered)
        {
            foreach (var record in filtered)
            {
                _byPath.TryAdd(record.Change.Path, record);
                _byOldName.TryAdd(record.Change.RenamedFrom, record);
            }
        }

        /// <summary>
        /// The reviewable record of one unfiltered record, or none: by its own path, else by the old name a rename into an
        /// excluded folder leaves in the filtered numstat, else by the rename the filtered numstat paired and the
        /// unfiltered one did not (git skips rename detection past its limit) — which the caller reports, never files.
        /// </summary>
        public CountedChange? Match(CountedChange u) =>
            _byPath.GetValueOrDefault(u.Change.Path) ?? _byPath.GetValueOrDefault(u.Change.RenamedFrom) ?? _byOldName.GetValueOrDefault(u.Change.Path);
    }

    private static IEnumerable<Placed> Placements(
        IReadOnlyList<CountedChange> filtered,
        IReadOnlyList<CountedChange> unfiltered,
        IReadOnlyCollection<string> elided)
    {
        var elidedPaths = new HashSet<string>(elided, StringComparer.Ordinal);
        var reviewable = new Reviewable(filtered);

        return unfiltered.Select(u => Place(u, reviewable.Match(u), elidedPaths));
    }

    /// <summary>
    /// Coverage precedence: <c>binary</c> beats <c>excluded</c> beats <c>elided</c> beats <c>shown</c>. The lines of a
    /// reviewed file are the FILTERED record's (what the reviewers were shown); of an excluded one, the only record that
    /// saw it. The kind is the file's name at the head of the range — the unfiltered record's.
    /// </summary>
    private static Placed Place(CountedChange u, CountedChange? f, HashSet<string> elided)
    {
        var kind = ChangeKind.Of(u.Change.Path);

        return u.Change.IsBinary ? new Placed(ChangeCoverage.Binary, kind, null, null)
            : f is null ? new Placed(ChangeCoverage.Excluded, kind, u.Added, u.Deleted)
            : new Placed(Reviewed(f, elided), kind, f.Added, f.Deleted);
    }

    /// <summary>A file the reviewers were given: named but not shown when it went over the shaper's budget, else shown.</summary>
    private static string Reviewed(CountedChange f, HashSet<string> elided) =>
        elided.Contains(f.Change.Path) ? ChangeCoverage.Elided : ChangeCoverage.Shown;

    private static List<SizeCell> Cells(IEnumerable<Placed> placed) =>
        [.. placed
            .GroupBy(p => (p.Coverage, p.Kind))
            .Select(g => new SizeCell(g.Key.Coverage, g.Key.Kind, g.Count(), Total(g.Select(p => p.Added)), Total(g.Select(p => p.Removed))))
            .OrderBy(c => ChangeCoverage.All.IndexOf(c.Coverage))
            .ThenBy(c => ChangeKind.All.IndexOf(c.Kind))];

    /// <summary>The sum, or null when any member has no count — a group of binaries, whose every member is null.</summary>
    private static int? Total(IEnumerable<int?> counts) => counts.Contains(null) ? null : counts.Sum();
}
