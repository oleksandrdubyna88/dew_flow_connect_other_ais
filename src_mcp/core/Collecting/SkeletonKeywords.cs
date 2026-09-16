namespace CoaiMcp.Core.Collecting;

/// <summary>
/// The language keywords a skeleton may contain, read from a file rather than from a parser.
/// </summary>
/// <remarks>
/// <para><b>A cache of a derivation, not a hand-written list.</b> The keywords are the anonymous
/// symbols of each tree-sitter grammar — the grammar knows them exactly, and
/// <c>TreeSitterNormalizer.KeywordsOf</c> reads them off it for that reason. The ingest server needs
/// the same set and must not ship sixty megabytes of native grammars to ask a parser for a word
/// list, so the derivation is run once, checked in, and pinned by a test that fails the day a grammar
/// bump changes it.</para>
/// <para>The alternative — the server carrying its own list — is the second copy that goes stale
/// silently, and the symptom would be an ingest server refusing skeletons that are perfectly good.</para>
/// </remarks>
public static class SkeletonKeywords
{
    /// <summary>The file, relative to the repository root.</summary>
    public const string FileName = "skeleton-keywords.txt";

    /// <summary>Each language's keywords, keyed by <c>SourceLanguage</c>'s own name.</summary>
    /// <remarks>
    /// Sections are <c>[CSharp]</c>, then one word a line. Comments open with <c>#</c>, because the
    /// file explains itself to whoever opens it wondering why a server has a word list in it.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> Read(string repositoryRoot)
    {
        var file = Path.Combine(repositoryRoot, "shared", FileName);
        var sections = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);
        var current = string.Empty;
        var words = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in File.ReadLines(file))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Close(sections, current, words);
                current = line[1..^1];
                words = new HashSet<string>(StringComparer.Ordinal);
                continue;
            }

            words.Add(line);
        }

        Close(sections, current, words);

        return sections;
    }

    private static void Close(
        Dictionary<string, IReadOnlySet<string>> sections, string name, HashSet<string> words)
    {
        if (name.Length > 0)
        {
            sections[name] = words;
        }
    }
}
