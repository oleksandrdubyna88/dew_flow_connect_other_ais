using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What story 1's code round found. Each of these is a defect that shipped in the first commit and
/// was named by a reviewer; the test is what stops it coming back.
/// </summary>
public sealed class ConsultGateFixesTests
{
    private const string Answers = "D:/data/consultations/answers";

    private static CodexConsultant Consultant() => new(new CodexRuntime());

    private static ConsultantLaunch Launch(string repo = "D:/rsd/checkout", string prompt = "help", string handle = "", string model = "") =>
        new(repo, prompt, handle, Answers, new ReviewerSettings("codex") { Model = model });

    [Fact]
    public void NoArgumentValueMayCarryALineBreak_NotOnlyThePrompt()
    {
        // The rule was asserted for the PROMPT and nowhere else; a repository path, an output path or
        // a configured model name is somebody else's string too, and cmd.exe truncates any of them.
        foreach (var launch in (ConsultantLaunch[])
                 [
                     Launch(repo: "D:/rsd/check\nout"),
                     Launch(model: "gpt-5.6\n--dangerously-bypass-approvals-and-sandbox"),
                 ])
        {
            var build = () => Consultant().Build(launch);

            build.Should().Throw<ArgumentException>().WithMessage("*line break*");
        }
    }

    [Fact]
    public void TheResumedSandboxOverrideCarriesNoEmbeddedQuotes()
    {
        // Measured 2026-09-12: `-c sandbox_mode=read-only` resumes a thread and returns the number
        // planted in turn 1, so the quoted form buys nothing — and an embedded double quote inside an
        // argument that reaches cmd.exe through an npm shim is a re-tokenisation waiting to happen.
        var args = Consultant().Build(Launch(handle: "0198f2c1-aaaa")).Request.Arguments;

        args.Should().ContainInOrder(["-c", "sandbox_mode=read-only"]);
        args.Should().OnlyContain(a => !a.Contains('"'));
    }
}

/// <summary>The caller's own text is the one part of the prompt nothing else bounded.</summary>
public sealed class ConsultantPromptBoundsTests
{
    [Fact]
    public void AnEnormousProblemIsCut_AndTheCutIsSaid()
    {
        var bounded = ConsultantPrompt.BoundedProblem(new string('x', ConsultantPrompt.ProblemBudget * 3));

        bounded.Length.Should().BeLessThan(ConsultantPrompt.ProblemBudget + 200);
        bounded.Should().EndWith("characters)");
    }

    [Fact]
    public void AnOrdinaryProblemIsUntouched()
    {
        ConsultantPrompt.BoundedProblem("  the parser counts the separator  ").Should().Be("the parser counts the separator");
    }

    [Fact]
    public void TheSuspectedListIsCappedByCount_AndSaysHowManyItDropped()
    {
        var many = Enumerable.Range(0, ConsultantPrompt.SuspectedFileCap + 7).Select(i => $"src/F{i}.cs").ToList();

        var bounded = ConsultantPrompt.BoundedFiles(many);

        bounded.Should().HaveCount(ConsultantPrompt.SuspectedFileCap + 1);
        bounded[^1].Should().Contain("7 more");
    }

    [Fact]
    public void EmptyEntriesAreDropped_AndLongOnesAreCut()
    {
        var bounded = ConsultantPrompt.BoundedFiles(["  ", "src/A.cs", new string('p', 900)]);

        bounded.Should().HaveCount(2);
        bounded[0].Should().Be("src/A.cs");
        bounded[1].Should().EndWith("…");
    }

    [Fact]
    public void AForgetfulVendorsTranscriptIsBuiltNewestFirst_AndSaysWhatItDropped()
    {
        var turns = Enumerable.Range(0, 40)
            .Select(i => ($"problem {i} " + new string('q', 800), $"advice {i} " + new string('a', 800)))
            .ToList();

        var transcript = ConsultantPrompt.Transcript(turns);

        transcript.Should().Contain("problem 39", "the newest turn is always carried");
        transcript.Should().Contain("are not carried");
        transcript.Length.Should().BeLessThan(21_000);
    }

    [Fact]
    public void AShortConversationIsCarriedWhole()
    {
        var transcript = ConsultantPrompt.Transcript([("why is it red", "check the separator"), ("it prints 3", "then the cause is the count")]);

        transcript.Should().Contain("why is it red").And.Contain("then the cause is the count");
        transcript.Should().NotContain("not carried");
    }
}

/// <summary>Splitting one `git diff` back into the per-file pieces the shaper elides whole.</summary>
public sealed class DiffSplitterTests
{
    private const string TwoFiles = """
        diff --git a/src/A.cs b/src/A.cs
        index 111..222 100644
        --- a/src/A.cs
        +++ b/src/A.cs
        @@ -1 +1,2 @@
         one
        +two
        diff --git a/src/B.cs b/src/B.cs
        index 333..444 100644
        --- a/src/B.cs
        +++ b/src/B.cs
        @@ -1 +1 @@
        -old
        +new
        """;

    [Fact]
    public void EachFileGetsItsOwnHunk()
    {
        var pieces = DiffSplitter.ByFile(TwoFiles);

        pieces.Should().HaveCount(2);
        pieces["src/A.cs"].Should().Contain("+two").And.NotContain("+new");
        pieces["src/B.cs"].Should().Contain("+new").And.NotContain("+two");
    }

    [Fact]
    public void APathWithASpaceIsReadFromThePlusLine_NotFromTheAmbiguousHeader()
    {
        var diff = "diff --git a/my file.cs b/my file.cs\n--- a/my file.cs\n+++ b/my file.cs\n@@ -1 +1 @@\n+x\n";

        DiffSplitter.ByFile(diff).Should().ContainKey("my file.cs");
    }

    [Fact]
    public void ADeletedFileIsFoundOnItsOldSide()
    {
        var diff = "diff --git a/gone.cs b/gone.cs\ndeleted file mode 100644\n--- a/gone.cs\n+++ /dev/null\n@@ -1 +0,0 @@\n-x\n";

        DiffSplitter.ByFile(diff).Should().ContainKey("gone.cs");
    }

    [Fact]
    public void NothingInIsNothingOut()
    {
        DiffSplitter.ByFile(string.Empty).Should().BeEmpty();
        DiffSplitter.ByFile("not a diff at all\n").Should().BeEmpty();
    }
}
