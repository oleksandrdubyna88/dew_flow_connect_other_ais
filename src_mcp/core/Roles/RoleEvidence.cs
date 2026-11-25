using System.Globalization;
using System.Text;
using CoaiMcp.Core.Feature;

namespace CoaiMcp.Core.Roles;

/// <summary>One piece of evidence a scout or a judge gives: a line of a file of the repository and the text the model says is on it.</summary>
public sealed record EvidenceItem(string File, int Line, string Text);

/// <summary>What a source of files answers for a path (epoch 3 E3 s1 t1): the file is readable, or the way it is not.</summary>
public enum EvidenceFileState
{
    /// <summary>A regular file inside the tree the job read, within the size cap, in UTF-8.</summary>
    Ok,

    /// <summary>Nothing is at the path.</summary>
    Missing,

    /// <summary>The path is there and is not a regular file (a folder).</summary>
    NotAFile,

    /// <summary>The file is over <see cref="RoleEvidence.MaxFileBytes"/>.</summary>
    TooLarge,

    /// <summary>The file is not UTF-8 text.</summary>
    NotUtf8,

    /// <summary>The path runs through a link, or leaves the tree: the file is not opened and the answer is in error.</summary>
    ThroughLink,

    /// <summary>The file is there and could not be read (no permission, an IO error).</summary>
    Unreadable,
}

/// <summary>The lines of a file and the state that says whether they are there. <see cref="Lines"/> is empty unless <see cref="State"/> is <see cref="EvidenceFileState.Ok"/>.</summary>
public sealed record EvidenceFileRead(EvidenceFileState State, IReadOnlyList<string> Lines)
{
    public static EvidenceFileRead Without(EvidenceFileState state) => new(state, []);
}

/// <summary>
/// Where the lines of an evidence check come from. The pure check never touches a disk: the server hands it a source that reads through <c>RealPath</c>, the one road that resolves links, and a test hands it a table.
/// </summary>
public interface IEvidenceSource
{
    /// <summary>The state of the file at <paramref name="path"/> (repository-relative, one spelling) without reading its text.</summary>
    EvidenceFileState Probe(string path);

    /// <summary>The lines of the file at <paramref name="path"/>, or the state that says why there are none.</summary>
    EvidenceFileRead Read(string path);
}

/// <summary>The answer of one evidence check: an error of the ANSWER (the model must send it again), or a verdict on the item.</summary>
public abstract record EvidenceVerdict
{
    private EvidenceVerdict()
    {
    }

    /// <summary>The item cannot be judged: it is not an item (a blank text) or names a place that is not allowed. Goes back to the model as a repair turn; the file was never opened.</summary>
    public sealed record Error(string Sentence) : EvidenceVerdict;

    /// <summary>The item was compared with the file. <see cref="Verified"/> is true only when the line holds the text; <see cref="Why"/> says what was found.</summary>
    public sealed record Checked(bool Verified, string Why) : EvidenceVerdict;
}

/// <summary>
/// The evidence check of the scout and the judge (epoch 3 E3 s1 t1; spec A): an item is VERIFIED when the file is in the tree the job read and that line holds <c>text</c> once white space is collapsed.
/// </summary>
/// <remarks>
/// <para><b>A blank text is an error before any line is read</b> (gate finding 5). Compared with a blank line, an empty or whitespace-only text would "hold" — a substring of the empty string is true — and a model could
/// verify a claim by quoting nothing. So the text is judged FIRST, in <see cref="Check"/> and again in the readers that call <see cref="BlankProblem"/>, and such an item can never be <c>verified</c>: not against a blank line,
/// not against a line of spaces, not against a line out of range, not against a file that is not there. Blank is wider than white space: zero-width and other format characters, and the few letters that draw as nothing,
/// count too (<see cref="IsBlank"/>).</para>
/// <para><b>A path is judged as text first.</b> One that is absolute, has a drive, a <c>..</c> or a <c>.git</c> segment, a control character, or is named like a credential file (<see cref="CredentialFiles"/>) is an error and
/// the source is never asked about it. A path that runs through a link is the source's to find (<see cref="EvidenceFileState.ThroughLink"/>), and is an error too.</para>
/// <para>Pure: lines arrive through <see cref="IEvidenceSource"/>. It never throws for an item that is wrong.</para>
/// </remarks>
public static class RoleEvidence
{
    /// <summary>A file over this many bytes is not read: its items are not verified.</summary>
    public const int MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>The longest evidence text an answer may carry, in characters.</summary>
    public const int MaxTextLength = 300;

    /// <summary>The longest path an answer may name.</summary>
    public const int MaxPathLength = 512;

    private const string Invisible = "⠀ㅤᅟᅠ឴឵";

    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>True when the text has nothing a person can read: empty, white space, or only characters that draw as nothing.</summary>
    public static bool IsBlank(string? text) => text is null || text.All(IsNothing);

    private static bool IsNothing(char c) => char.IsWhiteSpace(c) || Invisible.Contains(c, StringComparison.Ordinal) || char.GetUnicodeCategory(c) == UnicodeCategory.Format;

    /// <summary>The sentence of the error a blank text is, or empty. <paramref name="at"/> names the member, as the reader's problems do.</summary>
    public static string BlankProblem(string at, string? text) =>
        IsBlank(text) ? $"{at}: an evidence text is copied from the line and is never empty or only white space (a text with nothing in it proves nothing)" : string.Empty;

    /// <summary>The text with every run of white space as ONE space and the ends trimmed — how a line and a text are compared.</summary>
    public static string Collapse(string text)
    {
        var collapsed = new StringBuilder(text.Length);
        var pending = false;
        foreach (var c in text)
        {
            pending |= char.IsWhiteSpace(c);
            if (!char.IsWhiteSpace(c))
            {
                collapsed.Append(pending && collapsed.Length > 0 ? " " : string.Empty).Append(c);
                pending = false;
            }
        }

        return collapsed.ToString();
    }

    /// <summary>The path as one spelling: <c>/</c> for <c>\</c>, empty and <c>.</c> segments dropped. Meaningful only for a path <see cref="PathProblem"/> accepted.</summary>
    public static string Normal(string path) => string.Join('/', path.Split(Separators).Where(segment => segment.Length > 0 && segment != "."));

    /// <summary>The sentence that says why <paramref name="path"/> may not be opened, or empty when it may be asked of the source. Decided on the text alone.</summary>
    public static string PathProblem(string at, string path)
    {
        var shape = ShapeProblem(path);
        if (shape.Length > 0)
        {
            return $"{at}: '{Shown(path)}' {shape}";
        }

        var pattern = CredentialFiles.WhichPattern(path);

        return pattern.Length > 0 ? $"{at}: '{Shown(path)}' is named like a credential file ({pattern}); this product never reads one, so it is not opened" : string.Empty;
    }

    private static string ShapeProblem(string path)
    {
        if (path.Trim().Length == 0 || path.Length > MaxPathLength || path.Any(char.IsControl))
        {
            return $"is not a path: it is empty, longer than {MaxPathLength} characters or holds a control character";
        }

        if (path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':', StringComparison.Ordinal))
        {
            return "is not a path inside the repository: write it from the top of the repository (not absolute, no drive)";
        }

        var segments = path.Split(Separators);

        return segments.Contains("..") || segments.Contains(".git", StringComparer.OrdinalIgnoreCase)
            ? "leaves the repository's own files: no `..` segment and no `.git` folder"
            : string.Empty;
    }

    /// <summary>
    /// One item judged: the blank text first, the path next, then the file. The source is asked ONLY for an item whose text is not blank and whose path is allowed.
    /// </summary>
    public static EvidenceVerdict Check(EvidenceItem item, IEvidenceSource source, string at = "evidence")
    {
        if (BlankProblem(at + ".text", item.Text) is { Length: > 0 } blank)
        {
            return new EvidenceVerdict.Error(blank);
        }

        if (PathProblem(at + ".file", item.File) is { Length: > 0 } path)
        {
            return new EvidenceVerdict.Error(path);
        }

        return Compared(item, source.Read(Normal(item.File)), at);
    }

    /// <summary>
    /// A file a scout names (<c>files</c>): an error for a path that is not allowed or runs through a link, else a verdict whose <c>Verified</c> says that the file exists as a file.
    /// </summary>
    public static EvidenceVerdict CheckFile(string path, IEvidenceSource source, string at = "files")
    {
        if (PathProblem(at, path) is { Length: > 0 } problem)
        {
            return new EvidenceVerdict.Error(problem);
        }

        return source.Probe(Normal(path)) switch
        {
            EvidenceFileState.ThroughLink => LinkError(at, path),
            EvidenceFileState.Missing => new EvidenceVerdict.Checked(false, "the file is not there"),
            EvidenceFileState.NotAFile => new EvidenceVerdict.Checked(false, "the path is not a file"),
            _ => new EvidenceVerdict.Checked(true, "the file is there"),
        };
    }

    private static EvidenceVerdict Compared(EvidenceItem item, EvidenceFileRead read, string at) => read.State switch
    {
        EvidenceFileState.ThroughLink => LinkError(at + ".file", item.File),
        EvidenceFileState.Missing => new EvidenceVerdict.Checked(false, "the file is not in the tree the job read"),
        EvidenceFileState.NotAFile => new EvidenceVerdict.Checked(false, "the path is not a file"),
        EvidenceFileState.TooLarge => new EvidenceVerdict.Checked(false, $"the file is over {MaxFileBytes} bytes, so its lines were not read"),
        EvidenceFileState.NotUtf8 => new EvidenceVerdict.Checked(false, "the file is not UTF-8 text, so its lines were not read"),
        EvidenceFileState.Unreadable => new EvidenceVerdict.Checked(false, "the file could not be read"),
        _ => OnTheLine(item, read.Lines),
    };

    private static EvidenceVerdict LinkError(string at, string path) =>
        new EvidenceVerdict.Error($"{at}: '{Shown(path)}' runs through a link, so it is not opened; name the real path of the file");

    private static EvidenceVerdict OnTheLine(EvidenceItem item, IReadOnlyList<string> lines)
    {
        if (item.Line < 1 || item.Line > lines.Count)
        {
            return new EvidenceVerdict.Checked(false, $"line {item.Line} is not in the file (it has {lines.Count} line{(lines.Count == 1 ? string.Empty : "s")})");
        }

        return Collapse(lines[item.Line - 1]).Contains(Collapse(item.Text), StringComparison.Ordinal)
            ? new EvidenceVerdict.Checked(true, "the line holds the text")
            : new EvidenceVerdict.Checked(false, $"line {item.Line} does not hold the text");
    }

    private static string Shown(string text)
    {
        var line = new string([.. text.Select(c => char.IsControl(c) ? '?' : c)]);

        return line.Length <= 120 ? line : line[..120] + "…";
    }
}
