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
    /// <see cref="Corpus.MostWaiting"/> and the rate limiter — `submissions` is a lifetime COUNT and
    /// is not a rate limit, which the plan said after a reviewer pointed out that a lifetime count
    /// has no window and no reset. The only clock on a key is `last_seen_month`, which is a month
    /// and not a date, so it cannot answer when a request was made either.
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
    /// <param name="commented">
    /// Whether this batch arrived on <c>/ingest/commented</c>, the only route that READS a comment.
    /// A pair posted to plain <c>/ingest</c> is stored without one however it was serialised, which
    /// is what makes the two routes genuinely different rather than differently named.
    /// </param>
    public static UploadAnswer Take(
        IngestScope scope,
        IReadOnlyList<UploadedPair> items,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        bool commented = false)
    {
        // Read ONCE for the batch, not per item: the room's size is a property of the room, and a
        // count per pair would be two hundred queries to answer the same question.
        var room = Corpus.MostWaiting - scope.WaitingCount();
        var results = new List<UploadResult>(items.Count);
        foreach (var item in items)
        {
            var result = One(scope, item, keywords, room > 0, commented);
            room -= result.Took == Took.Accepted ? 1 : 0;
            results.Add(result);
        }

        return new UploadAnswer(results, Contract.Comments);
    }

    private static UploadResult One(
        IngestScope scope,
        UploadedPair pair,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords,
        bool room,
        bool commented)
    {
        // Every field read through `Whole()`, which is where a null the deserializer produced stops.
        // A positional record's `= null` default does NOT make an omitted JSON field anything but
        // null, and the validator below dereferences what it is handed. (Code round, codex.)
        var (name, before, after, typed) = pair.Whole();
        var id = PairId.Of(name, before, after);
        // Only the commented route reads it, and the refusal below is what makes that safe rather
        // than lossy. Kept as an explicit narrowing anyway: if the refusal is ever removed, this
        // still cannot write a comment through the route that promises not to store one.
        var comment = commented ? typed : string.Empty;

        var refusal = Refusal(name, before, after, typed, commented, keywords);
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
        var (kept, stored, words) = scope.Keep(name, before, after, comment);

        return new UploadResult(stored,
            kept is Kept.Stored ? Took.Accepted : Took.Duplicate,
            Lost(words));
    }

    /// <summary>
    /// What a person is owed when they wrote something and the store did not keep it.
    /// </summary>
    /// <remarks>
    /// <para><c>duplicate</c> is a SUCCESS, so without this the contributor is told their comment
    /// landed when it did not. It is answered from what the STORE did, not from what the pair's fate
    /// was: a pair that was already waiting and carried no comment takes this one, and that is a
    /// duplicate with nothing lost. (Plan round, gemini; code round, gemini again on the case where
    /// nothing could ever be attached.)</para>
    /// <para><b>Two ways to end up here, and they are told apart.</b> Somebody else's words are
    /// already on the pair, or the pair has been promoted out of the queue and the decision it
    /// belonged to is made. One sentence for both said "already carries a comment" to a person whose
    /// pair carried none, which is a false explanation of where their words went. (Code round,
    /// codex.) Storing several would need a comments table, a story of its own and not this one.
    /// </para>
    /// </remarks>
    private static string Lost(Words words) => words switch
    {
        Words.AlreadySpokenFor =>
            "this pair already carries a comment, and the first one stays, so yours was not stored",
        Words.TooLate =>
            "this pair has already been promoted into the corpus, so your comment was not stored",
        _ => string.Empty,
    };

    /// <summary>Why this pair may not be stored at all — the two whitelists, in their order.</summary>
    /// <remarks>
    /// <b>The alphabet first, deliberately.</b> A pair that both leaks an identifier and over-runs
    /// the comment limit reports the LEAK, because that is the refusal a person has to see first:
    /// one says somebody's private code nearly left their machine, the other says a sentence was
    /// too long. Split from <see cref="One"/> so that neither method exceeds the cyclomatic bound
    /// the C# doctrine sets at four, and because "may this pair be stored" is one question however
    /// many rules answer it.
    /// </remarks>
    private static string Refusal(
        string name,
        string before,
        string after,
        string comment,
        bool commented,
        IReadOnlyDictionary<string, IReadOnlySet<string>> keywords)
    {
        var unfit = Unfit(name, before, after, keywords);
        if (unfit.Length > 0)
        {
            return unfit;
        }

        return commented ? CommentRule.Refuse(comment) : Misrouted(comment);
    }

    /// <summary>
    /// A comment posted to the route that does not store one: refused, never dropped.
    /// </summary>
    /// <remarks>
    /// <para><b>Three code-round reviewers asked for this, and they were right.</b> The route exists
    /// because a server older than this drops an unknown field in SILENCE and answers
    /// <c>accepted</c>; making the new server do the same thing on its old route would be committing
    /// that exact failure ourselves, only harder to notice — the person is told their pair landed,
    /// and the words they wrote about it are gone with no trace anywhere.</para>
    /// <para>So the two routes differ in what they DO, not only in what they are called: one stores
    /// a comment, the other refuses to take one. A client that posts to the wrong path is told which
    /// path to use, and can retry the same batch with nothing lost — the refusal writes no row.
    /// </para>
    /// <para>No client is broken by this. A pair with no comment serialises without the property at
    /// all, so every existing client and every existing batch is unaffected; only a caller that
    /// deliberately put words in a comment and then sent them somewhere they cannot be kept sees
    /// this sentence.</para>
    /// </remarks>
    private static string Misrouted(string comment) =>
        comment.Length > 0
            ? "this pair carries a comment and was posted to /ingest, which stores none; post it to "
                + "/ingest/commented, where the comment is kept with the pair"
            : string.Empty;

    /// <summary>Why the PAIR itself may not be stored, or empty when it may.</summary>
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
