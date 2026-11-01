namespace CoaiMcp.Server;

/// <summary>How a written round came to be written — the buckets of <c>written</c> in the summary. Exclusive, in this priority: corrected, database only, then the verb's own.</summary>
internal enum WrittenAs
{
    Computed,
    RecomputedProvisional,
    RecomputedSnapshotLost,
    Reprojected,
    DatabaseOnly,
    Corrected,
}

/// <summary>A count per key: the one shape every bucket of a summary takes.</summary>
internal sealed class Tally<T>
    where T : notnull
{
    private readonly SortedDictionary<T, int> _counts = [];

    public int this[T key] => _counts.GetValueOrDefault(key);

    public int Total => _counts.Values.Sum();

    public IReadOnlyDictionary<T, int> All => _counts;

    public void Add(T key) => _counts[key] = _counts.GetValueOrDefault(key) + 1;
}

/// <summary>
/// What a run did, as counts only (E7.S3/S4): the closed buckets the summary prints. Nothing here carries a prompt, a description, a path or any
/// text a transcript said — the reasons are the closed sets of E3, E4 and S2.
/// </summary>
/// <remarks>A counter, not a value: one run owns one, and the rounds of one run add to it in order.</remarks>
internal sealed class BackfillCounts
{
    /// <summary>Settled plan or code rounds the run looked at.</summary>
    public int Selected { get; set; }

    /// <summary>Rounds whose authors were written. The figure a second run must bring to zero.</summary>
    public Tally<WrittenAs> Written { get; } = new();

    /// <summary>Session files given back the snapshot they lost (built from the database; the database is not touched).</summary>
    public int Healed { get; set; }

    /// <summary>Rounds left as they were, and why.</summary>
    public Tally<BackfillWhy> Kept { get; } = new();

    /// <summary>Rounds the run could not do now, and why: counted, never recorded on the round, retried on the next run.</summary>
    public Tally<BackfillWhy> Deferred { get; } = new();

    /// <summary>The states written, per reason: how many rounds came out <c>unobserved: &lt;reason&gt;</c>.</summary>
    public SortedDictionary<string, int> Unobserved { get; } = new(StringComparer.Ordinal);

    public int SizeWritten { get; set; }

    public int SizeKept { get; set; }

    public SortedDictionary<string, int> SizeUnrecoverable { get; } = new(StringComparer.Ordinal);

    /// <summary>The delta state of every size written, per <see cref="Core.Context.ScopeState"/> word.</summary>
    public SortedDictionary<string, int> SizeDelta { get; } = new(StringComparer.Ordinal);

    public static void Add(SortedDictionary<string, int> bucket, string key) => bucket[key] = bucket.GetValueOrDefault(key) + 1;
}
