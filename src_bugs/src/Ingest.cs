using CoaiMcp.Core.Collecting;
using CoaiMcp.Core.Normalising;

namespace CoaiBugs;

/// <summary>
/// Taking a batch: every item judged on its own.
/// </summary>
/// <remarks>
/// <para><b>One refusal never fails its neighbours.</b> A whole-batch 4xx is a batch the client
/// retries unchanged, for ever, with every valid pair stranded behind the invalid one — which a plan
/// reviewer traced out before a line of this existed. So the answer is 200 with a result per item,
/// and a refusal carries the word that caused it.</para>
/// <para><b>The wire types are not here.</b> <see cref="UploadedPair"/>, <see cref="UploadRequest"/>
/// and <see cref="UploadAnswer"/> live in <c>CoaiMcp.Core.Collecting</c>, which both halves
/// reference. They were declared twice, once per binary, and two reviewers pointed out that a field
/// renamed on one side leaves the other compiling and both suites green.</para>
/// <para>Pure, and separate from the route, so the decisions are a unit test rather than an HTTP
/// fixture.</para>
/// </remarks>
public static class Ingest
{
    /// <summary>The most pairs one request may carry.</summary>
    /// <remarks>
    /// With the body cap this bounds one REQUEST. What bounds a key holder across requests is
    /// <see cref="Corpus.MostWaiting"/> — `submissions` is a counter without a clock and is not a
    /// rate limit, which the plan said after a reviewer pointed out that a lifetime count has no
    /// window and no reset.
    /// </remarks>
    public const int MostPerBatch = 200;

    /// <summary>The largest body this server reads, in bytes.</summary>
    public const int MostBytes = 1024 * 1024;

    /// <summary>
    /// Judges every item, stores what it can inside the batch's transaction, and answers each one.
    /// </summary>
    /// <remarks>
    /// <paramref name="scope"/> IS the batch: <see cref="Corpus.Accept"/> opened one transaction for
    /// one key in one month and hands this exactly what may be written inside it. An explicit
    /// argument, where an ambient flag used to let <c>Keep</c> ask the corpus whether a batch
    /// happened to be open — a leaky abstraction that stops being true the moment anything on this
    /// path becomes async. (Code round, gemini.)
    /// </remarks>
    public static UploadAnswer Take(
        IngestScope scope,
        IReadOnlyList<UploadedPair> items,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords)
    {
        // Read ONCE for the batch, not per item: the room's size is a property of the room, and a
        // count per pair would be two hundred queries to answer the same question.
        var room = Corpus.MostWaiting - scope.WaitingCount();
        var results = new List<UploadResult>(items.Count);
        foreach (var item in items)
        {
            var result = One(scope, item, keywords, room > 0);
            room -= result.Took == Took.Accepted ? 1 : 0;
            results.Add(result);
        }

        return new UploadAnswer(results);
    }

    private static UploadResult One(
        IngestScope scope,
        UploadedPair pair,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        bool room)
    {
        // Every field read through `Whole()`, which is where a null the deserializer produced stops.
        // A positional record's `= null` default does NOT make an omitted JSON field anything but
        // null, and the validator below dereferences what it is handed. (Code round, codex.)
        var (name, before, after) = pair.Whole();
        var id = PairId.Of(name, before, after);

        var refusal = Unfit(name, before, after, keywords);
        if (refusal.Length > 0)
        {
            return Refused(id, refusal);
        }

        if (!room)
        {
            return Refused(
                id,
                $"quarantine holds {Corpus.MostWaiting} pairs nobody has read yet; nothing more is "
                + "taken until a person promotes or rejects what is waiting");
        }

        // The id comes BACK from the store, which derives it: two places computing one identity is
        // how they come to disagree. This one is still computed above, for the refusals that never
        // reach the store at all, and the two are asserted equal by `TheIdIsAFunctionOfThePair`.
        var (kept, stored) = scope.Keep(name, before, after);

        return new UploadResult(
            stored, kept is Kept.Stored ? Took.Accepted : Took.Duplicate, string.Empty);
    }

    /// <summary>Why this pair may not be stored, or empty when it may.</summary>
    private static string Unfit(
        string name,
        string before,
        string after,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords)
    {
        if (!Enum.TryParse<SourceLanguage>(name, out var language)
            || language is SourceLanguage.Unsupported)
        {
            return $"'{name}' is not a language this corpus holds";
        }

        return keywords.TryGetValue(name, out var words)
            ? Leaked(before, after, language, words)
            : $"this server has no keyword list for {name}";
    }

    /// <summary>What the alphabet refuses in either half, or why the pair teaches nothing.</summary>
    /// <remarks>
    /// Split out of <see cref="Unfit"/> to stay inside the cyclomatic bound the C# doctrine sets at
    /// four. The two are also two different questions — "is this anonymous" and "is this a change at
    /// all" — and they read better apart than stacked.
    /// </remarks>
    private static string Leaked(
        string before, string after, SourceLanguage language, IReadOnlySet<string> words)
    {
        // BOTH halves. A pair is only as anonymous as its worse side, and checking one would be a
        // check that reads as thorough and is not.
        foreach (var skeleton in (string[])[before, after])
        {
            var leak = Alphabet.Refuse(skeleton, language, words);
            if (leak.Length > 0)
            {
                return leak;
            }
        }

        // A pair whose halves are identical teaches nothing: the whole artefact is the DIFFERENCE
        // between broken and fixed. The collector should never produce one, so this is a client
        // defect rather than a judgement about the code.
        return string.Equals(before, after, StringComparison.Ordinal)
            ? "the two skeletons are identical, so the pair shows no change"
            : string.Empty;
    }

    private static UploadResult Refused(string id, string why) => new(id, Took.Refused, why);
}
