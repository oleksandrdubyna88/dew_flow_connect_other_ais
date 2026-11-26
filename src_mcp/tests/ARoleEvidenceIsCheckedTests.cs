using System.Text;
using CoaiMcp.Core.Roles;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E3 s1 t1 — the evidence check of the scout and the judge (spec A, scenarios S5 and S12). An item <c>{file, line, text}</c> is verified when the file is in the tree the job read and the line holds the text once white space
/// is collapsed (d1); a blank text is an error of the answer, decided BEFORE any line is read, in every setting (d2); a path that is absolute, has a drive or <c>..</c>, runs through a link or is named like a credential file is an error
/// and the file is never opened (d3); the pure part has no IO (d4). The pure check runs over a table of files; the real source runs over a temp repository, with links.
/// </summary>
public sealed class ARoleEvidenceIsCheckedTests
{
    /// <summary>A table of files that records every question it is asked — so "never opened" is a count, not a hope.</summary>
    private sealed class Table(Dictionary<string, EvidenceFileRead> files) : IEvidenceSource
    {
        public List<string> Asked { get; } = [];

        public EvidenceFileState Probe(string path)
        {
            Asked.Add(path);

            return files.TryGetValue(path, out var read) ? read.State : EvidenceFileState.Missing;
        }

        public EvidenceFileRead Read(string path)
        {
            Asked.Add(path);

            return files.TryGetValue(path, out var read) ? read : EvidenceFileRead.Without(EvidenceFileState.Missing);
        }
    }

    private static EvidenceFileRead Lines(params string[] lines) => new(EvidenceFileState.Ok, lines);

    private static Table Source() => new(new()
    {
        ["src/a.cs"] = Lines("class A", "    int  x =\t1;", "", "   ", "}"),
        ["big.txt"] = EvidenceFileRead.Without(EvidenceFileState.TooLarge),
        ["bin.dat"] = EvidenceFileRead.Without(EvidenceFileState.NotUtf8),
        ["link"] = EvidenceFileRead.Without(EvidenceFileState.ThroughLink),
        ["folder"] = EvidenceFileRead.Without(EvidenceFileState.NotAFile),
    });

    private static EvidenceVerdict Check(Table source, string file, int line, string text) => RoleEvidence.Check(new EvidenceItem(file, line, text), source);

    // ---------- d1: verified, and the ways it is not ----------

    [Fact]
    public void D1_AnItemIsVerified_WhenTheLineHoldsTheText_AfterWhiteSpaceIsCollapsed()
    {
        var source = Source();

        Check(source, "src/a.cs", 1, "class A").Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeTrue();
        Check(source, "src/a.cs", 2, "int x = 1;").Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeTrue("two spaces and a tab are one space on both sides");
        Check(source, "src/a.cs", 2, "  int\t\tx =  1;  ").Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeTrue();
        Check(source, "src\\a.cs", 5, "}").Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeTrue("a backslash is a separator in a path");
    }

    [Theory]
    [InlineData("src/a.cs", 0, "class A", "line 0")]
    [InlineData("src/a.cs", 6, "class A", "line 6 is not in the file")]
    [InlineData("src/a.cs", 1, "class B", "does not hold the text")]
    [InlineData("src/a.cs", 1, "class A extra", "does not hold the text")]
    [InlineData("src/none.cs", 1, "class A", "not in the tree the job read")]
    [InlineData("big.txt", 1, "x", "over 2097152 bytes")]
    [InlineData("bin.dat", 1, "x", "not UTF-8")]
    [InlineData("folder", 1, "x", "not a file")]
    public void D1_AnItemThatIsNotTrue_IsNotVerified_WithAReason(string file, int line, string text, string reason)
    {
        var verdict = Check(Source(), file, line, text).Should().BeOfType<EvidenceVerdict.Checked>().Subject;

        verdict.Verified.Should().BeFalse();
        verdict.Why.Should().Contain(reason);
    }

    [Fact]
    public void D1_TheLimitIs2MiB_AndALineIsFoundByItsNumber()
    {
        RoleEvidence.MaxFileBytes.Should().Be(2 * 1024 * 1024);
        RoleEvidence.Collapse("  a \t\n b  ").Should().Be("a b");
        RoleEvidence.Collapse("   ").Should().BeEmpty();
    }

    // ---------- d2 / S12: a blank text is an error, before any line is read ----------

    public static TheoryData<string> BlankTexts =>
    [
        string.Empty, " ", "   ", "\t", "\n", "\r\n", " \t \n ", "\u00A0", "\u2003", "\u3000", "\u200B", "\uFEFF", "\u2800", "\u00A0\u200B \t",
    ];

    [Theory]
    [MemberData(nameof(BlankTexts))]
    public void D2_ABlankText_IsAnErrorOfTheAnswer_ForABlankLine_ALineOfSpaces_ALineOutOfRange_AndAFileThatIsNotThere(string blank)
    {
        foreach (var (file, line) in new[] { ("src/a.cs", 3), ("src/a.cs", 4), ("src/a.cs", 99), ("src/a.cs", 0), ("src/missing.cs", 1), ("big.txt", 1), ("bin.dat", 1) })
        {
            var source = Source();

            var verdict = Check(source, file, line, blank);

            verdict.Should().BeOfType<EvidenceVerdict.Error>($"{file}:{line} with text '{blank.Length} chars'").Which.Sentence.Should().Contain("evidence.text").And.Contain("never empty or only white space");
            source.Asked.Should().BeEmpty("the text was judged before any line was read, and before the file was asked about");
        }
    }

    [Fact]
    public void D2_NoCaseOfABlankText_AnswersVerifiedTrue_AndANullTextIsBlankToo()
    {
        RoleEvidence.IsBlank(null).Should().BeTrue();
        RoleEvidence.IsBlank("x").Should().BeFalse();
        RoleEvidence.IsBlank(" x ").Should().BeFalse();
        RoleEvidence.BlankProblem("facts[0].evidence[1].text", "  ").Should().StartWith("facts[0].evidence[1].text:");
        RoleEvidence.BlankProblem("at", "ok").Should().BeEmpty();
        var blankLineSource = new Table(new() { ["blank.txt"] = Lines("", "  ", "\t") });

        foreach (var blank in new[] { string.Empty, " ", "\t" })
        {
            foreach (var line in new[] { 1, 2, 3 })
            {
                RoleEvidence.Check(new EvidenceItem("blank.txt", line, blank), blankLineSource).Should().BeOfType<EvidenceVerdict.Error>();
            }
        }

        blankLineSource.Asked.Should().BeEmpty();
    }

    // ---------- d3 / S5: a path that may not be opened ----------

    [Theory]
    [InlineData("/etc/passwd", "inside the repository")]
    [InlineData("\\windows\\win.ini", "inside the repository")]
    [InlineData("C:/Users/x/a.txt", "inside the repository")]
    [InlineData("C:\\Users\\x\\a.txt", "inside the repository")]
    [InlineData("c:a.txt", "inside the repository")]
    [InlineData("../outside.txt", "leaves the repository")]
    [InlineData("src/../../outside.txt", "leaves the repository")]
    [InlineData("..", "leaves the repository")]
    [InlineData("src\\..\\..\\x", "leaves the repository")]
    [InlineData(".git/config", "leaves the repository")]
    [InlineData("a/.GIT/HEAD", "leaves the repository")]
    [InlineData(".env", "credential file")]
    [InlineData("config/.env.local", "credential file")]
    [InlineData("keys/server.pem", "credential file")]
    [InlineData("keys/SERVER.PEM", "credential file")]
    [InlineData("home/id_rsa", "credential file")]
    [InlineData("home/id_ed25519.pub", "credential file")]
    [InlineData("store/cert.pfx", "credential file")]
    [InlineData("", "not a path")]
    [InlineData("   ", "not a path")]
    [InlineData("a\0b", "not a path")]
    public void D3_APathThatMayNotBeOpened_IsAnError_AndTheSourceIsNeverAsked(string path, string word)
    {
        var source = Source();

        var verdict = RoleEvidence.Check(new EvidenceItem(path, 1, "text"), source);

        verdict.Should().BeOfType<EvidenceVerdict.Error>().Which.Sentence.Should().StartWith("evidence.file:").And.Contain(word);
        source.Asked.Should().BeEmpty("the file was never opened, nor asked about");
        RoleEvidence.CheckFile(path, source).Should().BeOfType<EvidenceVerdict.Error>();
        source.Asked.Should().BeEmpty();
    }

    [Fact]
    public void D3_APathThroughALink_IsAnError_ForAnItemAndForAListedFile()
    {
        var source = Source();

        RoleEvidence.Check(new EvidenceItem("link", 1, "x"), source).Should().BeOfType<EvidenceVerdict.Error>().Which.Sentence.Should().Contain("runs through a link");
        RoleEvidence.CheckFile("link", source).Should().BeOfType<EvidenceVerdict.Error>().Which.Sentence.Should().Contain("runs through a link");
    }

    [Fact]
    public void D3_AListedFile_ExistsOrNot_AndIsNeverReadForIt()
    {
        var source = Source();

        RoleEvidence.CheckFile("src/a.cs", source).Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeTrue();
        RoleEvidence.CheckFile("src/none.cs", source).Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeFalse();
        RoleEvidence.CheckFile("folder", source).Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeFalse("a folder is not a file");
    }

    [Fact]
    public void D3_ThePathRule_NamesTheItemsPlace_AndAnOrdinaryPathPasses()
    {
        RoleEvidence.PathProblem("facts[2].evidence[0].file", "../x").Should().StartWith("facts[2].evidence[0].file:");
        foreach (var fine in new[] { "src/a.cs", "a.txt", "dir/sub/file.name.cs", "./src/a.cs", ".github/workflows/ci.yml", "docs/environment.md" })
        {
            RoleEvidence.PathProblem("f", fine).Should().BeEmpty(fine);
        }

        RoleEvidence.Normal(".\\src//a.cs").Should().Be("src/a.cs");
    }

    // ---------- the real source: files through RealPath, links never followed ----------

    [Fact]
    public void D1_TheRealSource_ReadsALine_WithCrlf_ABom_NoTrailingNewline_AndAnEmptyFile()
    {
        using var repo = TempDir.For("coai-evidence-");
        File.WriteAllBytes(repo.At("crlf.txt"), Encoding.UTF8.GetBytes("one\r\ntwo\r\nthree\r\n"));
        File.WriteAllBytes(repo.At("bom.txt"), [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("first\nsecond")]);
        File.WriteAllBytes(repo.At("empty.txt"), []);
        var source = new RoleEvidenceFiles(repo);

        source.Read("crlf.txt").Lines.Should().Equal("one", "two", "three");
        source.Read("bom.txt").Lines.Should().Equal("first", "second");
        source.Read("empty.txt").Lines.Should().BeEmpty();
        RoleEvidence.Check(new EvidenceItem("crlf.txt", 3, "three"), source).Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeTrue();
        RoleEvidence.Check(new EvidenceItem("crlf.txt", 4, "three"), source).Should().BeOfType<EvidenceVerdict.Checked>().Which.Why.Should().Contain("line 4 is not in the file (it has 3 lines)");
        RoleEvidence.Check(new EvidenceItem("empty.txt", 1, "x"), source).Should().BeOfType<EvidenceVerdict.Checked>().Which.Verified.Should().BeFalse();
    }

    [Fact]
    public void D1_TheRealSource_RefusesAFileOver2MiB_AndAFileThatIsNotUtf8_AndAFolder_AndNothing()
    {
        using var repo = TempDir.For("coai-evidence-");
        File.WriteAllBytes(repo.At("big.txt"), new byte[RoleEvidence.MaxFileBytes + 1]);
        File.WriteAllBytes(repo.At("edge.txt"), Enumerable.Repeat((byte)'a', RoleEvidence.MaxFileBytes).ToArray());
        File.WriteAllBytes(repo.At("latin1.txt"), [0x63, 0x61, 0x66, 0xE9, 0x0A]);
        Directory.CreateDirectory(repo.At("dir"));
        var source = new RoleEvidenceFiles(repo);

        source.Read("big.txt").State.Should().Be(EvidenceFileState.TooLarge);
        source.Read("edge.txt").State.Should().Be(EvidenceFileState.Ok, "a file of exactly the cap is read");
        source.Read("latin1.txt").State.Should().Be(EvidenceFileState.NotUtf8);
        source.Read("dir").State.Should().Be(EvidenceFileState.NotAFile);
        source.Read("nothing.txt").State.Should().Be(EvidenceFileState.Missing);
        source.Read("dir/inner/none.txt").State.Should().Be(EvidenceFileState.Missing);
    }

    [Fact]
    public void D3_TheRealSource_NeverOpensAFileThroughALink_ToTheOutside_ToTheInside_OrADanglingOne()
    {
        using var outside = TempDir.For("coai-evidence-outside-");
        File.WriteAllText(outside.At("secret.txt"), "TOP SECRET\n");
        using var repo = TempDir.For("coai-evidence-");
        File.WriteAllText(repo.At("real.txt"), "inside\n");
        Directory.CreateDirectory(repo.At("sub"));
        File.WriteAllText(repo.At("sub", "in.txt"), "deep\n");
        File.CreateSymbolicLink(repo.At("fileLink"), outside.At("secret.txt"));
        File.CreateSymbolicLink(repo.At("folderLink"), outside);
        File.CreateSymbolicLink(repo.At("insideLink"), repo.At("real.txt"));
        File.CreateSymbolicLink(repo.At("subLink"), repo.At("sub"));
        File.CreateSymbolicLink(repo.At("dangling"), repo.At("not-there.txt"));
        var source = new RoleEvidenceFiles(repo);

        foreach (var path in new[] { "fileLink", "folderLink/secret.txt", "insideLink", "subLink/in.txt", "dangling" })
        {
            source.Read(path).State.Should().Be(EvidenceFileState.ThroughLink, path);
            source.Probe(path).Should().Be(EvidenceFileState.ThroughLink, path);
            RoleEvidence.Check(new EvidenceItem(path, 1, "TOP SECRET"), source).Should().BeOfType<EvidenceVerdict.Error>(path).Which.Sentence.Should().Contain("runs through a link");
        }

        source.Read("real.txt").State.Should().Be(EvidenceFileState.Ok, "a plain file in the same tree still reads: the refusals are not blanket");
        source.Read("sub/in.txt").Lines.Should().Equal("deep");
    }

    [Fact]
    public void D1_TheRealSource_ReadsAFileOnce_PerJob()
    {
        using var repo = TempDir.For("coai-evidence-");
        File.WriteAllText(repo.At("a.txt"), "one\n");
        var source = new RoleEvidenceFiles(repo);
        var first = source.Read("a.txt");
        File.WriteAllText(repo.At("a.txt"), "changed\n");

        source.Read("a.txt").Should().BeSameAs(first, "the lines the job's first question saw are the lines every later question sees");
    }

    // ---------- d4: the pure part has no IO ----------

    [Fact]
    public void D4_ThePurePart_HoldsNoIo_AndTheOnlyFileReaderIsTheOneThroughRealPath()
    {
        var core = File.ReadAllLines(Path.Combine(RepoPaths.Root(), "src_mcp", "core", "Roles", "RoleEvidence.cs"));
        var code = string.Join('\n', core.Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        foreach (var word in new[] { "System.IO", "File.", "Directory.", "FileStream", "Process", "Environment." })
        {
            code.Should().NotContain(word, "RoleEvidence is pure: lines come through IEvidenceSource");
        }

        var files = File.ReadAllText(Path.Combine(RepoPaths.Root(), "src_mcp", "src", "Server", "RoleJobs", "RoleEvidenceFiles.cs"));
        files.Should().Contain("RealPath.Resolve").And.NotContain("ResolveLinkTarget").And.NotContain("GetFullPath");
    }
}
