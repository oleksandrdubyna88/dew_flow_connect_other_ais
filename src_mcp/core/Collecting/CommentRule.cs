using System.Globalization;

namespace CoaiMcp.Core.Collecting;

/// <summary>
/// What a contributor's comment may contain — one rule, in the core both halves compile against.
/// </summary>
/// <remarks>
/// <para><b>A comment is PUBLIC and nothing scrubs it</b> (the operator's decision, 2026-09-18): it
/// goes to the server verbatim, for storage and later processing. So this is not a sanitiser. It
/// REFUSES, naming what it refused, and the pair carrying it is refused with it — because a
/// truncated or scrubbed comment is text the person did not write, and sending that on their behalf
/// is worse than telling them no.</para>
/// <para><b>The alphabet does not run over a comment.</b> <see cref="Alphabet"/> guards the two
/// skeletons, where an identifier that survived normalisation would be somebody's private code
/// leaking into a public corpus. A comment has no normaliser and needs none; a test asserts that a
/// word the alphabet refuses in a skeleton is ACCEPTED in a comment, so that the decision cannot be
/// undone by somebody helpfully widening the whitelist.</para>
/// <para><b>What is refused, and why each one.</b> Only what breaks a reader, a terminal or a log:
/// <list type="bullet">
///   <item>C0 and C1 controls other than LF and TAB — NUL truncates `sqlite3`'s own output of a TEXT
///   column, and the rest corrupt the listing a person reads to decide what to promote.</item>
///   <item>The bidirectional controls — a comment that renders its own text backwards or hides part
///   of it is the "Trojan Source" shape, and a corpus shown to people as precedent is worth
///   poisoning.</item>
///   <item>Unpaired surrogates, which are not scalar values and cannot be encoded as UTF-8 at all.
///   </item>
/// </list>
/// Emoji, CJK, U+2028/U+2029 and every punctuation mark are fine. So is LF: a person writing two
/// sentences about a defect will press Enter, and refusing that would be the product arguing with
/// the one thing it asked for. `--waiting` prints the first line and says there is more.</para>
/// </remarks>
public static class CommentRule
{
    /// <summary>
    /// The most UTF-16 code units a comment may carry.
    /// </summary>
    /// <remarks>
    /// <para>UTF-16 code units — <c>string.Length</c> here, <c>.length</c> in the page's script —
    /// because it is the one measure both halves already share, so the box's <c>maxlength</c> counts
    /// exactly what this counts and no encoding arithmetic crosses the boundary.</para>
    /// <para><b>The arithmetic.</b> A request is bounded at 200 pairs and 1 MiB. A median pair is
    /// 762 B; a 1 000-unit comment is at most 3 000 B of UTF-8. So a full batch of worst-case
    /// comments is ≈ 752 KB, inside the cap with room for skeletons well above the median. At 2 000
    /// the same batch is ~1.15 MB and does not fit, which is why it is not 2 000.</para>
    /// <para><b>It is a DESIGN number, not a measurement</b> — no real comment exists yet. After the
    /// first fifty the median is read off `quarantine` and this is revisited.</para>
    /// </remarks>
    public const int MostChars = 1000;

    /// <summary>Why this comment may not be stored, or empty when it may.</summary>
    public static string Refuse(string comment)
    {
        if (comment.Length > MostChars)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"the comment is {comment.Length} characters; at most {MostChars} are taken");
        }

        var at = FirstUnwanted(comment);

        return at < 0 ? string.Empty : Named(comment, at);
    }

    /// <summary>Where the first character this will not take is, or -1.</summary>
    private static int FirstUnwanted(string comment)
    {
        for (var at = 0; at < comment.Length; at++)
        {
            if (Unwanted(comment, at))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>Whether the unit at this position is one of the three refused kinds.</summary>
    private static bool Unwanted(string comment, int at)
    {
        var one = comment[at];

        return Controlling(one) || Reordering(one) || Lonely(comment, at);
    }

    /// <summary>A C0 or C1 control that is not LF or TAB.</summary>
    private static bool Controlling(char one) =>
        one is not '\n' and not '\t'
        && (one <= '\u001f' || one is >= '\u007f' and <= '\u009f');

    /// <summary>A bidirectional control, which can make text read as something it is not.</summary>
    /// <remarks>
    /// <b>Written as escapes, and that is not a style choice.</b> These characters were first
    /// spelled as themselves, which compiled and worked and was indefensible: every literal here
    /// renders as an empty pair of quotes, so nobody reading this method — or its diff — could see
    /// what it matches, and three reviewers in one round read it as broken. A guard against
    /// invisible characters must not be written in invisible characters. It is also fragile in a way
    /// that costs nothing to avoid: an editor, a formatter or a copy through a tool that normalises
    /// or strips these would silently empty the whitelist, and every test here would still pass on
    /// the ranges that survived.
    /// <list type="bullet">
    ///   <item>U+061C ARABIC LETTER MARK, U+200E LEFT-TO-RIGHT MARK, U+200F RIGHT-TO-LEFT MARK</item>
    ///   <item>U+202A–U+202E the embedding and override block, which is the Trojan Source shape</item>
    ///   <item>U+2066–U+2069 the isolates</item>
    /// </list>
    /// </remarks>
    private static bool Reordering(char one) =>
        one is '؜' or '‎' or '‏'
            or >= '‪' and <= '‮'
            or >= '⁦' and <= '⁩';

    /// <summary>A surrogate with no partner, which is not a character at all.</summary>
    /// <remarks>
    /// The two directions are their own methods rather than one stacked expression, which put this
    /// over the cyclomatic bound the C# doctrine sets at four — and made the one line that has to be
    /// read carefully the hardest one here to read.
    /// </remarks>
    private static bool Lonely(string comment, int at) =>
        char.IsHighSurrogate(comment[at])
            ? NothingLowAfter(comment, at)
            : char.IsLowSurrogate(comment[at]) && NothingHighBefore(comment, at);

    /// <summary>A high surrogate at the end, or followed by something that cannot complete it.</summary>
    private static bool NothingLowAfter(string comment, int at) =>
        at + 1 >= comment.Length || !char.IsLowSurrogate(comment[at + 1]);

    /// <summary>A low surrogate at the start, or preceded by something that cannot have begun it.</summary>
    private static bool NothingHighBefore(string comment, int at) =>
        at == 0 || !char.IsHighSurrogate(comment[at - 1]);

    /// <summary>The refusal, naming the code point rather than printing it.</summary>
    private static string Named(string comment, int at) =>
        string.Create(CultureInfo.InvariantCulture,
            $"the comment holds U+{(int)comment[at]:X4}, which is not taken");
}
