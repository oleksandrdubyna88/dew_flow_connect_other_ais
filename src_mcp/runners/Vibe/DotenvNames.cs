using System.Text;
using System.Text.Unicode;

namespace CoaiMcp.Runners.Vibe;

/// <summary>What one admission scan of a dotenv file came to — a closed union, and never text from the file.</summary>
public abstract record DotenvVerdict
{
    private DotenvVerdict()
    {
    }

    /// <summary>Every line is empty, a comment, or a <c>MISTRAL_API_KEY=…</c> binding.</summary>
    public sealed record Admitted : DotenvVerdict
    {
        public static readonly Admitted Instance = new();
    }

    /// <summary>The first line, in file order, that is not admitted — 1-based — and which kind of refusal it is.</summary>
    public sealed record Refused(int Line, DotenvRefusal Kind) : DotenvVerdict;
}

/// <summary>Why a line of the person's Vibe sign-in file is refused (DL2 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>): four kinds, never one.</summary>
public enum DotenvRefusal
{
    /// <summary>Invalid UTF-8 (python-dotenv cannot decode it, so Vibe would not start), or a NUL byte on this line (python-dotenv DOES read and bind it — L-V6b — but Vibe cannot put that value in its environment). Refused either way.</summary>
    NotText,

    /// <summary>The line is neither empty, nor a comment, nor <c>NAME=…</c>.</summary>
    NoBinding,

    /// <summary>The line binds a name that is not the admitted one.</summary>
    OtherName,

    /// <summary>Line 1 starts with <c>EF BB BF</c>, which python-dotenv's <c>utf-8</c> codec (not <c>utf-8-sig</c>) keeps as part of the first name.</summary>
    ByteOrderMark,
}

/// <summary>
/// The NAME grammar of the person's <c>~/.vibe/.env</c> (DL2 of <c>research/PLAN_kimi_and_vibe_on_linux.md</c>, A1 spec): which
/// variable names python-dotenv — the reader Vibe uses — would bind from the file's bytes, judged WITHOUT ever reading a value.
/// </summary>
/// <remarks>
/// <para><b>The one invariant</b> (I3): <i>coai admits the file ⇒ every name python-dotenv 1.2.2 binds from it is
/// <see cref="Admitted"/>.</i> One direction only — refusing a file python-dotenv would have read as fine is allowed; admitting one that
/// binds another name (a <c>VIBE_*</c> would configure the launch) is not. <c>shared/dotenv-name-vectors.json</c> holds the oracle's
/// answers and <c>DotenvNamesTests</c> asserts the implication over every one.</para>
/// <para><b>No value byte leaves this type</b> (I4). The verdict is a line number and an enum; no string is made from the file's bytes,
/// and a value — everything after the <c>=</c> of a binding — is never compared, stored or returned. A line a quoted value continues onto
/// is read as a line of its own (coai reads physical lines with no quote state): sound, because every binding python-dotenv makes
/// starts on a physical line that coai reads too.</para>
/// <para><b>A line break is <c>\r\n</c>, <c>\n</c> or a bare <c>\r</c>, and nothing else</b> — python-dotenv reads the file in text mode with
/// universal newlines. Splitting on <c>\n</c> alone would hide <c>VIBE_X=1</c> after a bare CR. A vertical tab, form feed, NEL or U+2028 is
/// NOT a break (python-dotenv's unquoted value swallows them); splitting there would only refuse more. The split is made on bytes before
/// decoding: 0x0A and 0x0D never occur inside a multi-byte UTF-8 sequence.</para>
/// </remarks>
public static class DotenvNames
{
    /// <summary>The one variable the person's Vibe sign-in file may bind.</summary>
    public const string Admitted = "MISTRAL_API_KEY";

    /// <summary>The cap on the file: 64 KiB. One constant — the descriptor read is asked with it and its refusal sentence derives from it.</summary>
    public const int MaxBytes = 64 * 1024;

    private static ReadOnlySpan<byte> ByteOrderMark => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> AdmittedBytes => "MISTRAL_API_KEY"u8;

    private static ReadOnlySpan<byte> ExportWord => "export"u8;

    /// <summary>
    /// Pure. The whole file's bytes (at most <see cref="MaxBytes"/>) → the verdict. Allocates no string of any line and holds nothing
    /// after it returns; the caller zeroes the buffer.
    /// </summary>
    public static DotenvVerdict Judge(ReadOnlySpan<byte> file) =>
        file.StartsWith(ByteOrderMark) ? new DotenvVerdict.Refused(1, DotenvRefusal.ByteOrderMark) : Lines(file);

    private static DotenvVerdict Lines(ReadOnlySpan<byte> file)
    {
        var at = 0;
        for (var number = 1; ; number++)
        {
            var rest = file[at..];
            var cut = LineCut.Of(rest);
            var judged = Line(rest[..cut.Length]);
            if (judged.Refused)
            {
                return new DotenvVerdict.Refused(number, judged.Kind);
            }

            if (cut.Break == 0)
            {
                return DotenvVerdict.Admitted.Instance;
            }

            at += cut.Length + cut.Break;
        }
    }

    /// <summary>Where a physical line ends in what is left of the file, and how many bytes of break follow it (0 at the end of the file).</summary>
    private readonly record struct LineCut(int Length, int Break)
    {
        public static LineCut Of(ReadOnlySpan<byte> rest)
        {
            var end = rest.IndexOfAny((byte)'\r', (byte)'\n');

            return end < 0 ? new LineCut(rest.Length, 0) : new LineCut(end, BreakAt(rest, end));
        }

        /// <summary>CRLF is one break of two bytes; a bare CR or a bare LF is one of one.</summary>
        private static int BreakAt(ReadOnlySpan<byte> rest, int end) =>
            rest[end] == '\r' && end + 1 < rest.Length && rest[end + 1] == '\n' ? 2 : 1;
    }

    /// <summary>One line's judgement: fine, or refused as one kind.</summary>
    private readonly record struct LineJudgement(bool Refused, DotenvRefusal Kind)
    {
        public static LineJudgement Fine { get; } = new(false, default);

        public static LineJudgement Because(DotenvRefusal kind) => new(true, kind);
    }

    private static LineJudgement Line(ReadOnlySpan<byte> line) =>
        IsText(line) ? Grammar(line) : LineJudgement.Because(DotenvRefusal.NotText);

    /// <summary>Valid UTF-8 and no NUL — what python-dotenv decodes and Vibe can put in its environment.</summary>
    private static bool IsText(ReadOnlySpan<byte> line) => !line.Contains((byte)0) && Utf8.IsValid(line);

    /// <summary>python-dotenv's reading of the start of a line: leading whitespace, a comment, an optional <c>export</c>, then a binding.</summary>
    private static LineJudgement Grammar(ReadOnlySpan<byte> line)
    {
        var start = SkipSpace(line, 0);

        return start == line.Length || line[start] == '#' ? LineJudgement.Fine : Binding(line, AfterExport(line, start));
    }

    /// <summary><c>export</c> plus at least one whitespace character at <paramref name="at"/>: where the name starts after them, else <paramref name="at"/>.</summary>
    private static int AfterExport(ReadOnlySpan<byte> line, int at)
    {
        var after = at + ExportWord.Length;

        return line[at..].StartsWith(ExportWord) && after < line.Length && IsSpaceAt(line, after) ? SkipSpace(line, after) : at;
    }

    /// <summary>A name, optional whitespace, then <c>=</c>; the NAME alone is compared. What follows the <c>=</c> is value and is never looked at.</summary>
    private static LineJudgement Binding(ReadOnlySpan<byte> line, int at)
    {
        var name = NameAt(line, at);

        return name.Found && EqualsFollows(line, name.After) ? Named(line[name.Start..name.End]) : LineJudgement.Because(DotenvRefusal.NoBinding);
    }

    private static bool EqualsFollows(ReadOnlySpan<byte> line, int from)
    {
        var at = SkipSpace(line, from);

        return at < line.Length && line[at] == '=';
    }

    private static LineJudgement Named(ReadOnlySpan<byte> name) =>
        name.SequenceEqual(AdmittedBytes) ? LineJudgement.Fine : LineJudgement.Because(DotenvRefusal.OtherName);

    /// <summary>A name's bytes <c>[Start, End)</c> and where reading goes on after it (past a closing quote, when it had one).</summary>
    private readonly record struct NameCut(bool Found, int Start, int End, int After);

    /// <summary>
    /// The name at <paramref name="at"/>: <c>'…'</c> closed on this line and not empty, else a run of at least one character that is
    /// not <c>=</c>, <c>#</c> or whitespace. Read without backtracking, as python-dotenv reads it (<c>export =v</c> is unreadable, not a
    /// variable called <c>export</c>).
    /// </summary>
    private static NameCut NameAt(ReadOnlySpan<byte> line, int at) =>
        at < line.Length && line[at] == '\'' ? Quoted(line, at) : Unquoted(line, at);

    private static NameCut Quoted(ReadOnlySpan<byte> line, int at)
    {
        var close = line[(at + 1)..].IndexOf((byte)'\'');

        return close > 0 ? new NameCut(true, at + 1, at + 1 + close, at + 2 + close) : new NameCut(false, at, at, at);
    }

    private static NameCut Unquoted(ReadOnlySpan<byte> line, int at)
    {
        var end = at;
        while (end < line.Length && !EndsAName(line, end))
        {
            end += RuneAt(line, end).Length;
        }

        return new NameCut(end > at, at, end, end);
    }

    private static bool EndsAName(ReadOnlySpan<byte> line, int at) => line[at] is (byte)'=' or (byte)'#' || IsSpaceAt(line, at);

    private static int SkipSpace(ReadOnlySpan<byte> line, int from)
    {
        var at = from;
        while (at < line.Length && IsSpaceAt(line, at))
        {
            at += RuneAt(line, at).Length;
        }

        return at;
    }

    private static bool IsSpaceAt(ReadOnlySpan<byte> line, int at) => IsDotenvWhitespace(RuneAt(line, at).Rune);

    /// <summary>The code point at a byte offset of a line already known to be valid UTF-8, and how many bytes it spans.</summary>
    private static (Rune Rune, int Length) RuneAt(ReadOnlySpan<byte> line, int at) =>
        Rune.DecodeFromUtf8(line[at..], out var rune, out var length) == System.Buffers.OperationStatus.Done ? (rune, length) : (Rune.ReplacementChar, 1);

    /// <summary>
    /// python-dotenv's <c>\s</c> for <c>str</c> — Python's <c>str.isspace()</c>: .NET's White_Space plus U+001C–U+001F.
    /// <b>The invariant:</b> coai's whitespace set is a SUBSET of python-dotenv's. The direction matters. A code point coai took for
    /// whitespace that python-dotenv does not would let coai read <c>MISTRAL_API_KEY&lt;c&gt;=v</c> as the admitted name while python-dotenv
    /// binds <c>MISTRAL_API_KEY&lt;c&gt;</c> — unsound. The reverse only refuses more.
    /// </summary>
    internal static bool IsDotenvWhitespace(Rune rune) => Rune.IsWhiteSpace(rune) || rune.Value is >= 0x1C and <= 0x1F;
}
