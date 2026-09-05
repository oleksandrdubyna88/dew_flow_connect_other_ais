using Xunit;
using FluentAssertions;
using CoaiBench.Judging;
using CoaiBench.Model;

namespace CoaiBench.Tests;

/// <summary>
/// The judge is handed the code, at the commit that was reviewed — not a path into whatever the
/// checkout looks like today.
/// </summary>
/// <remarks>
/// <para>Two defects in one, found on 2026-09-05 by watching the first judgement pass. The judge
/// gave Fable a finding and a PATH and said "read the file before deciding", so every judgement was
/// an agentic session — two minutes and change per finding, four hours for one campaign. And the
/// path was read from the working tree, which had moved on: the sidebar file one campaign's findings
/// name was rewritten twice that day, so the judge would have weighed a finding about code that no
/// longer looked like that.</para>
/// <para>The file at the reviewed commit, windowed around the cited line, goes INTO the prompt. One
/// turn, no tools, a judgement about the code the reviewer actually saw.</para>
/// </remarks>
public sealed class JudgeReadsTheCodeItJudgesTests
{
    private static readonly string[] Lines = Enumerable.Range(1, 400).Select(i => $"line {i}").ToArray();
    private static readonly string Source = string.Join("\n", Lines);

    [Fact]
    public void TheWindowIsAroundTheCitedLine_NumberedSoTheJudgeCanPointBack()
    {
        var window = Judge.Window(Source, line: 200, radius: 3);

        window.Should().Be("197: line 197\n198: line 198\n199: line 199\n200: line 200\n201: line 201\n202: line 202\n203: line 203");
    }

    [Fact]
    public void AShortFileIsGivenWhole_AndAMissingLineMeansTheWholeFileToo()
    {
        var shortFile = "a\nb\nc";

        Judge.Window(shortFile, line: 2, radius: 80).Should().Be("1: a\n2: b\n3: c");
        Judge.Window(shortFile, line: 0, radius: 80).Should().Be("1: a\n2: b\n3: c", "no line cited: the whole file is the context");
    }

    [Fact]
    public void ALongFileWithNoLine_IsCappedRatherThanPastedWhole()
    {
        var window = Judge.Window(Source, line: 0, radius: 80);

        window.Split('\n').Length.Should().BeLessThanOrEqualTo(2 * 80 + 1, "a plan-level finding on a long file is not a licence to paste it all");
        window.Should().StartWith("1: line 1");
    }

    [Fact]
    public void ThePromptCarriesTheFindingTheCodeAndWhichCommitItIs()
    {
        var finding = new Finding(Severity: "major", Category: "security", File: "src/a.cs", Line: 12, Title: "Token in log", Why: "it is printed", Fix: "do not print it");

        var prompt = Judge.PromptFor(finding, "10: var t = token;\n11: Log(t);\n12: return t;", "267e07a");

        prompt.Should().Contain("Token in log").And.Contain("src/a.cs:12");
        prompt.Should().Contain("11: Log(t);", "the code is in the prompt, not behind a tool");
        prompt.Should().Contain("267e07a", "the judgement is about the code as it was reviewed");
        prompt.Should().NotContain("Read the file before deciding", "there is nothing to read; it is here");
    }

    [Fact]
    public void AFindingWithNoFile_IsJudgedOnItsOwnWords()
    {
        var finding = new Finding(Severity: "minor", Category: "architecture", File: "", Line: 0, Title: "Plan is vague", Why: "no numbers", Fix: "add them");

        var prompt = Judge.PromptFor(finding, string.Empty, "");

        prompt.Should().Contain("Plan is vague");
        prompt.Should().Contain("names no file", "and says so rather than pretending there is code");
    }

    [Fact]
    public void OneTurn_NoTools_TheModelAsked()
    {
        var arguments = Judge.Arguments("claude-fable-5-1");

        arguments.Should().ContainInOrder("--max-turns", "1");
        arguments.Should().ContainInOrder("--model", "claude-fable-5-1");
        arguments.Should().Contain("--disallowedTools");
        arguments.Should().ContainInOrder("--output-format", "json");
    }
}
