using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;

namespace CoaiBugs;

/// <summary>One pair, as it crosses the wire.</summary>
/// <remarks>
/// <para><b>Three fields, and an architecture test says so.</b> `StoredPair` on the client carries the
/// finding id, the symbol, the severity, the category and the title — every one of which must never
/// leave — and a story-5 code round flagged reusing it here as the obvious way to leak all five at
/// once. Explicit mapping is human discipline; the test is what makes widening this a red build.</para>
/// <para><b>No id.</b> Idempotency needs a stable key and the server derives one from these three
/// fields, so nothing extra crosses and there is no second copy to disagree.</para>
/// </remarks>
public sealed record UploadedPair(
    string Language = "", string SkeletonBefore = "", string SkeletonAfter = "");

/// <summary>A batch, as the client sends it.</summary>
/// <remarks>
/// Nullable and read through a pattern match at the boundary: a document with no <c>items</c> is a
/// malformed request, and normalising it to an empty list would make a misspelled field look like a
/// successful no-op. `coai-mcp`'s own `--pairs-keep` learned that during story 5.
/// </remarks>
public sealed record IngestRequest(IReadOnlyList<UploadedPair>? Items = null);

/// <summary>What one item came to.</summary>
public sealed record IngestResult(string EntryId = "", string Took = "", string Why = "");

/// <summary>What the batch came to, item by item.</summary>
public sealed record IngestAnswer(IReadOnlyList<IngestResult> Items);

/// <summary>
/// Taking a batch: every item judged on its own.
/// </summary>
/// <remarks>
/// <para><b>One refusal never fails its neighbours.</b> A whole-batch 4xx is a batch the client
/// retries unchanged, for ever, with every valid pair stranded behind the invalid one — which a plan
/// reviewer traced out before a line of this existed. So the answer is 200 with a result per item,
/// and a refusal carries the word that caused it.</para>
/// <para>Pure, and separate from the route, so the decisions are a unit test rather than an HTTP
/// fixture.</para>
/// </remarks>
public static class Ingest
{
    /// <summary>The most pairs one request may carry.</summary>
    /// <remarks>
    /// With the body cap, this is what actually bounds abuse — `submissions` is a counter without a
    /// clock and is not a rate limit, which the plan said after a reviewer pointed out that a
    /// lifetime count has no window and no reset.
    /// </remarks>
    public const int MostPerBatch = 200;

    /// <summary>The largest body this server reads, in bytes.</summary>
    public const int MostBytes = 1024 * 1024;

    /// <summary>Judges every item, stores what it can, and answers each one.</summary>
    public static IngestAnswer Take(
        Corpus corpus,
        IReadOnlyList<UploadedPair> items,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        string keyId,
        string nowUtc)
    {
        var results = new List<IngestResult>(items.Count);
        foreach (var item in items)
        {
            results.Add(One(corpus, item, keywords, keyId, nowUtc));
        }

        return new IngestAnswer(results);
    }

    private static IngestResult One(
        Corpus corpus,
        UploadedPair pair,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        string keyId,
        string nowUtc)
    {
        var id = Corpus.IdOf(pair.Language, pair.SkeletonBefore, pair.SkeletonAfter);

        if (!Enum.TryParse<SourceLanguage>(pair.Language, out var language)
            || language is SourceLanguage.Unsupported)
        {
            return Refused(id, $"'{pair.Language}' is not a language this corpus holds");
        }

        if (!keywords.TryGetValue(pair.Language, out var words))
        {
            return Refused(id, $"this server has no keyword list for {pair.Language}");
        }

        // BOTH halves. A pair is only as anonymous as its worse side, and checking one would be a
        // check that reads as thorough and is not.
        foreach (var skeleton in (string[])[pair.SkeletonBefore, pair.SkeletonAfter])
        {
            var refusal = Alphabet.Refuse(skeleton, language, words);
            if (refusal.Length > 0)
            {
                return Refused(id, refusal);
            }
        }

        if (string.Equals(pair.SkeletonBefore, pair.SkeletonAfter, StringComparison.Ordinal))
        {
            // A pair whose halves are identical teaches nothing: the whole artefact is the DIFFERENCE
            // between broken and fixed. The collector should never produce one, so this is a client
            // defect rather than a judgement about the code.
            return Refused(id, "the two skeletons are identical, so the pair shows no change");
        }

        var took = corpus.Keep(
            id, pair.Language, pair.SkeletonBefore, pair.SkeletonAfter, keyId, nowUtc);

        return new IngestResult(id, took.ToString().ToLowerInvariant(), string.Empty);
    }

    private static IngestResult Refused(string id, string why) =>
        new(id, Took.Refused.ToString().ToLowerInvariant(), why);
}
