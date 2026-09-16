using System.Text.RegularExpressions;
using CoaiMcp.Core.Normalising;

namespace CoaiMcp.Core.Collecting;

/// <summary>
/// What a skeleton may be made of, checked by a server that has never seen the original.
/// </summary>
/// <remarks>
/// <para><b>The two validators are deliberately different, and this is the strictly stronger one.</b>
/// The client knows the source, so it asserts a BLACKLIST — no input identifier, string or number
/// survived — which is only possible while the original is in hand. The server has never seen it, so
/// it asserts a WHITELIST: every word is a placeholder or runtime vocabulary, every string is empty,
/// every number is zero. A whitelist needs no parser and cannot be defeated by a name nobody thought
/// to forbid.</para>
/// <para><b>It is SHAPE validation, not proof of anonymity.</b> A source method literally named
/// <c>method_1</c> normalises to a placeholder that may be the same string, and nothing here can tell
/// the two apart. Nothing unsafe follows — a name that coincides with one we would have generated
/// carries no information — but the plan first called this check the guarantee, and the guarantee is
/// the client's property test over real files. (Plan round, codex.)</para>
/// <para><b>Refused, never quarantined.</b> A skeleton that fails this is a defect in a client we
/// wrote, and it must come back as an error somebody reads. Quarantine is for a well-formed thing
/// that might be crafted, which is a different problem with a different answer.</para>
/// </remarks>
public static partial class Alphabet
{
    /// <summary>A name the normaliser generated: <c>var_1</c>, <c>type_2</c>, <c>method_3</c>.</summary>
    [GeneratedRegex(@"^(var|type|method)_\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder { get; }

    /// <summary>Every run of word characters, which is what this has an opinion about.</summary>
    /// <remarks>
    /// Digits are split out by the <c>[A-Za-z_]</c> start: a bare <c>0</c> is a number rather than a
    /// word, and numbers are checked by their own rule below. A word that begins with a digit cannot
    /// be an identifier in any language here.
    /// </remarks>
    [GeneratedRegex(@"[A-Za-z_][A-Za-z_0-9]*", RegexOptions.CultureInvariant)]
    private static partial Regex Words { get; }

    /// <summary>A numeric literal. Only an exact <c>0</c> is admitted.</summary>
    /// <remarks>
    /// <para>A LITERAL is a run of digits and at most one fractional part. Written greedily over
    /// <c>[\d.]*</c> it swallowed the range operator too: <c>var_1[0..0]</c> came back as the literal
    /// <c>0..</c>, which is not a number and not a leak, and a real file in this repository was refused
    /// for it. The test over real skeletons found that; a hand-written fixture would not have had a
    /// range in it.</para>
    /// <para>The lookbehind excludes a digit inside a placeholder — the <c>1</c> of <c>var_1</c> — and
    /// a digit that continues a number already matched.</para>
    /// </remarks>
    [GeneratedRegex(@"(?<![A-Za-z_0-9.])\d+(\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex Literal { get; }

    /// <summary>Every quoted run, with whatever is inside it captured.</summary>
    /// <remarks>
    /// <para>The normaliser writes every string as <c>""</c>, so what is refused is a capture with
    /// anything in it: a domain, a path, a message — the thing this whole pipeline exists to remove.</para>
    /// <para><b>Pairwise, and that is the whole difficulty.</b> Written as "a quoted run with content"
    /// — <c>"[^"]+"</c> — it matches ACROSS two adjacent empty strings: in
    /// <c>method_1("", "")</c> the <c>", "</c> between them is a quote, some content and a quote. Three
    /// real files in this repository were refused by that version, and the test over real skeletons is
    /// what found it. Matching every run and asking whether its CONTENT is empty consumes the quotes in
    /// pairs, which is what they are.</para>
    /// </remarks>
    [GeneratedRegex("\"([^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Quoted { get; }

    /// <summary>
    /// Every word in <paramref name="skeleton"/> that this alphabet does not admit.
    /// </summary>
    /// <remarks>
    /// The words themselves rather than a boolean, so a refusal can say WHICH — a person debugging a
    /// client that leaked needs the word, and a count tells them nothing. Ordered and de-duplicated,
    /// because a name repeated forty times is one problem.
    /// </remarks>
    /// <param name="keywords">
    /// The language's own tokens. PASSED IN rather than looked up, because they come from a
    /// tree-sitter grammar and the ingest server must not ship one — it reads them from
    /// `shared/skeleton-keywords.txt`, which `SkeletonKeywords` loads and a test pins to what the
    /// grammars actually say. `Skeleton.Leaks` takes them the same way and for the same reason.
    /// </param>
    public static IReadOnlyList<string> Strangers(
        string skeleton, SourceLanguage language, IReadOnlySet<string> keywords)
    {
        var known = RuntimeVocabulary.For(language);

        return
        [
            .. Words.Matches(skeleton)
                .Select(word => word.Value)
                .Where(word => !Placeholder.IsMatch(word)
                               && !known.Contains(word)
                               && !keywords.Contains(word))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Why this skeleton may not be accepted, or empty.</summary>
    /// <remarks>
    /// One sentence, naming the first thing wrong with it. A caller gets a reason it can show a person
    /// rather than a code it has to look up — this is the only answer a contributor ever sees when
    /// their client is the one at fault.
    /// </remarks>
    public static string Refuse(
        string skeleton, SourceLanguage language, IReadOnlySet<string> keywords)
    {
        if (skeleton.Length == 0)
        {
            return "the skeleton is empty";
        }

        var text = Quoted.Matches(skeleton)
            .FirstOrDefault(one => one.Groups[1].Value.Length > 0);
        if (text is not null)
        {
            return $"a string literal survived normalisation: {text.Value}";
        }

        var number = Literal.Matches(skeleton)
            .FirstOrDefault(one => !string.Equals(one.Value, "0", StringComparison.Ordinal));
        if (number is not null)
        {
            return $"a numeric literal survived normalisation: {number.Value}";
        }

        var strangers = Strangers(skeleton, language, keywords);

        return strangers.Count > 0
            ? $"{strangers.Count} word(s) are neither placeholder nor runtime vocabulary: "
              + string.Join(", ", strangers.Take(5))
            : string.Empty;
    }

    /// <summary>Whether this skeleton is made only of what the alphabet admits.</summary>
    public static bool Admits(
        string skeleton, SourceLanguage language, IReadOnlySet<string> keywords) =>
        Refuse(skeleton, language, keywords).Length == 0;
}
