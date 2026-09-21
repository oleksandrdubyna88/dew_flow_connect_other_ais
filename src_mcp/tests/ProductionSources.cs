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
/// <para><b>The code is read as ONE string with nothing between the lines.</b> A call wrapped across
/// two lines — which a long qualified name invites — is invisible to a per-line scan. The extension's
/// own census learned this first and says so: <i>"at least one call here is split across a line break,
/// and a line-by-line scan misses it — which is one of the four the first hand count was out by"</i>.
/// The join has no separator, because joining with a space leaves <c>Type .Method(</c>, which
/// contains no spelling anybody searches for.</para>
///
/// <para><b>The roots are DISCOVERED, never listed.</b> A hand-written list of paths does not visit a
/// project added tomorrow, so a new write road or refusal road inside it leaves every census green.
/// Every <c>src_*</c> directory is scanned and what is EXCLUDED is named instead, which is the
/// smaller and more stable set. (Story 1.4's code round, codex.)</para>
///
/// <para><b>Comments are skipped</b>, so a sentence ABOUT a call is not a call — and a line whose
/// trimmed text starts with <c>//</c> covers the XML docs too, which begin with three.</para>
/// </remarks>
internal static class ProductionSources
{
    /// <summary>What is NOT production, by the name of a directory on the way to a file.</summary>
    private static readonly string[] NotProduction =
    [
        "bin", "obj", "node_modules", "out", "dist", "TestResults", ".vscode-test",
    ];

    /// <summary>Every <c>src_*</c> directory this repository has — found, never listed.</summary>
    internal static IEnumerable<string> Roots() =>
        Directory.EnumerateDirectories(NoSourceFileCarriesAControlByteTests.RepositoryRoot(), "src_*");

    /// <summary>Every production source file, by repository-relative path.</summary>
    internal static IEnumerable<string> Files()
    {
        var root = NoSourceFileCarriesAControlByteTests.RepositoryRoot();
        foreach (var source in Roots())
        {
            foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsProduction(relative))
                {
                    yield return relative;
                }
            }
        }
    }

    /// <summary>One production file's code, comments dropped and newlines collapsed to nothing.</summary>
    internal static string CodeOf(string relative) =>
        Joined(File.ReadAllLines(Path.Combine(NoSourceFileCarriesAControlByteTests.RepositoryRoot(), relative)));

    /// <summary>The code lines, trimmed and joined with NOTHING between them. See the remarks above.</summary>
    internal static string Joined(IEnumerable<string> lines) =>
        string.Concat(lines.Select(line => line.Trim()).Where(IsCode));

    /// <summary>Every production file whose CODE contains the text, and how many times.</summary>
    internal static Dictionary<string, int> FilesMentioning(string text)
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in Files())
        {
            var times = Occurrences(CodeOf(file), text);
            if (times > 0)
            {
                found[file] = times;
            }
        }

        return found;
    }

    /// <summary>
    /// How many times a method is called UNQUALIFIED — <c>Error(</c>, never <c>_log.Error(</c>.
    /// </summary>
    /// <remarks>
    /// <para>The precision the plan round asked for. A whole-file search for <c>Error(</c> counts
    /// every logger call in the file, and this codebase logs constantly; a search for <c>.Error(</c>
    /// counts only those. What a private helper's call looks like is an occurrence whose preceding
    /// character is neither a dot nor part of an identifier — a brace, a space, a bracket, a
    /// semicolon.</para>
    /// <para>The DECLARATION is excluded by the same rule read once more: it is the occurrence
    /// preceded by a return type, and <c>string Error(</c> is the only shape either helper has. A
    /// declaration counted as a call is an off-by-one in a number the documents quote, which is the
    /// class of defect the census exists to end.</para>
    /// </remarks>
    internal static int UnqualifiedCalls(string code, string method)
    {
        var count = 0;
        var wanted = method + "(";
        for (var at = code.IndexOf(wanted, StringComparison.Ordinal); at >= 0;
             at = code.IndexOf(wanted, at + wanted.Length, StringComparison.Ordinal))
        {
            if (at > 0 && !IsQualifiedOrPartOfAName(code[at - 1]) && !IsDeclaration(code, at))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsQualifiedOrPartOfAName(char before) =>
        before == '.' || char.IsLetterOrDigit(before) || before == '_';

    /// <summary>Whether this occurrence is the method being DECLARED rather than called.</summary>
    private static bool IsDeclaration(string code, int at) =>
        at >= 7 && code.AsSpan(at - 7, 7).SequenceEqual("string ");

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

    private static bool IsCode(string line) =>
        line is { Length: > 0 } && !line.StartsWith("//", StringComparison.Ordinal);
}
