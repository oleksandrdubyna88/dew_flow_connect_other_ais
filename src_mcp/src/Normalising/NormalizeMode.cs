using CoaiMcp.Core.Normalising;

namespace CoaiMcp.Normalising;

/// <summary>One method to read out of a file and rewrite.</summary>
/// <param name="Id">The caller's own handle; echoed back so a batch can be re-joined.</param>
/// <param name="Path">Used only to decide the language — this mode reads no files.</param>
/// <param name="Line">1-based, as a finding's line is.</param>
/// <param name="Source">The file's text AT THE COMMIT the caller wants, which only it can fetch.</param>
public sealed record NormalizeAsk(string Id = "", string Path = "", int Line = 0, string Source = "");

/// <summary>What became of one ask.</summary>
/// <param name="Skip">
/// Empty when it worked; otherwise the collector's own reason code — <c>language_unsupported</c> or
/// <c>symbol_not_resolved</c>. The vocabulary is the plan's, so the caller records what it is given
/// rather than inventing a second set of words for the same outcomes.
/// </param>
/// <param name="Leaks">
/// Words the skeleton kept that are neither placeholder, keyword nor runtime vocabulary. NON-EMPTY
/// IS A DEFECT IN THIS CODE, not a property of the input — the caller records those as
/// <c>failed</c>, never as a skip.
/// </param>
public sealed record NormalizeAnswer(
    string Id = "",
    string Language = "",
    string Skip = "",
    string Kind = "",
    int StartLine = 0,
    int EndLine = 0,
    string Skeleton = "",
    IReadOnlyList<string>? Leaks = null);

public sealed record NormalizeRequest(IReadOnlyList<NormalizeAsk>? Items = null);

public sealed record NormalizeResult(IReadOnlyList<NormalizeAnswer>? Items = null);

/// <summary>
/// The corpus collector's parser, as a MODE of this binary rather than a second one.
/// </summary>
/// <remarks>
/// <para><b>Why a mode.</b> The release publishes one file per platform, and a companion executable
/// beside it is one somebody eventually copies without — the same reasoning that made
/// <c>--ask-local</c> a mode. tree-sitter reaches its grammars by P/Invoke, which Native AOT carries
/// without complaint, so the sidecar this was first planned as would have bought a second release
/// line and nothing else. The grammars ride beside the binary exactly as <c>e_sqlite3</c> does.</para>
/// <para><b>Files in, files out, and batched.</b> A method's source is too big for an argument on
/// Windows and the answers are bigger still; <c>--ask-local</c> already takes its prompt from a file
/// for that reason. One invocation per collect run rather than per finding, because the cost here is
/// the process, not the parse.</para>
/// <para>It reads no source files itself. The caller fetches text out of git, because only the caller
/// knows which commit it wants — and half of them are commits no branch can reach any more.</para>
/// </remarks>
public sealed class NormalizeMode(IAstNormalizer normalizer)
{
    private readonly Dictionary<SourceLanguage, IReadOnlySet<string>> _keywords = [];

    /// <summary>Answers every ask, in the order given.</summary>
    public NormalizeResult Run(NormalizeRequest request) =>
        new([.. (request.Items ?? []).Select(Answer)]);

    private NormalizeAnswer Answer(NormalizeAsk ask)
    {
        var language = normalizer.LanguageOf(ask.Path);
        if (language is SourceLanguage.Unsupported)
        {
            return new NormalizeAnswer(ask.Id, language.ToString(), Skip: "language_unsupported");
        }

        if (normalizer.Locate(language, ask.Source, ask.Line) is not { } symbol)
        {
            // Expected, and often: a finding's line is whatever the reviewing model wrote, and plenty
            // of real ones point at a field, a using block or a blank line.
            return new NormalizeAnswer(ask.Id, language.ToString(), Skip: "symbol_not_resolved");
        }

        var skeleton = normalizer.Normalise(language, symbol.Source);

        return new NormalizeAnswer(
            ask.Id,
            language.ToString(),
            Skip: string.Empty,
            symbol.Kind,
            symbol.StartLine,
            symbol.EndLine,
            skeleton,
            Skeleton.Leaks(skeleton, language, Keywords(language)));
    }

    /// <summary>A language's keywords, read from the grammar once per run rather than per ask.</summary>
    private IReadOnlySet<string> Keywords(SourceLanguage language)
    {
        if (!_keywords.TryGetValue(language, out var known))
        {
            known = normalizer.KeywordsOf(language);
            _keywords[language] = known;
        }

        return known;
    }
}
