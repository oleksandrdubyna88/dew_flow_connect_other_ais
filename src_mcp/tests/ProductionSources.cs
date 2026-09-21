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
    internal static Dictionary<string, int> FilesMentioning(string text) => FilesWhere(CodeOf, text);

    /// <summary>Every production file that SPELLS the text, string literals included.</summary>
    internal static Dictionary<string, int> FilesSpelling(string text) => FilesWhere(SpellingOf, text);

    private static Dictionary<string, int> FilesWhere(Func<string, string> view, string text)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Files())
        {
            var times = Occurrences(view(file), text);
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
    /// <para>What it does not handle: interpolation holes inside a string, whose code is removed with
    /// the rest of the string. Nothing is guarded by a count, so a call written only inside an
    /// interpolation would be missed by the numbers and not by the boundary.</para>
    /// </remarks>
    private static string WithoutComments(string source, bool keepText)
    {
        var kept = new StringBuilder(source.Length);
        for (var at = 0; at < source.Length;)
        {
            at = source[at] switch
            {
                '/' when Next(source, at) == '/' => SkipTo(source, at, "\n", kept, keepEnd: true),
                '/' when Next(source, at) == '*' => SkipTo(source, at + 2, "*/", kept, keepEnd: false),
                '"' or '\'' or '@' or '$' => Text(source, at, kept, keepText),
                _ => Keep(source, at, kept),
            };
        }

        return kept.ToString();
    }

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

    /// <summary>A string or character literal: where it ends, and how much of it survives.</summary>
    private static int Text(string source, int at, StringBuilder kept, bool keepText)
    {
        var quote = at;
        while (quote < source.Length && source[quote] is '@' or '$')
        {
            quote++;
        }

        if (quote >= source.Length || source[quote] is not ('"' or '\''))
        {
            // A `$` or `@` that begins no literal — an identifier, or an interpolation's own brace.
            return Keep(source, at, kept);
        }

        var delimiter = source[quote];
        var run = RunOf(source, quote, delimiter);

        // An interpolation hole is not CONTENTS — it is code that runs. Dropping it with the text
        // around it is how `$"{new ErrorAnswer(why)}"` hides from a boundary rule that reads source,
        // which is the guarantee story 2.2 rests on. (The second code round, codex.) The holes are
        // collected as they are walked and spliced back between the fences.
        var holes = run < 3 && IsInterpolated(source, at) ? new StringBuilder() : null;
        var end = run >= 3
            ? EndOfRaw(source, quote + run, delimiter, run)
            : EndOfLiteral(source, quote + 1, delimiter, IsVerbatim(source, at), holes);

        Surviving(kept, source, at, end, delimiter, run, keepText, holes);

        return end;
    }

    /// <summary>The whole literal, or the fences it was written between with its holes inside.</summary>
    private static void Surviving(
        StringBuilder kept, string source, int at, int end, char delimiter, int run, bool keepText,
        StringBuilder? holes)
    {
        if (keepText)
        {
            kept.Append(source, at, end - at);

            return;
        }

        var fence = run >= 3 ? run : 1;
        kept.Append(delimiter, fence);
        if (holes is not null)
        {
            kept.Append(holes);
        }

        kept.Append(delimiter, fence);
    }

    /// <summary>Whether <c>{…}</c> is a hole here — <c>$"…"</c>, <c>$@"…"</c>, <c>@$"…"</c>.</summary>
    private static bool IsInterpolated(string source, int at) =>
        source[at] == '$' || (source[at] == '@' && Next(source, at) == '$');

    /// <summary>How many of the delimiter in a row — three or more opens a raw string.</summary>
    private static int RunOf(string source, int quote, char delimiter)
    {
        var run = 0;
        while (quote + run < source.Length && source[quote + run] == delimiter)
        {
            run++;
        }

        return run;
    }

    /// <summary>
    /// Whether a backslash is an ordinary character here — <c>@"…"</c>, <c>$@"…"</c>, <c>@$"…"</c>.
    /// </summary>
    /// <remarks>
    /// An interpolated string used to count as verbatim, which was wrong and could end a literal
    /// early: <c>$"a\"b"</c> escapes its quote exactly as an ordinary string does. It never mattered
    /// while one view existed, because a mis-ended literal only moved where the CONTENTS stopped being
    /// dropped; it matters now that a view keeps them.
    /// </remarks>
    private static bool IsVerbatim(string source, int at) =>
        source[at] == '@' || (source[at] == '$' && Next(source, at) == '@');

    private static int EndOfRaw(string source, int from, char delimiter, int run)
    {
        var at = source.IndexOf(new string(delimiter, run), from, StringComparison.Ordinal);

        return at < 0 ? source.Length : at + run;
    }

    /// <summary>
    /// Where a one-fence literal ends, collecting its interpolation holes on the way if asked.
    /// </summary>
    /// <remarks>
    /// <paramref name="holes"/> is null for a literal that has none, and the walk is then the plain
    /// one it always was. When it is not null every <c>{…}</c> is handed to <see cref="Hole"/>, which
    /// copies the code out — and, because a hole may itself hold a literal, that walk is where
    /// <c>$"{map["k"]}"</c> stops ending at the quote before <c>k</c>.
    /// </remarks>
    private static int EndOfLiteral(
        string source, int from, char delimiter, bool verbatim, StringBuilder? holes)
    {
        for (var at = from; at < source.Length;)
        {
            if (holes is not null && source[at] == '{')
            {
                // `{{` is how C# writes a literal brace, so what follows it is text.
                at = Next(source, at) == '{' ? at + 2 : Hole(source, at + 1, holes);
                continue;
            }

            if (!verbatim && source[at] == '\\')
            {
                at += 2;
                continue;
            }

            if (source[at] != delimiter)
            {
                at++;
                continue;
            }

            if (verbatim && Next(source, at) == delimiter)
            {
                at += 2;
                continue;
            }

            return at + 1;
        }

        return source.Length;
    }

    /// <summary>One <c>{…}</c> hole, copied out as the code it is — nested braces and literals too.</summary>
    private static int Hole(string source, int from, StringBuilder holes)
    {
        var depth = 1;
        var at = from;
        while (at < source.Length)
        {
            depth += source[at] switch { '{' => 1, '}' => -1, _ => 0 };
            if (depth == 0)
            {
                // The code ran; a space keeps it from gluing onto whatever follows the literal.
                holes.Append(' ');

                return at + 1;
            }

            at = source[at] is '"' or '\'' or '@' or '$'
                ? Text(source, at, holes, keepText: false)
                : Keep(source, at, holes);
        }

        return source.Length;
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
