using System.Text;
using CoaiMcp.Core.Feature;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The feature outline, built on a REAL temporary repository with the real outliner: every kind of
/// change named, sizes read before content, nothing over the ceiling read, the head commit rather than
/// the dirty tree, and the budget holding whatever it is set to.
/// </summary>
/// <remarks>
/// One fixture repository per test (base commit, head commit, then an uncommitted edit on top), which
/// the test only reads. A recording launcher wraps the real one, so a test can see what each git process
/// was ASKED, which is how "never read" is proven rather than inferred from the output.
/// </remarks>
public sealed class FeatureOutlineBuilderTests : IAsyncLifetime
{
    /// <summary>Enough lines that git's rename detection sees a rename with an edit, not an add and a delete.</summary>
    private const int LinesEnoughToBeARename = 40;

    private static readonly byte[] Binary = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03, 0x00, 0xFF];

    private readonly WatchedLauncher _launcher = new(new ProcessLauncher());
    private string _repo = string.Empty;
    private string _base = string.Empty;
    private string _head = string.Empty;

    private const string ShopAtBase =
        "public sealed class Shop\n"
        + "{\n"
        + "    public int Buy(int n)\n"
        + "    {\n"
        + "        var total = n;\n"
        + "        return total;\n"
        + "    }\n"
        + "\n"
        + "    public int Sell(int n)\n"
        + "    {\n"
        + "        return n;\n"
        + "    }\n"
        + "}\n";

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-feature-outline-").FullName;
        await Git("init", "-b", "main");
        Write("src/Shop.cs", ShopAtBase);
        Write("src/Old.cs", "public static class Old\n{\n" + string.Concat(Enumerable.Range(0, LinesEnoughToBeARename).Select(i => $"    public static int F{i}() => {i};\n")) + "}\n");
        Write("src/Dead.cs", "public sealed class Dead { }\n");
        Write("docs/notes.txt", "notes at base\n");
        WriteBytes("assets/logo.bin", Binary);
        await Git("add", ".");
        await Git("commit", "-m", "base");
        _base = await Rev("HEAD");

        Write("src/Shop.cs", ShopAtBase.Replace("var total = n;", "var total = n * 2;", StringComparison.Ordinal));
        await Git("mv", "src/Old.cs", "src/Renamed.cs");
        File.AppendAllText(Path.Combine(_repo, "src", "Renamed.cs"), "// renamed and edited\n");
        await Git("rm", "-q", "src/Dead.cs");
        Write("src/Added.ts", "export function added(a: number): number {\n  return a + 1;\n}\n");
        Write("docs/notes.txt", "notes at head\nand more\n");
        WriteBytes("assets/logo.bin", [.. Binary, 0x00, 0x42]);
        Write("src/Huge.cs", "public sealed class Huge\n{\n" + new string('/', (int)Core.Outlining.OutlineLimits.MaxInputBytes) + "\n}\n");
        await Git("add", ".");
        await Git("commit", "-m", "the feature");
        _head = await Rev("HEAD");

        // Uncommitted, on top of head: a signature that must NOT reach the reviewer.
        Write("src/Shop.cs", ShopAtBase.Replace("public int Buy(int n)", "public int BuyDirty(int n)", StringComparison.Ordinal));
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_repo, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ValueTask.CompletedTask;
    }

    private FeatureOutlineBuilder Builder() => new(_launcher, new TreeSitterOutliner());

    [Fact]
    public async Task EveryKindOfChange_IsNamedWithItsLetter()
    {
        var outline = await Builder().BuildAsync(_repo, _base, _head);

        outline.Files.Select(f => (f.Path, f.Letter)).Should().BeEquivalentTo(new[]
        {
            ("src/Shop.cs", 'M'), ("src/Renamed.cs", 'R'), ("src/Dead.cs", 'D'), ("src/Added.ts", 'A'),
            ("docs/notes.txt", 'M'), ("assets/logo.bin", 'M'), ("src/Huge.cs", 'A'),
        });
        outline.Section.Should().Contain("### src/Shop.cs (M, +1/-1)");
        outline.Section.Should().Contain("### src/Renamed.cs (R, +1/-0) — renamed from src/Old.cs");
        outline.Section.Should().Contain("### src/Added.ts (A, new, +3/-0)");
        outline.Omissions.NotOutlined.Should().ContainEquivalentOf(new NotOutlined("src/Dead.cs", "deleted at head"));
        outline.BaseSha.Should().Be(_base);
        outline.HeadSha.Should().Be(_head);
    }

    [Fact]
    public async Task ABinaryAndAnUnsupportedFile_AreNamedWithTheirSizeAtHead()
    {
        var outline = await Builder().BuildAsync(_repo, _base, _head);

        outline.Omissions.NotOutlined.Should().ContainEquivalentOf(new NotOutlined("assets/logo.bin", "binary; not read", Binary.Length + 2));
        outline.Omissions.NotOutlined.Should().ContainEquivalentOf(new NotOutlined("docs/notes.txt", "unsupported (language); not read", "notes at head\nand more\n".Length));
    }

    [Fact]
    public async Task AFileOverTheCeiling_IsNamedWithItsSize_AndNeverRead()
    {
        var hugeOid = await Rev($"{_head}:src/Huge.cs");
        var logoOid = await Rev($"{_head}:assets/logo.bin");
        var hugeSize = new FileInfo(Path.Combine(_repo, "src", "Huge.cs")).Length;

        var outline = await Builder().BuildAsync(_repo, _base, _head);

        var named = outline.Omissions.NotOutlined.Should().ContainSingle(n => n.Path == "src/Huge.cs").Subject;
        named.Bytes.Should().Be(hugeSize);
        named.Reason.Should().Contain("too large").And.Contain("not read");
        var batch = _launcher.Requests.Where(r => r.Arguments.SequenceEqual(["cat-file", "--batch"])).Should().ContainSingle("ONE batch reads every chosen blob").Subject;
        batch.StdIn.Should().NotContain(hugeOid, "a file over the ceiling is never read");
        batch.StdIn.Should().NotContain(logoOid, "nor is a binary");
        batch.StdIn.Should().Contain(await Rev($"{_head}:src/Shop.cs"), "the positive: an ordinary file IS read through the batch");
    }

    [Fact]
    public async Task TheChangedMemberIsMarked_ItsSiblingIsNot_AndItsHunkIsShownUnderIt()
    {
        var outline = await Builder().BuildAsync(_repo, _base, _head);

        outline.Section.Should().Contain("*   public int Buy(int n) [3-7]");
        outline.Section.Should().Contain("    public int Sell(int n) [9-12]");
        outline.Section.Should().Contain("#### src/Shop.cs — public int Buy(int n) [3-7] (+1/-1)");
        outline.Section.Should().NotContain("#### src/Shop.cs — public sealed class Shop", "the innermost changed member owns the edit");
        outline.Section.Should().Contain("-        var total = n;\n+        var total = n * 2;");
    }

    [Fact]
    public async Task TheOutlineIsOfTheHeadCommit_NotOfTheDirtyWorkingTree()
    {
        var outline = await Builder().BuildAsync(_repo, _base, _head);

        outline.Section.Should().Contain("public int Buy(int n)");
        outline.Section.Should().NotContain("BuyDirty", "an uncommitted edit never reaches the reviewer");
    }

    /// <summary>
    /// The launcher ends a CUT stream with one sentence. A file whose own changed line quotes that sentence
    /// — this builder, the launcher, a test of either — is not a cut stream, and keeps its marks and its hunk.
    /// </summary>
    [Fact]
    public async Task AChangedLineThatQuotesTheLaunchersCutSentence_KeepsItsMarksAndItsHunk()
    {
        Write("src/Shop.cs", ShopAtBase.Replace("        return n;", "        return n; // the launcher's own words: \"[coai: output truncated at 8 characters\"", StringComparison.Ordinal));
        await Git("add", ".");
        await Git("commit", "-m", "quotes the sentence");
        var head = await Rev("HEAD");

        var outline = await Builder().BuildAsync(_repo, _base, head);

        outline.Section.Should().Contain("*   public int Sell(int n)", "the member whose line changed is marked");
        outline.Section.Should().Contain("    public int Buy(int n) [3-7]", "and the one that did not change is not");
        outline.Section.Should().Contain("#### src/Shop.cs — public int Sell(int n)", "its hunk is shown");
        outline.Section.Should().Contain("[coai: output truncated at 8 characters", "the quoted sentence is an ordinary changed line, not a cut");
        outline.Omissions.Notes.Should().BeEmpty("nothing was cut");
    }

    [Fact]
    public async Task PastTheFileCap_FilesAreNamedNotRead()
    {
        var limits = FeatureOutlineLimits.Shipped with { MaxOutlinedFiles = 1 };

        var outline = await Builder().BuildAsync(_repo, _base, _head, limits);

        outline.Outlined.Should().Be(1);
        outline.Omissions.NotOutlined.Where(n => n.Reason == "past the 1-file outline cap; not read").Select(n => n.Path)
            .Should().BeEquivalentTo("src/Shop.cs", "src/Renamed.cs");
        outline.Section.Should().Contain("### src/Added.ts", "the largest readable change is the one read");
    }

    /// <summary>
    /// EVERY process, not only the diffs and the batch: the merge-base is one of the builder's own, through
    /// <c>ContextAssembler.ComparisonBase</c>, and it used to run under the launcher's ten-minute default.
    /// </summary>
    [Fact]
    public async Task EveryGitProcess_RunsUnderTheBuildersDeadline()
    {
        await Builder().BuildAsync(_repo, _base, _head);

        _launcher.Requests.Should().NotBeEmpty()
            .And.Contain(r => r.Arguments.FirstOrDefault() == "merge-base", "the comparison base was resolved by a git process the recorder saw")
            .And.OnlyContain(r => r.Timeout == FeatureOutlineBuilder.GitDeadline, "no git process of the build is outside its deadline");
    }

    /// <summary>The fallback road — no common ancestor, so the shallow check and the pin run — is under the deadline too.</summary>
    [Fact]
    public async Task WithNoCommonAncestor_TheFallbackProcessesRunUnderTheDeadlineToo()
    {
        await Git("checkout", "-q", "--orphan", "elsewhere");
        await Git("add", ".");
        await Git("commit", "-q", "-m", "an unrelated history");
        var elsewhere = await Rev("HEAD");

        var outline = await Builder().BuildAsync(_repo, elsewhere, _head);

        outline.BaseNote.Should().Contain("no common ancestor", "the fallback road was taken");
        _launcher.Requests.Should()
            .Contain(r => r.Arguments.Contains("--is-shallow-repository"), "the shallow check ran")
            .And.OnlyContain(r => r.Timeout == FeatureOutlineBuilder.GitDeadline);
    }

    [Fact]
    public async Task TheSameRange_BuildsTheSameBytes()
    {
        var first = await Builder().BuildAsync(_repo, _base, _head);
        var second = await Builder().BuildAsync(_repo, _base, _head);

        second.Section.Should().Be(first.Section);
        second.Omissions.Should().BeEquivalentTo(first.Omissions);
    }

    /// <summary>
    /// The budget property on real git output: whatever the budget, the section never passes it, and
    /// every changed file is outlined, dropped by name or named as not outlined — and every omission is
    /// rendered whole inside the reserve.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(700)]
    [InlineData(1200)]
    [InlineData(2000)]
    [InlineData(168 * 1024)]
    public async Task ForAnyBudget_TheSectionNeverPassesIt_AndEveryFileIsAccountedFor(int budget)
    {
        var limits = FeatureOutlineLimits.Shipped with { OutlineBytes = budget };

        var outline = await Builder().BuildAsync(_repo, _base, _head, limits);
        var omissions = OmissionsRenderer.Render(outline.Omissions, [], FeatureBudget.OmissionsReserveBytes);

        Encoding.UTF8.GetByteCount(outline.Section).Should().BeLessThanOrEqualTo(budget);
        Encoding.UTF8.GetByteCount(omissions).Should().BeLessThanOrEqualTo(FeatureBudget.OmissionsReserveBytes);
        foreach (var file in outline.Files)
        {
            (outline.Section.Contains($"### {file.Path} (", StringComparison.Ordinal) || omissions.Contains(file.Path, StringComparison.Ordinal))
                .Should().BeTrue($"{file.Path} is outlined or named (budget {budget})");
        }

        foreach (var cut in outline.Omissions.CutHunks)
        {
            CutMembersOf(omissions, cut.Path).Should().Contain($"[{cut.Start}-{cut.End}] +{cut.Added}/-{cut.Deleted}",
                $"every cut member hunk is named by its file AND its outline span (budget {budget})");
        }
    }

    /// <summary>The spans the omissions name as cut under one file — the text after <c>path: </c> on the cut-hunks line, up to the next file.</summary>
    private static string CutMembersOf(string omissions, string path)
    {
        var line = omissions.Split('\n').SingleOrDefault(l => l.StartsWith("- Member hunks cut", StringComparison.Ordinal)) ?? string.Empty;
        var at = line.IndexOf(path + ": ", StringComparison.Ordinal);
        var group = at < 0 ? string.Empty : line[(at + path.Length + 2)..];
        var next = group.IndexOf("; ", StringComparison.Ordinal);

        return next < 0 ? group : group[..next];
    }

    private void Write(string path, string text)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private void WriteBytes(string path, byte[] bytes)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    private async Task Git(params string[] args)
    {
        var result = await new ProcessLauncher().RunAsync(new ProcessRequest(
            "git",
            ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", .. args],
            _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private async Task<string> Rev(string rev)
    {
        var result = await new ProcessLauncher().RunAsync(new ProcessRequest("git", ["rev-parse", rev], _repo));
        result.ExitCode.Should().Be(0, result.StdErr);

        return result.StdOut.Trim();
    }
}
