using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;

namespace CoaiMcp.Core.Outlining;

/// <summary>A language the OUTLINER can read, or the honest statement that it cannot.</summary>
/// <remarks>
/// <para><b>Separate from <see cref="Normalising.SourceLanguage"/>, deliberately.</b> That enum is the
/// defect corpus's trust boundary: <c>coai-bugs</c> parses it, and the normalizer's function-kind
/// table falls through to the JavaScript kinds for anything it does not name — so widening it to
/// seven languages would silently change what the collector normalises and uploads. The feature
/// review needs seven languages and needs none of that behaviour, so it gets its own word for them
/// (plan, §4.7).</para>
/// <para><see cref="Tsx"/> is its own value because it is its own grammar: the package ships a
/// separate <c>tree-sitter-tsx</c>, and a JSX expression read with the TypeScript grammar is an
/// ERROR node. The collector still reads <c>.tsx</c> as TypeScript, unchanged.</para>
/// </remarks>
public enum OutlineLanguage
{
    Unsupported,
    CSharp,
    TypeScript,
    Tsx,
    JavaScript,
    Rust,
    Php,
    Python,
}

/// <summary>Which language a path is in, by its extension alone — pure, and the one mapping.</summary>
public static class OutlineLanguages
{
    /// <summary>The language of <paramref name="path"/>; a declaration file (<c>.d.ts</c>) is TypeScript.</summary>
    /// <remarks>
    /// By extension because a feature review reads files out of git at a commit, where there is no
    /// working-tree file to sniff. <c>.pyi</c> is a Python stub and <c>.jsx</c> is JavaScript: the
    /// JavaScript grammar reads JSX, the TypeScript one does not.
    /// </remarks>
    public static OutlineLanguage Of(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".cs" => OutlineLanguage.CSharp,
            ".ts" or ".mts" or ".cts" => OutlineLanguage.TypeScript,
            ".tsx" => OutlineLanguage.Tsx,
            ".js" or ".mjs" or ".cjs" or ".jsx" => OutlineLanguage.JavaScript,
            ".rs" => OutlineLanguage.Rust,
            ".php" => OutlineLanguage.Php,
            ".py" or ".pyi" => OutlineLanguage.Python,
            _ => OutlineLanguage.Unsupported,
        };
}

/// <summary>One declaration of a file: where it is, what it is called, and its signature WITHOUT a body.</summary>
/// <param name="Depth">0 at the top of the file; one more inside each container (a class, a module, an impl).</param>
/// <param name="Kind">A short word from the language's table — <c>class</c>, <c>method</c>, <c>fn</c>, <c>impl</c>.</param>
/// <param name="Name">What it is called, or empty when the grammar gives it no name (a conversion operator).</param>
/// <param name="Signature">
/// The source from the declaration's start to its body's start, whitespace collapsed, at most
/// <see cref="OutlineLimits.MaxSignatureChars"/>. No body, by construction.
/// </param>
/// <param name="StartLine">1-based and inclusive; a decorator or an <c>export</c> above it counts.</param>
/// <param name="EndLine">1-based and inclusive — the body's last line, which is what a source request asks for.</param>
public sealed record OutlineEntry(int Depth, string Kind, string Name, string Signature, int StartLine, int EndLine);

/// <summary>Whether a file was outlined, and if not, which of the three reasons applies.</summary>
public enum OutlineStatus
{
    Outlined,
    UnsupportedLanguage,
    TooLarge,
    ParseFailed,
}

/// <summary>The limits of an outline — one place, read by the outliner and by whoever reports on it.</summary>
public static class OutlineLimits
{
    /// <summary>
    /// The INPUT ceiling: a file larger than this is refused before any parse, with its size.
    /// </summary>
    /// <remarks>
    /// A minified bundle or a generated file can be megabytes on one line, and the outline runs inside
    /// the stdio server, where a parse that takes a minute is a server that stops answering. 1 MiB is
    /// the plan round's figure (E1, 2026-09-25); the largest real source file S0.1 measured was far
    /// under it (plan §6).
    /// </remarks>
    public const long MaxInputBytes = 1_048_576;

    /// <summary>A file whose ERROR nodes cover more than this share of its text is not outlined.</summary>
    /// <remarks>
    /// Measured as the characters under outermost ERROR nodes over all characters — a share of TEXT,
    /// not of nodes, because one broken brace can produce one ERROR node that swallows half a file.
    /// Past a fifth, an outline would present guesses as declarations.
    /// </remarks>
    public const double MaxErrorShare = 0.20;

    /// <summary>A signature longer than this is cut, with an ellipsis, so one attribute wall cannot eat a budget.</summary>
    public const int MaxSignatureChars = 240;
}

/// <summary>The outline of one file, or the named reason there is none.</summary>
/// <param name="Reason">Empty when <see cref="Status"/> is <see cref="OutlineStatus.Outlined"/>; a sentence otherwise.</param>
/// <param name="Bytes">The input's size in UTF-8 bytes — reported for every status, so a refusal says how big.</param>
/// <param name="ErrorShare">
/// The share of the text under ERROR nodes, 0 to 1 — measured whenever the file was parsed, so an
/// outline of a half-broken file says so; 0 when it was never parsed.
/// </param>
public sealed record SourceOutline(
    OutlineLanguage Language,
    OutlineStatus Status,
    string Reason,
    long Bytes,
    ImmutableArray<OutlineEntry> Entries,
    double ErrorShare = 0)
{
    [JsonIgnore]
    public bool IsOutlined => Status is OutlineStatus.Outlined;

    public static SourceOutline Of(
        OutlineLanguage language, long bytes, ImmutableArray<OutlineEntry> entries, double errorShare = 0) =>
        new(language, OutlineStatus.Outlined, string.Empty, bytes, entries, errorShare);

    public static SourceOutline UnsupportedLanguage(OutlineLanguage language, long bytes) =>
        new(language, OutlineStatus.UnsupportedLanguage, "unsupported (language)", bytes, []);

    /// <summary>Refused unparsed — the ONLY answer a file over the ceiling ever gets.</summary>
    public static SourceOutline TooLarge(OutlineLanguage language, long bytes) =>
        new(language, OutlineStatus.TooLarge,
            $"unsupported (too large): {bytes} bytes, over the {OutlineLimits.MaxInputBytes}-byte ceiling; not parsed",
            bytes, []);

    public static SourceOutline ParseFailed(OutlineLanguage language, long bytes, double errorShare) =>
        new(language, OutlineStatus.ParseFailed,
            $"unsupported (parse failed): {errorShare:P0} of its text did not parse as {language} ({bytes} bytes)",
            bytes, [], errorShare);

    /// <summary>The outline as the reviewer reads it: one line per entry, indented by depth, lines last.</summary>
    /// <remarks>
    /// <c>  public int Add(int a, int b) [12-18]</c>. The kind is not printed because the signature
    /// already says it, and every byte here is a byte of the feature review's outline budget —
    /// <c>FeatureBudget</c> was calibrated against exactly this rendering (plan §6, S0.2).
    /// </remarks>
    public string Render()
    {
        if (!IsOutlined)
        {
            return Reason + "\n";
        }

        var text = new StringBuilder();
        foreach (var entry in Entries)
        {
            text.Append(' ', entry.Depth * 2)
                .Append(entry.Signature)
                .Append(" [").Append(entry.StartLine).Append('-').Append(entry.EndLine).Append("]\n");
        }

        return text.ToString();
    }
}

/// <summary>
/// Reads the declarations out of a file — names, signatures, lines — and never a body.
/// </summary>
/// <remarks>
/// <para>The feature review's reader (plan §4.7, D3): a reviewer is sent the SHAPE of every changed
/// file and asks for the code it wants by name. A body in the outline would be code nobody asked for,
/// which is the whole thing D3 rules out.</para>
/// <para>The same seam discipline as <see cref="Normalising.IAstNormalizer"/>: the core names no
/// parser and does no IO; the implementation and every P/Invoke live in <c>CoaiMcp.Normalizer</c>.
/// The caller reads the text — out of git, at the commit it means.</para>
/// </remarks>
public interface ISourceOutliner
{
    /// <summary>Which language a path is in — <see cref="OutlineLanguages.Of"/>, offered here for symmetry.</summary>
    OutlineLanguage LanguageOf(string path);

    /// <summary>The outline of <paramref name="source"/>, or a named reason there is none. Never throws for input.</summary>
    /// <remarks>
    /// Enforces <see cref="OutlineLimits.MaxInputBytes"/> itself, before any parse, so no caller can
    /// forget to — a caller-side check is a check the next caller does not make.
    /// </remarks>
    SourceOutline Outline(OutlineLanguage language, string source);
}
