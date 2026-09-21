using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace CoaiMcp.Tests;

/// <summary>
/// Every production source file, and what its CODE says — the one scanner the censuses share.
/// </summary>
/// <remarks>
/// <para>Built for <c>TheOneAppendTests</c> in story 1.4 and extracted here when story 2.1 wanted the
/// same thing for the refusal roads. Copying it would have been the defect <c>reuse-first.md</c> is
/// about, and a worse one than usual: every rule below was earned by a bypass somebody found, and a
/// second copy would have been the version that still has them.</para>
///
/// <para><b>Comments and string literals are REMOVED before anything is searched</b> — story 2.1's
/// code round, four reviewers. The first version skipped a line whose trimmed text began with
/// <c>//</c> and nothing else, so a <c>/* … */</c> block holding a commented-out call counted as a
/// call, a trailing <c>// new ErrorAnswer(</c> counted as one, and a sentence in a string literal
/// counted as one. A census that counts prose is the defect it exists to end, in a new place.</para>
///
/// <para><b>Lines are joined so that a wrapped MEMBER glues and a wrapped WORD does not.</b> The
/// first version joined with nothing at all, so <c>Type⏎.Method(</c> became <c>Type.Method(</c> —
/// which is what it was for — and <c>using⏎static</c> became <c>usingstatic</c>, which is what a
/// guard then failed to see. A line that begins with <c>.</c> is glued to the one before it;
/// everything else is separated by one space. (The code round, gemini.)</para>
///
/// <para><b>The roots are DISCOVERED, never listed.</b> A hand-written list of paths does not visit a
/// project added tomorrow, so a new write road or refusal road inside it leaves every census green.
/// Every <c>src_*</c> directory is scanned and what is EXCLUDED is named instead, which is the
/// smaller and more stable set. (Story 1.4's code round, codex.)</para>
///
/// <para><b>It is read once per run.</b> Each census asks several questions and the first version
/// re-read and re-joined the whole tree for every one of them. The answers are cached: the files are
/// what they were when the suite started, and a test that changes production source while it runs
/// would be a different problem. (The code round, both reviewers.)</para>
/// </remarks>
internal static class ProductionSources
{
    /// <summary>What is NOT production, by the name of a directory on the way to a file.</summary>
    private static readonly string[] NotProduction =
    [
        "bin", "obj", "node_modules", "out", "dist", "TestResults", ".vscode-test",
    ];

    private static readonly Lazy<IReadOnlyList<string>> AllFiles = new(Discover, isThreadSafe: true);

    private static readonly ConcurrentDictionary<string, string> Code = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, string> Spellings = new(StringComparer.Ordinal);

    private static readonly Lazy<string> Root = new(FindRepositoryRoot, isThreadSafe: true);

    /// <summary>
    /// The checkout this test assembly was built from.
    /// </summary>
    /// <remarks>
    /// It lives here rather than on one of the test classes that happens to have needed it first: a
    /// shared scanner that cannot find its own root without a particular test class existing is a
    /// utility with a hidden owner. (The code round, codex.)
    /// </remarks>
    internal static string RepositoryRoot() => Root.Value;

    /// <summary>Every <c>src_*</c> directory this repository has — found, never listed.</summary>
    internal static IEnumerable<string> Roots() =>
        Directory.EnumerateDirectories(RepositoryRoot(), "src_*");

    /// <summary>Every production source file, by repository-relative path.</summary>
    internal static IReadOnlyList<string> Files() => AllFiles.Value;

    /// <summary>One production file's CODE — no comments, no string contents, one line.</summary>
    internal static string CodeOf(string relative) => Code.GetOrAdd(relative, path => Joined(Read(path)));

    /// <summary>One production file's code AND its string literals — no comments, one line.</summary>
    /// <remarks>
    /// The other view, for the other kind of question. Removing string contents is what stops a census
    /// of CALLS counting a sentence that mentions one — and it is exactly wrong for a census of a
    /// NAME, because a name in this codebase IS a literal. Story 2.1's lexer took
    /// <c>"server-notices.jsonl"</c> away from the two guards in <see cref="TheOneAppendTests"/> that
    /// hold that file name to one place, and they went red: which is what those guards' companion
    /// assertions are for, and the reason every census here has one. Comments are still gone from this
    /// view — a file that merely TALKS about the name is not a second spelling of it.
    /// </remarks>
    internal static string SpellingOf(string relative) =>
        Spellings.GetOrAdd(relative, path => JoinLines(WithoutComments(Read(path), keepText: true)));

    /// <summary>The same rule over text a test supplies, so the scanner's own behaviour is testable.</summary>
    internal static string Joined(string source) => JoinLines(WithoutComments(source, keepText: false));

    /// <summary>The same, keeping the literals — the spelling view, over text a test supplies.</summary>
    internal static string JoinedWithText(string source) =>
        JoinLines(WithoutComments(source, keepText: true));

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), relative));

    /// <summary>Every production file whose CODE contains the text, and how many times.</summary>
    internal static Dictionary<string, int> FilesMentioning(string text) =>
        FilesWhere(CodeOf, code => Occurrences(code, text));

    /// <summary>Every production file that SPELLS the text, string literals included.</summary>
    internal static Dictionary<string, int> FilesSpelling(string text) =>
        FilesWhere(SpellingOf, code => Occurrences(code, text));

    /// <summary>
    /// Every production file whose CODE matches the pattern, and how many times.
    /// </summary>
    /// <remarks>
    /// For the shapes that a space can be written into. <c>new ErrorAnswer(</c> searched as characters
    /// misses <c>new  ErrorAnswer (why)</c> and a construction wrapped after <c>new</c>, neither of
    /// which changes any behaviour — so a census that counts them can be made wrong by a formatter.
    /// (CodeRabbit, on the pull request.) The TYPE-name rule beside it is unaffected either way, which
    /// is why this sharpens a count rather than closing a hole.
    /// </remarks>
    internal static Dictionary<string, int> FilesMatching(Regex pattern) => FilesWhere(CodeOf, pattern.Count);

    private static Dictionary<string, int> FilesWhere(Func<string, string> view, Func<string, int> count)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Files())
        {
            var times = count(view(file));
            if (times > 0)
            {
                found[file] = times;
            }
        }

        return found;
    }

    /// <summary>
    /// How many times a method is CALLED unqualified — <c>Error(</c>, never <c>_log.Error(</c>.
    /// </summary>
    /// <remarks>
    /// <para>A whole-file search for <c>Error(</c> counts every logger call, and this codebase logs
    /// constantly. A call is an occurrence whose preceding character is neither a dot nor part of an
    /// identifier.</para>
    /// <para>The DECLARATION is subtracted rather than pattern-matched on its return type. The first
    /// version looked for the seven characters <c>string </c> before the name, which the code round
    /// was right to call brittle: change the helper to return <c>Task&lt;string&gt;</c> and the
    /// declaration starts counting as a call, moving a number the documents quote. A declaration is
    /// the occurrence whose argument list begins with a TYPE and a name — and with string literals
    /// already removed, <c>Error(string …</c> can only be one.</para>
    /// <para>The spacing is not part of the rule. The second code round was right that matching the
    /// exact characters <c>(string </c> makes <c>Error(  string sentence)</c> — a change that alters
    /// no behaviour and that no formatter forbids — count the declaration as a refusal, which moves a
    /// number the documents quote.</para>
    /// </remarks>
    internal static int UnqualifiedCalls(string code, string method)
    {
        var wanted = method + "(";
        var calls = 0;
        for (var at = code.IndexOf(wanted, StringComparison.Ordinal); at >= 0;
             at = code.IndexOf(wanted, at + wanted.Length, StringComparison.Ordinal))
        {
            if (at > 0 && !IsQualifiedOrPartOfAName(code[at - 1]))
            {
                calls++;
            }
        }

        return calls - Declarations(code, method);
    }

    /// <summary>The occurrences that are the helper being DECLARED, not called.</summary>
    private static int Declarations(string code, string method) =>
        Regex.Count(code, Regex.Escape(method) + @"\(\s*string\b", RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));

    /// <summary>How many times the text appears, counting overlaps as one each.</summary>
    internal static int Occurrences(string code, string text)
    {
        var count = 0;
        for (var at = code.IndexOf(text, StringComparison.Ordinal); at >= 0;
             at = code.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Whether a repository-relative path is production: not a test project, not build output.
    /// </summary>
    /// <remarks>
    /// A directory whose name STARTS with <c>tests</c> covers <c>tests</c>, <c>tests_fakecli</c> and
    /// <c>tests_notices</c>; one that ENDS with <c>.Tests</c> covers <c>CoaiBench.Tests</c>. That is
    /// every test project here and the shape a new one will have, said as a rule rather than as a
    /// list, for the reason the roots are found rather than listed.
    /// </remarks>
    internal static bool IsProduction(string relative) =>
        !relative.Split('/').Any(segment =>
            segment.StartsWith("tests", StringComparison.OrdinalIgnoreCase)
            || segment.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || NotProduction.Contains(segment, StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Discover()
    {
        var root = RepositoryRoot();
        var found = new List<string>();
        foreach (var source in Directory.EnumerateDirectories(root, "src_*"))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsProduction(relative))
                {
                    found.Add(relative);
                }
            }
        }

        return found;
    }

    private static bool IsQualifiedOrPartOfAName(char before) =>
        before == '.' || char.IsLetterOrDigit(before) || before == '_';

    /// <summary>
    /// The source with every comment removed, and the CONTENTS of every string removed with them
    /// unless <paramref name="keepText"/> asks to keep them.
    /// </summary>
    /// <remarks>
    /// <para>One pass, because a lexer is the only honest way to tell a <c>//</c> inside a string
    /// from a comment and a quote inside a comment from a string. Line comments, block comments,
    /// ordinary strings with their escapes, verbatim strings with their doubled quotes, raw strings
    /// and character literals are all handled. With <paramref name="keepText"/> false the delimiters
    /// are KEPT and only what is between them goes, so that <c>Error("x")</c> stays a call with an
    /// argument; with it true the literal survives whole, which is what a census of a NAME needs.</para>
    /// <para>An interpolation HOLE survives either way, because it is not contents — it is code that
    /// runs, and dropping it is how <c>$"{new ErrorAnswer(why)}"</c> hid from the boundary rule story
    /// 2.2 rests on. (The second code round, codex; raw interpolation added after CodeRabbit read the
    /// pull request.)</para>
    /// </remarks>
    private static string WithoutComments(string source, bool keepText)
    {
        var kept = new StringBuilder(source.Length);
        for (var at = 0; at < source.Length;)
        {
            at = Lex(source, at, kept, keepText);
        }

        return kept.ToString();
    }

    /// <summary>What this character begins: a comment, a literal, or nothing in particular.</summary>
    private static int Lex(string source, int at, StringBuilder kept, bool keepText) => source[at] switch
    {
        '/' when Next(source, at) == '/' => SkipTo(source, at, "\n", kept, keepEnd: true),
        '/' when Next(source, at) == '*' => SkipTo(source, at + 2, "*/", kept, keepEnd: false),
        '"' or '\'' or '@' or '$' => Text(source, at, kept, keepText),
        _ => Keep(source, at, kept),
    };

    private static char Next(string source, int at) => at + 1 < source.Length ? source[at + 1] : '\0';

    private static int Keep(string source, int at, StringBuilder kept)
    {
        kept.Append(source[at]);

        return at + 1;
    }

    /// <summary>Skips to the end marker, keeping the newline a line comment ends at.</summary>
    private static int SkipTo(string source, int from, string end, StringBuilder kept, bool keepEnd)
    {
        var at = source.IndexOf(end, from, StringComparison.Ordinal);
        if (at < 0)
        {
            return source.Length;
        }

        // A block comment separates what is on either side of it, exactly as a space does.
        kept.Append(keepEnd ? end : " ");

        return at + end.Length;
    }

    /// <summary>
    /// How a literal is written: what closes it, and what opens a hole inside it.
    /// </summary>
    /// <param name="Delimiter">The quote character — <c>"</c> or <c>'</c>.</param>
    /// <param name="Run">How many of it open and close the literal; three or more is a raw string.</param>
    /// <param name="Verbatim">An <c>@</c> prefix: a backslash is an ordinary character and a doubled
    /// quote is one quote.</param>
    /// <param name="Braces">How many braces open an interpolation hole — the number of <c>$</c>
    /// prefixes, and zero when the literal has none. C# says one <c>$</c> takes <c>{…}</c> and two
    /// take <c>{{…}}</c>, which is why a LITERAL brace is written twice that many.</param>
    private readonly record struct Fence(char Delimiter, int Run, bool Verbatim, int Braces)
    {
        /// <summary>Whether a backslash escapes here. It does not in a verbatim or a raw string.</summary>
        internal bool Escapes => Run == 1 && !Verbatim;
    }

    /// <summary>A string or character literal: where it ends, and how much of it survives.</summary>
    private static int Text(string source, int at, StringBuilder kept, bool keepText)
    {
        var quote = Quote(source, at);
        if (quote >= source.Length || source[quote] is not ('"' or '\''))
        {
            // A `$` or `@` that begins no literal — an identifier, or an interpolation's own brace.
            return Keep(source, at, kept);
        }

        var fence = FenceAt(source, at, quote);
        var holes = HolesOf(fence);
        var end = EndOfLiteral(source, quote + fence.Run, fence, holes);

        Surviving(kept, source, at, end, fence, keepText, holes);

        return end;
    }

    /// <summary>Past the <c>$</c> and <c>@</c> prefixes, to the quote they belong to.</summary>
    private static int Quote(string source, int at)
    {
        var quote = at;
        while (quote < source.Length && source[quote] is '@' or '$')
        {
            quote++;
        }

        return quote;
    }

    /// <summary>The fence of the literal that starts at <paramref name="at"/> and quotes at <paramref name="quote"/>.</summary>
    private static Fence FenceAt(string source, int at, int quote)
    {
        var prefix = source.AsSpan(at, quote - at);

        return new Fence(source[quote], FenceRun(source, quote), prefix.Contains('@'), prefix.Count('$'));
    }

    /// <summary>
    /// How many quotes open this literal: three or more is a raw string, and anything else is one.
    /// </summary>
    /// <remarks>
    /// The two-quote case is why this is not just <see cref="Run"/>: <c>""</c> is the EMPTY string, not
    /// a fence of two. Reading it as a fence made the lexer hunt for the next two quotes in a row and
    /// swallow everything up to them — which is how one rewrite of this file quietly lost
    /// <c>UsageLedger.cs</c> from the append census, caught by that census's companion assertion.
    /// </remarks>
    private static int FenceRun(string source, int quote)
    {
        var run = Run(source, quote, source[quote]);

        return run >= 3 ? run : 1;
    }

    /// <summary>Somewhere to collect the holes, when this literal can have any.</summary>
    private static StringBuilder? HolesOf(Fence fence) => fence.Braces > 0 ? new StringBuilder() : null;

    /// <summary>Where a literal ends, collecting its interpolation holes on the way.</summary>
    private static int EndOfLiteral(string source, int from, Fence fence, StringBuilder? holes)
    {
        for (var at = from; at < source.Length;)
        {
            if (Closes(source, at, fence))
            {
                return at + fence.Run;
            }

            at = Advance(source, at, fence, holes);
        }

        return source.Length;
    }

    /// <summary>Whether the fence closes here — a doubled quote in a verbatim string does not.</summary>
    private static bool Closes(string source, int at, Fence fence) =>
        Run(source, at, fence.Delimiter) >= fence.Run
        && !(fence.Verbatim && Next(source, at) == fence.Delimiter);

    /// <summary>One step: into a hole, past an escape, past a doubled quote, or over a character.</summary>
    private static int Advance(string source, int at, Fence fence, StringBuilder? holes) => source[at] switch
    {
        '{' when holes is not null => Brace(source, at, fence, holes),
        '\\' when fence.Escapes => at + 2,
        _ when source[at] == fence.Delimiter => at + 2,
        _ => at + 1,
    };

    /// <summary>A <c>{</c>: twice the brace count writes a literal brace, fewer opens a hole.</summary>
    private static int Brace(string source, int at, Fence fence, StringBuilder holes) =>
        Run(source, at, '{') >= fence.Braces * 2
            ? at + (fence.Braces * 2)
            : Hole(source, at + fence.Braces, fence, holes);

    /// <summary>One hole, copied out as the code it is — nested braces and literals included.</summary>
    private static int Hole(string source, int from, Fence fence, StringBuilder holes)
    {
        var depth = 1;
        var at = from;
        while (at < source.Length && depth > 0)
        {
            depth += Nesting(source[at]);
            at = depth == 0 ? at : Take(source, at, holes);
        }

        // The code ran; a space keeps it from gluing onto whatever follows the literal.
        return depth == 0 ? Closed(at + fence.Braces, holes) : source.Length;
    }

    private static int Nesting(char character) => character switch { '{' => 1, '}' => -1, _ => 0 };

    /// <summary>A character of a hole: a nested literal is lexed, anything else is copied.</summary>
    private static int Take(string source, int at, StringBuilder holes) =>
        source[at] is '"' or '\'' or '@' or '$'
            ? Text(source, at, holes, keepText: false)
            : Keep(source, at, holes);

    private static int Closed(int at, StringBuilder holes)
    {
        holes.Append(' ');

        return at;
    }

    /// <summary>The whole literal, or the fences it was written between with its holes inside.</summary>
    private static void Surviving(
        StringBuilder kept, string source, int at, int end, Fence fence, bool keepText, StringBuilder? holes)
    {
        if (keepText)
        {
            kept.Append(source, at, end - at);

            return;
        }

        kept.Append(fence.Delimiter, fence.Run).Append(holes).Append(fence.Delimiter, fence.Run);
    }

    /// <summary>How many of the character run together from here.</summary>
    private static int Run(string source, int from, char character)
    {
        var run = 0;
        while (from + run < source.Length && source[from + run] == character)
        {
            run++;
        }

        return run;
    }




    /// <summary>
    /// The lines as one string: a member continuation glues, everything else is separated.
    /// </summary>
    private static string JoinLines(string source)
    {
        var joined = new StringBuilder(source.Length);
        foreach (var line in source.Split('\n'))
        {
            var code = line.Trim();
            if (code.Length == 0)
            {
                continue;
            }

            if (joined.Length > 0 && code[0] != '.')
            {
                joined.Append(' ');
            }

            joined.Append(code);
        }

        return joined.ToString();
    }

    private static string FindRepositoryRoot()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, ".git"))
               && !File.Exists(Path.Combine(here.FullName, ".git")))
        {
            here = here.Parent;
        }

        return here?.FullName
            ?? throw new InvalidOperationException(
                $"no repository above {AppContext.BaseDirectory}, so no source can be scanned");
    }
}
