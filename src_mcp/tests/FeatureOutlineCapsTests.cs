using System.Diagnostics;
using System.Text;
using CoaiMcp.Core.Feature;
using CoaiMcp.Normalizer;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What the old wall bound of <c>QuestionOutlineTimingTests</c> was standing in for, pinned by COUNT on a repository whose size never moves (E5 S11, C36):
/// the outline never READS more files than <see cref="FeatureBudget.MaxOutlinedFiles"/> and its section never weighs more than
/// <see cref="FeatureBudget.OutlineBytes"/>, whatever the tree.
/// </summary>
/// <remarks>
/// <para><b>Why a fixture.</b> The old test built the outline of THIS checkout and bounded the wall time at a minute "so a row's five-minute budget is not eaten". That
/// bound claimed two things — the caps hold, and the cost is bounded — and a clock on a growing checkout on a loaded machine proves neither: the checkout doubled in
/// size in three weeks and a three-suite machine multiplies the time five-fold, so the test went red ALONE with the product unchanged. The caps are a count and need no clock;
/// the cost is measured (<see cref="QuestionOutlineTimingTests"/> writes it by phase) and not asserted.</para>
/// <para>Real temporary repositories, the real launcher and the real outliner, each repository built in one commit and removed even if read-only. Two of them:
/// <c>MaxOutlinedFiles + 50</c> tiny classes — far inside the byte budget, so only the FILE cap can stop the read — and a hundred and twenty classes of eighty
/// members each, far inside the file cap, so only the BYTE budget can stop the section.</para>
/// <para><b>Not covered here:</b> the 16 MiB read ceiling (<see cref="FeatureOutlineBuilder.ReadCeilingBytes"/>), which would need a 16 MiB fixture; its reason string is
/// pinned where <c>ReadPlan</c> is exercised by <c>FeatureOutlineBuilderTests</c>.</para>
/// </remarks>
public sealed class FeatureOutlineCapsTests
{
    private const int PastTheCap = 50;

    private const int ManyMembers = 80;

    private const int BigFiles = 120;

    [Fact]
    public async Task MoreFilesThanTheCap_AreNamedPastTheCap_AndOnlyTheCapIsRead()
    {
        var files = Enumerable.Range(0, FeatureBudget.MaxOutlinedFiles + PastTheCap)
            .Select(i => ($"src/f{i:D3}.cs", $"public sealed class C{i:D3} {{ }}\n"))
            .ToList();
        using var repo = await FixtureRepo.CommitAsync(files);
        var launcher = new OutlineGitPhases(new ProcessLauncher());
        var outliner = new TimedOutliner(new TreeSitterOutliner());

        var started = Stopwatch.StartNew();
        var outline = await new FeatureOutlineBuilder(launcher, outliner).BuildAtHeadAsync(repo.Path, repo.Head, ct: TestContext.Current.CancellationToken);

        Console.Error.WriteLine("[measurement] outline cap fixture, " + launcher.Describe(started.Elapsed, outliner));
        outline.Files.Count.Should().Be(files.Count, "every file of the fixture is changed against the empty tree");
        outline.Outlined.Should().BeLessThanOrEqualTo(FeatureBudget.MaxOutlinedFiles);
        outline.Outlined.Should().Be(FeatureBudget.MaxOutlinedFiles, "the fixture is small enough for the byte budget, so the file cap alone decides how many are outlined");
        launcher.FilesRead.Should().Be(FeatureBudget.MaxOutlinedFiles, "the ONE batch is asked for the capped files and no more — the rest are named, not read");
        outline.Omissions.NotOutlined.Count(n => n.Reason == $"past the {FeatureBudget.MaxOutlinedFiles}-file outline cap; not read")
            .Should().Be(PastTheCap, "every file past the cap is named with that reason");
    }

    [Fact]
    public async Task MoreOutlineThanTheBudget_IsCutToTheBudget_AndEveryFileIsAccountedFor()
    {
        var members = string.Concat(Enumerable.Range(0, ManyMembers).Select(j => $"    public int Member{j}(int a) => a + {j};\n"));
        var files = Enumerable.Range(0, BigFiles)
            .Select(i => ($"src/Big{i:D3}.cs", $"public sealed class Big{i:D3}\n{{\n{members}}}\n"))
            .ToList();
        using var repo = await FixtureRepo.CommitAsync(files);
        var launcher = new OutlineGitPhases(new ProcessLauncher());
        var outliner = new TimedOutliner(new TreeSitterOutliner());

        var started = Stopwatch.StartNew();
        var outline = await new FeatureOutlineBuilder(launcher, outliner).BuildAtHeadAsync(repo.Path, repo.Head, ct: TestContext.Current.CancellationToken);

        Console.Error.WriteLine("[measurement] outline byte-budget fixture, " + launcher.Describe(started.Elapsed, outliner));
        Encoding.UTF8.GetByteCount(outline.Section).Should().BeLessThanOrEqualTo(FeatureBudget.OutlineBytes);
        (outline.Omissions.Dropped.Count + outline.Omissions.Collapsed.Count).Should().BeGreaterThan(0, "the fixture outlines to several times the budget, so something must be cut");
        (outline.Outlined + outline.Omissions.Dropped.Count).Should().Be(BigFiles, "a file is outlined or dropped by name, never lost");
    }

    /// <summary>A real temporary git repository holding the given files in one commit; removed — read-only objects and all — when disposed.</summary>
    private sealed class FixtureRepo : IDisposable
    {
        private FixtureRepo(string path, string head)
        {
            Path = path;
            Head = head;
        }

        public string Path { get; }

        public string Head { get; }

        public static async Task<FixtureRepo> CommitAsync(IReadOnlyList<(string Path, string Text)> files)
        {
            var root = Directory.CreateTempSubdirectory("coai-outline-caps-").FullName;
            try
            {
                foreach (var (path, text) in files)
                {
                    var full = System.IO.Path.Combine(root, path);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
                    await File.WriteAllTextAsync(full, text, TestContext.Current.CancellationToken);
                }

                await Git(root, "init", "-b", "main");
                await Git(root, "add", ".");
                await Git(root, "commit", "-m", "the fixture");

                return new FixtureRepo(root, (await Git(root, "rev-parse", "HEAD")).Trim());
            }
            catch
            {
                GitScratch.DeleteEvenIfReadOnly(root);
                throw;
            }
        }

        public void Dispose() => GitScratch.DeleteEvenIfReadOnly(Path);

        private static async Task<string> Git(string repo, params string[] args)
        {
            var result = await new ProcessLauncher().RunAsync(
                new ProcessRequest("git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "-c", "core.autocrlf=false", .. args], repo),
                TestContext.Current.CancellationToken);
            result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");

            return result.StdOut;
        }
    }
}
