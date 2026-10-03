using System.Reflection;
using System.Text.RegularExpressions;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every way a question can end (<see cref="QuestionOutcomes"/>, the server's words on disk) has a sentence in the
/// extension's Logs → Questions tab — the <c>OUTCOMES</c> map of <c>src_vs_code/src/qconsultLog.ts</c>.
/// </summary>
/// <remarks>
/// <para>A contract with two implementations is checked from the side that EMITS, by enumerating the type rather than
/// retyping it (testing.md): a TypeScript test against a list copied from here could never notice an outcome it was
/// never told about. Two outcomes arrived on 2026-10-03 (<c>research/PLAN_ask_human_is_for_the_gate.md</c>) and would have
/// rendered as raw words in the log.</para>
/// <para>The map is read as text because the TypeScript is not this test's to run; the companion test proves the scan
/// still finds a key it knows, so a reformatted map cannot turn this into a test that matches nothing.</para>
/// </remarks>
public sealed class TheLogNamesEveryQuestionOutcomeTests
{
    private static readonly string LogPage = Path.Combine(ProductionSources.RepositoryRoot(), "src_vs_code", "src", "qconsultLog.ts");

    private static IReadOnlyList<string> Outcomes() =>
        [.. typeof(QuestionOutcomes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)];

    /// <summary>The keys of the page's <c>OUTCOMES</c> map — the block from its declaration to its closing brace.</summary>
    private static IReadOnlyList<string> Labelled()
    {
        // Comments out first: a key-shaped line inside one must not count as a label.
        var text = Regex.Replace(File.ReadAllText(LogPage), @"/\*.*?\*/|//[^\r\n]*", string.Empty, RegexOptions.Singleline);
        var block = Regex.Match(text, @"const OUTCOMES[^{]*\{(?<body>.*?)\n\s*\};", RegexOptions.Singleline);
        block.Success.Should().BeTrue($"{LogPage} declares the OUTCOMES map this test reads");

        return [.. Regex.Matches(block.Groups["body"].Value, @"^\s*['""]?(?<key>[A-Za-z0-9_]+)['""]?\s*:", RegexOptions.Multiline).Select(m => m.Groups["key"].Value)];
    }

    [Fact]
    public void EveryOutcomeTheServerWrites_HasASentenceInTheLog()
    {
        var labelled = Labelled();

        Outcomes().Should().NotBeEmpty().And.OnlyContain(outcome => labelled.Contains(outcome),
            "an outcome with no label renders as its raw word in the Questions tab — add it to OUTCOMES in qconsultLog.ts");
    }

    [Fact]
    public void TheScanStillFindsAKnownLabel()
    {
        Labelled().Should().Contain(QuestionOutcomes.PersonAsked, "the scan must keep matching the map, or the test above matches nothing");
    }
}
