using CoaiMcp.Core.Outlining;

namespace CoaiMcp.Core.Feature;

/// <summary>What happened to a file between the base and the head.</summary>
public enum FileChange
{
    Added,
    Modified,
    Deleted,
    Renamed,
}

/// <summary>One file the range changed, as git counted it.</summary>
/// <param name="Path">Where the file is at head (for a deletion, where it was).</param>
/// <param name="RenamedFrom">Where it was at the base, or empty when it did not move.</param>
/// <param name="Added">Lines added — 0 for a binary, which has no lines.</param>
/// <param name="Deleted">Lines deleted — 0 for a binary.</param>
public sealed record ChangedFile(string Path, string RenamedFrom, FileChange Change, int Added, int Deleted, bool IsBinary)
{
    /// <summary>The order files are read, outlined and cut in: the larger change first.</summary>
    public int Size => Added + Deleted;

    /// <summary>The one letter a reviewer sees — the letters git itself uses.</summary>
    public char Letter => Change switch
    {
        FileChange.Added => 'A',
        FileChange.Deleted => 'D',
        FileChange.Renamed => 'R',
        FileChange.Modified => 'M',
        _ => throw new ArgumentOutOfRangeException(nameof(Change), Change, "a change this build does not know"),
    };

    /// <summary>Largest change first, then by path — the ONE order every list of files here uses.</summary>
    public static IReadOnlyList<ChangedFile> Ordered(IEnumerable<ChangedFile> files) => Ordered(files, f => f);

    /// <summary>The same order, for anything that carries a changed file.</summary>
    public static IReadOnlyList<T> Ordered<T>(IEnumerable<T> items, Func<T, ChangedFile> file) =>
        [.. items.OrderByDescending(i => file(i).Size).ThenBy(i => file(i).Path, StringComparer.Ordinal)];
}

/// <summary>A changed file that was not outlined, and why — named, never silently dropped.</summary>
/// <param name="Bytes">Its size at head, or <see cref="Unknown"/> when there is none to report (a deletion).</param>
/// <param name="Lines">Its line count, or <see cref="Unknown"/> when it was never read — only a file that was read has lines.</param>
public sealed record NotOutlined(string Path, string Reason, long Bytes = NotOutlined.Unknown, int Lines = NotOutlined.Unknown)
{
    /// <summary>"Not measured" — never rendered as a number, because absent is not zero.</summary>
    public const int Unknown = -1;
}

/// <summary>A 1-based, inclusive span of lines at head.</summary>
public readonly record struct LineSpan(int Start, int End)
{
    public bool Intersects(int start, int end) => Start <= end && start <= End;
}

/// <summary>One line of a unified diff with three lines of context, placed at its line at head.</summary>
/// <param name="Hunk">Which hunk of its file it came from, 0-based.</param>
/// <param name="Kind"><c>' '</c> context, <c>'+'</c> added, <c>'-'</c> deleted.</param>
/// <param name="NewLine">Its line at head; a deleted line sits at the head line that follows it.</param>
/// <param name="Text">The line as git printed it, marker included, without a trailing carriage return.</param>
public sealed record DiffLine(int Hunk, char Kind, int NewLine, string Text);

/// <summary>A file that was read and outlined, with what the diff says changed in it.</summary>
/// <param name="Changed">The head-side spans of <c>git diff -U0</c> — what the <c>*</c> marks are drawn from.</param>
/// <param name="Hunks">Every line of <c>git diff -U3</c> for this file — what the member hunks are cut from.</param>
public sealed record OutlinedFile(
    ChangedFile File,
    SourceOutline Outline,
    IReadOnlyList<LineSpan> Changed,
    IReadOnlyList<DiffLine> Hunks);

/// <summary>A changed member whose hunks did not fit, named by its outline span.</summary>
public sealed record CutHunk(string Path, int Start, int End, int Added, int Deleted);

/// <summary>A large file whose unchanged members were collapsed to fit.</summary>
public sealed record CollapsedFile(string Path, int MembersElided);

/// <summary>Everything the outline left out, kept whole — rendered outside the outline's budget.</summary>
/// <param name="Dropped">Outlined files dropped whole for the budget, smallest change first.</param>
/// <param name="Notes">One sentence each for what is left out that is not a file — a diff read short, for one.</param>
public sealed record FeatureOmissions(
    IReadOnlyList<NotOutlined> NotOutlined,
    IReadOnlyList<CollapsedFile> Collapsed,
    IReadOnlyList<string> Dropped,
    IReadOnlyList<CutHunk> CutHunks,
    IReadOnlyList<string> Notes)
{
    public static readonly FeatureOmissions None = new([], [], [], [], []);
}

/// <summary>What a feature reviewer is shown of the code: the range, the outline section, and what was left out.</summary>
/// <param name="BaseSha">The commit the range was compared against.</param>
/// <param name="BaseNote">Empty when that is the merge base; otherwise the sentence saying what it is instead.</param>
/// <param name="Section">The outlines and the member hunks together — never more than the outline budget.</param>
/// <param name="Outlined">How many files the section outlines.</param>
public sealed record FeatureOutline(
    string BaseSha,
    string HeadSha,
    string BaseNote,
    IReadOnlyList<ChangedFile> Files,
    string Section,
    int Outlined,
    FeatureOmissions Omissions);
