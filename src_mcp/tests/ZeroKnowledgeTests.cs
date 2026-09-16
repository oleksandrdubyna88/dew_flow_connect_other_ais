using System.Text.RegularExpressions;
using CoaiMcp.Core.Normalising;
using CoaiMcp.Normalizer;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Over real source: nothing this repository named survives a skeleton.
/// </summary>
/// <remarks>
/// <para><b>This is the test the whole collector rests on.</b> A skeleton leaves the machine and lands
/// in a corpus other people read, so "the normaliser removes our names" is not a claim to make about
/// a fixture somebody wrote to be removable. It is asserted over this repository's own code, which
/// nobody wrote with a normaliser in mind — the same code the collector will actually meet.</para>
/// <para>Two checks, because they fail differently. The BLACKLIST — no word from the original that is
/// ours survives — is the stronger one and is only possible here, where the original is at hand. The
/// WHITELIST — every word in the skeleton is a placeholder, a keyword or runtime vocabulary — is
/// weaker on paper and stronger in shape, because it cannot be defeated by a name nobody thought to
/// forbid, and it is the only check the ingest server can run at all.</para>
/// <para>A failure here is <c>failed</c>, never <c>skipped</c>: a normaliser that lets a name through
/// is a defect in our code, and recording it as a property of the data is how it would go unnoticed.</para>
/// </remarks>
public sealed class ZeroKnowledgeTests
{
    private readonly IAstNormalizer _normalizer = new TreeSitterNormalizer();

    /// <summary>Identifier-ish words, the same shape <see cref="Skeleton"/> accounts for.</summary>
    private static readonly Regex Word = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    public static TheoryData<string, SourceLanguage> RealFiles()
    {
        var data = new TheoryData<string, SourceLanguage>();
        foreach (var (folder, pattern, language) in ((string, string, SourceLanguage)[])
        [
            ("src_mcp/core", "*.cs", SourceLanguage.CSharp),
            ("src_mcp/src/Store", "*.cs", SourceLanguage.CSharp),
            ("src_vs_code/src", "*.ts", SourceLanguage.TypeScript),
        ])
        {
            var directory = Path.Combine(RepositoryRoot(), folder);
            // Ordered and capped so the set is the same on every machine: a property test that
            // samples differently per run reports a different answer to the same question.
            foreach (var file in Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                         .OrderBy(f => f, StringComparer.Ordinal)
                         .Take(8))
            {
                data.Add(file, language);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RealFiles))]
    public void EveryMethodInARealFile_NormalisesToNothingOfOurs(string path, SourceLanguage language)
    {
        var source = File.ReadAllText(path);
        var keywords = _normalizer.KeywordsOf(language);
        var vocabulary = RuntimeVocabulary.For(language);
        var methods = MethodsOf(source, language);

        // No assertion that the file HAS functions: `Schema.cs` is const strings, `ConsultationRow.cs`
        // is a record, and a generated `.ts` is data. A file without a function is an ordinary file,
        // and demanding one here failed four of them for being what they are. What stops this passing
        // vacuously is the positive control below, not a precondition on every file.

        foreach (var method in methods)
        {
            var skeleton = _normalizer.Normalise(language, method);

            // The whitelist: the only check the ingest server will ever be able to run.
            // The skeleton itself rides on the failure: a leak is a word, and a word without the
            // code around it says nothing about how it survived.
            Skeleton.Leaks(skeleton, language, keywords).Should().BeEmpty(
                $"{Path.GetFileName(path)} produced a skeleton with a word that is neither placeholder, "
                + $"keyword nor runtime vocabulary. The skeleton was:{Environment.NewLine}{skeleton}");

            // The blacklist: possible only here, where the original is at hand.
            var ours = Word.Matches(method).Select(m => m.Value)
                .Where(w => !keywords.Contains(w) && !vocabulary.Contains(w))
                .Distinct(StringComparer.Ordinal);

            foreach (var name in ours)
            {
                Regex.IsMatch(skeleton, $@"\b{Regex.Escape(name)}\b").Should().BeFalse(
                    $"'{name}' is a name from {Path.GetFileName(path)} and it survived into the skeleton");
            }
        }
    }

    /// <summary>The finder does find things — without which every check above passes on nothing.</summary>
    /// <remarks>
    /// The positive control for this class. A <c>Locate</c> that returned nothing for every line would
    /// leave the property tests iterating an empty list and reporting success, which is the failure
    /// mode a property test is least able to notice about itself.
    /// </remarks>
    [Fact]
    public void TheFinderActuallyFindsMethods_InThisRepositorysOwnCode()
    {
        var path = Path.Combine(RepositoryRoot(), "src_mcp", "core", "Gate", "GateRule.cs");
        var methods = MethodsOf(File.ReadAllText(path), SourceLanguage.CSharp);

        methods.Should().HaveCountGreaterThan(2, "GateRule.cs is several methods, and they must be found");
        methods.Should().AllSatisfy(m => m.Should().NotBeNullOrWhiteSpace());
    }

    /// <summary>
    /// A skeleton of a method with non-ASCII text in it is not cut through the middle of a letter.
    /// </summary>
    /// <remarks>
    /// tree-sitter counts UTF-8 BYTES, and this repository has comments and strings in Cyrillic — so a
    /// normaliser splicing by character index corrupts exactly the files most likely to carry a name
    /// worth hiding, and does it silently. Asserted rather than assumed.
    /// </remarks>
    [Fact]
    public void NonAsciiSourceIsNotCorrupted()
    {
        const string source = """
            public void Проверить(string ключ)
            {
                // Комментарий, который должен исчезнуть целиком.
                var счёт = new Dictionary<string, int>();
                счёт.Add(ключ, 1);
            }
            """;

        var skeleton = _normalizer.Normalise(SourceLanguage.CSharp, source);

        skeleton.Should().NotContain("�", "a replacement character means a byte span was cut mid-letter");
        skeleton.Should().NotContain("Проверить").And.NotContain("ключ").And.NotContain("счёт");
        skeleton.Should().NotContain("Комментарий");
        skeleton.Should().Contain("Dictionary").And.Contain("Add", "the runtime's own words stay");
        Skeleton.Leaks(skeleton, SourceLanguage.CSharp, _normalizer.KeywordsOf(SourceLanguage.CSharp))
            .Should().BeEmpty();
    }

    /// <summary>Every function in a file, found the way the collector finds one.</summary>
    private IReadOnlyList<string> MethodsOf(string source, SourceLanguage language)
    {
        var found = new Dictionary<(int, int), string>();
        var lines = source.Split('\n').Length;
        for (var line = 1; line <= lines; line++)
        {
            if (_normalizer.Locate(language, source, line) is { } symbol)
            {
                found[(symbol.StartLine, symbol.EndLine)] = symbol.Source;
            }
        }

        return [.. found.Values];
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "dew_flow_connect_other_ais.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"no repository root above {AppContext.BaseDirectory}");
    }

    /// <summary>A regex is a literal, and what people put in one is the point.</summary>
    /// <remarks>
    /// <para>It passed through VERBATIM until 2026-09-16 — pattern and flags — so a method matching
    /// an internal hostname shipped that hostname in its skeleton. The property test found it only
    /// because a new file in this repository happened to use `/&amp;/g`, and what it reported was the
    /// stray `g`: the flag was a bare word, while the pattern beside it was not even suspected.</para>
    /// <para>This is the case that would have caught it directly, and it is deliberately a pattern
    /// nobody could mistake for harmless.</para>
    /// </remarks>
    [Fact]
    public void ARegexIsBlanked_PatternAndFlags()
    {
        var source = """
            function reach(url) {
              return /api\.internal\.acme-corp\.example/gi.test(url);
            }
            """;

        var skeleton = new TreeSitterNormalizer().Normalise(SourceLanguage.JavaScript, source);

        skeleton.Should().NotContain("acme", "a hostname in a pattern is a hostname");
        skeleton.Should().NotContain("internal");
        skeleton.Should().NotContain("gi", "the flags are part of the literal too");
        Skeleton.Leaks(skeleton, SourceLanguage.JavaScript, new TreeSitterNormalizer().KeywordsOf(SourceLanguage.JavaScript))
            .Should().BeEmpty();
    }
}
