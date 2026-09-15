using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Normalising;

/// <summary>
/// What a normalised method is allowed to contain, checked without seeing the original.
/// </summary>
/// <remarks>
/// <para><b>Two different checks guard the same promise, deliberately.</b> The collector, which holds
/// the original, can assert that no identifier of ours survived — a blacklist, and the stronger check
/// where it is possible. The ingest server has never seen the original and never will, so the only
/// check available to it is this one: every word in the skeleton is either a generated placeholder or
/// a member of <see cref="RuntimeVocabulary"/>. That is a whitelist, and it is the stronger SHAPE —
/// it cannot be defeated by a name nobody thought to forbid.</para>
/// <para>It lives in the pure core so that both sides run the same code rather than two descriptions
/// of one rule, which is how the two would drift.</para>
/// </remarks>
public static partial class Skeleton
{
    /// <summary>The three placeholder shapes a normaliser may emit.</summary>
    [GeneratedRegex(@"^(var|type|method)_\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder { get; }

    /// <summary>Every identifier-ish word, which is what has to be accounted for.</summary>
    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.CultureInvariant)]
    private static partial Regex Word { get; }

    /// <summary>
    /// Words in <paramref name="skeleton"/> that are neither a placeholder nor runtime vocabulary.
    /// </summary>
    /// <remarks>
    /// <para>Empty means the skeleton may leave the machine. Anything in it is a leak, and the
    /// collector records that as <c>failed</c> rather than as a skip: a normaliser that lets a name
    /// through is a defect in our code, and burying it in the skip telemetry is how it would go
    /// unnoticed for a month.</para>
    /// <para>Language KEYWORDS are not vocabulary and are not placeholders, so they are passed in
    /// separately — the normaliser knows them because the grammar does, and a list of them here
    /// would be a second, worse copy of the grammar.</para>
    /// </remarks>
    public static IReadOnlyList<string> Leaks(
        string skeleton, SourceLanguage language, IReadOnlySet<string> keywords)
    {
        var allowed = RuntimeVocabulary.For(language);
        var leaks = new List<string>();
        foreach (Match word in Word.Matches(skeleton))
        {
            if (!Placeholder.IsMatch(word.Value) && !allowed.Contains(word.Value) && !keywords.Contains(word.Value))
            {
                leaks.Add(word.Value);
            }
        }

        return [.. leaks.Distinct(StringComparer.Ordinal)];
    }
}
