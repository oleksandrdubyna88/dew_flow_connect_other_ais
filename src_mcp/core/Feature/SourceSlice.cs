using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Core.Feature;

/// <summary>
/// One piece of one file, served to a feature reviewer: which lines of which file at which commit,
/// and the text — already through <see cref="Notices.Redaction.SafeSource"/>.
/// </summary>
/// <param name="File">Repository-relative, as the reviewer asked for it.</param>
/// <param name="Symbol">The declaration's name when a symbol was asked for; empty for lines or a whole file.</param>
/// <param name="StartLine">1-based, inclusive.</param>
/// <param name="EndLine">1-based, inclusive.</param>
/// <param name="TotalLines">How many lines the file has, so "28-35 of 49" tells the reviewer what it is not seeing.</param>
/// <param name="Sha">The commit the text was read at — the whole id, because that is what "pinned" means.</param>
/// <param name="Text">The lines, joined with <c>\n</c>; a whole file verbatim.</param>
/// <param name="Note">What was cut or counted — "cut at 400 lines; ask for lines 401-500", "4 overloads, 3 shown" — or empty.</param>
/// <param name="Why">The reviewer's own reason, carried to the heading.</param>
public sealed record ServedSlice(
    string File,
    string Symbol,
    int StartLine,
    int EndLine,
    int TotalLines,
    string Sha,
    string Text,
    string Note,
    string Why)
{
    /// <summary>What this slice costs a budget: the text's UTF-8 bytes.</summary>
    public int Bytes => Encoding.UTF8.GetByteCount(Text);

    /// <summary>The heading and the fenced text, as a reviewer reads them.</summary>
    public string Render()
    {
        var fence = SourceFence.For(Text);
        var body = Text.EndsWith('\n') ? Text : Text + "\n";

        return $"### {Heading()}\n{NoteLine()}{fence}{SourceFence.LanguageOf(File)}\n{body}{fence}\n";
    }

    private string Heading() =>
        $"{File} lines {StartLine}-{EndLine} of {TotalLines} @ {Sha}"
        + (Symbol.Length > 0 ? $" — {Symbol}" : string.Empty)
        + (Why.Length > 0 ? $" (why: {SourceFence.OneLine(Why)})" : string.Empty);

    private string NoteLine() => Note.Length > 0 ? $"note: {Note}\n" : string.Empty;
}

/// <summary>One request that was not served, and the sentence that says why.</summary>
/// <param name="Symbol">The symbol asked for, when one was; empty otherwise.</param>
public sealed record SourceRefusal(string File, string Symbol, string Reason)
{
    /// <summary>The line the reviewer reads: <c>not served: path [symbol] — reason</c>.</summary>
    public string Render() =>
        $"not served: {File}{(Symbol.Length > 0 ? " " + Symbol : string.Empty)} — {Reason}";
}

/// <summary>
/// How much source one reviewer has been served so far, over every turn — carried from turn to
/// turn by the caller, never kept by the resolver, which is shared by every reviewer of a round.
/// </summary>
public readonly record struct SourceSpend(int Bytes)
{
    public static SourceSpend None => default;
}

/// <summary>What one turn's requests came to: the slices served, the refusals, and the spend after them.</summary>
/// <param name="Spent">The reviewer's spend AFTER this turn — what the caller hands to the next one.</param>
public sealed record ServedTurn(
    ImmutableArray<ServedSlice> Served,
    ImmutableArray<SourceRefusal> Refused,
    SourceSpend Spent)
{
    public bool IsEmpty => Served.IsEmpty && Refused.IsEmpty;

    /// <summary>Every served slice fenced with its path, lines and commit, then every refusal on a line of its own.</summary>
    public string Render()
    {
        var text = new StringBuilder();
        foreach (var slice in Served)
        {
            text.Append(slice.Render()).Append('\n');
        }

        foreach (var refusal in Refused)
        {
            text.Append(refusal.Render()).Append('\n');
        }

        return text.ToString();
    }
}

/// <summary>How served text is fenced: a fence longer than any run of backticks inside it, and the language a reader expects.</summary>
public static partial class SourceFence
{
    [GeneratedRegex("`+", RegexOptions.CultureInvariant)]
    private static partial Regex Backticks { get; }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace { get; }

    /// <summary>At least three backticks, and one more than the longest run in <paramref name="text"/> — so the text cannot close its own fence.</summary>
    public static string For(string text)
    {
        var longest = Backticks.Matches(text).Select(run => run.Length).DefaultIfEmpty(0).Max();

        return new string('`', Math.Max(3, longest + 1));
    }

    /// <summary>The fence's language word — the outliner's seven by name, otherwise the extension itself.</summary>
    public static string LanguageOf(string path) => OutlineLanguages.Of(path) switch
    {
        OutlineLanguage.CSharp => "csharp",
        OutlineLanguage.TypeScript => "typescript",
        OutlineLanguage.Tsx => "tsx",
        OutlineLanguage.JavaScript => "javascript",
        OutlineLanguage.Rust => "rust",
        OutlineLanguage.Php => "php",
        OutlineLanguage.Python => "python",
        _ => ByExtension(path),
    };

    private static string ByExtension(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        return extension == "md" ? "markdown" : extension;
    }

    /// <summary>A reviewer's sentence on one line, so it cannot break the heading it rides in.</summary>
    public static string OneLine(string text) => Whitespace.Replace(text, " ").Trim();
}
