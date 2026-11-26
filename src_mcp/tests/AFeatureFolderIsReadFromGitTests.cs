using System.Formats.Tar;
using System.Text;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Work;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// <c>GitWorkFolder</c> (epoch 3 E3 s3 t1): the work-plan folder of a COMMIT as an <see cref="IWorkFolder"/> — one <c>ls-tree</c>, each blob proved against its id, a link and a submodule never followed, the working tree never read.
/// Real git in temp repositories; a launcher that notes every command (and, for one test, damages one answer) makes "nothing was read" and "the proof refuses" observations.
/// </summary>
public sealed class AFeatureFolderIsReadFromGitTests
{
    private const string Feature = "plan";

    private readonly ProcessLauncher _launcher = new();

    private static string Canon(WorkReport report) =>
        string.Join(
            "\n",
            [
                $"found={report.FeatureFound} valid={report.Valid} counts={report.Counts} truncated={report.Truncated}",
                .. report.Errors.Select(error => $"E {error.Code}|{error.Path}|{error.Node}|{error.Message}"),
                .. report.Notes.Select(note => $"N {note}"),
                .. report.Important.Select(item => $"I {item.Node}|{item.Reason}"),
            ]);

    private static string Validate(IWorkFolder folder) => Canon(new WorkPlanValidator(new StrictYamlReader()).Validate(folder, WorkPlan.Defaults));

    private static async Task<TempGitRepo> RepoAsync(IProcessLauncher launcher, Dictionary<string, string> files)
    {
        var repo = await TempGitRepo.InitAsync(launcher, "coai-gitfolder-");
        await repo.GitAsync("config", "core.autocrlf", "false");
        Lay(repo, files);
        await repo.CommitAsync("the plan");

        return repo;
    }

    private static void Lay(TempGitRepo repo, Dictionary<string, string> files)
    {
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(repo.Path, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text, new UTF8Encoding(false));
        }
    }

    private static Dictionary<string, string> Under(string folder, Dictionary<string, string> files) =>
        files.ToDictionary(pair => $"{folder}/{pair.Key}", pair => pair.Value);

    private static async Task<GitWorkFolder> OpenedAsync(IProcessLauncher launcher, string repo, string sha, string folder)
    {
        var opened = await GitWorkFolder.OpenAsync(new GitHistory(launcher), repo, sha, folder, TestContext.Current.CancellationToken);

        return opened.Should().BeOfType<FolderOpen.Opened>(opened is FolderOpen.Refused refused ? refused.Sentence : string.Empty).Subject.Folder;
    }

    private static async Task<string> RefusedAsync(IProcessLauncher launcher, string repo, string sha, string folder)
    {
        var opened = await GitWorkFolder.OpenAsync(new GitHistory(launcher), repo, sha, folder, TestContext.Current.CancellationToken);

        return opened.Should().BeOfType<FolderOpen.Refused>().Subject.Sentence;
    }

    /// <summary>Real git, a note of every request, and an optional change of one answer.</summary>
    private sealed class Watching(IProcessLauncher real, Func<ProcessRequest, ProcessResult, ProcessResult>? change = null) : IProcessLauncher
    {
        public List<ProcessRequest> Ran { get; } = [];

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Ran.Add(request);
            var result = await real.RunAsync(request, ct);

            return change is null ? result : change(request, result);
        }

        public bool Read => Ran.Any(request => request.Arguments.Contains("cat-file"));
    }

    private static Dictionary<string, string> InvalidPlan()
    {
        var files = WorkPlan.Base();
        files[WorkPlan.TaskFile] = WorkPlan.Task("t1-one", dependsOn: "\n  - t9-nobody");
        files["epoch-1/e1-core/stories/s2-second/story.yaml"] = "id: s2-second\ntitle: Second\ndependsOn:\n  - s7-nowhere\n";

        return files;
    }

    [Fact]
    public async Task D1_TheValidator_OverGit_GivesTheAnswerOfACleanCheckout_ForAValidAndForAnInvalidPlan()
    {
        await using var repo = await RepoAsync(_launcher, Under(Feature, WorkPlan.Base()));
        var valid = await repo.HeadAsync();
        Lay(repo, Under(Feature, InvalidPlan()));
        await repo.CommitAsync("the plan, broken");
        var invalid = await repo.HeadAsync();

        foreach (var sha in new[] { valid, invalid })
        {
            await using var clean = await TempGitRepo.CloneAsync(_launcher, repo.Path, "coai-gitfolder-clean-");
            await clean.GitAsync("checkout", "--quiet", sha);
            var fromGit = Validate(await OpenedAsync(_launcher, repo.Path, sha, Feature));
            var fromDisk = Validate(new DiskWorkFolder(Path.Combine(clean.Path, Feature)));

            fromGit.Should().Be(fromDisk, $"commit {sha[..7]}");
        }

        Validate(await OpenedAsync(_launcher, repo.Path, valid, Feature)).Should().Contain("valid=True");
        Validate(await OpenedAsync(_launcher, repo.Path, invalid, Feature)).Should().Contain("valid=False").And.Contain("unknown-dependency");
    }

    [Fact]
    public async Task D1_AnEditedAStagedAndAnUntrackedFileOfTheWorkingTree_DoNotChangeTheAnswer()
    {
        await using var repo = await RepoAsync(_launcher, Under(Feature, WorkPlan.Base()));
        var sha = await repo.HeadAsync();
        var before = Validate(await OpenedAsync(_launcher, repo.Path, sha, Feature));

        Lay(repo, new Dictionary<string, string> { [$"{Feature}/{WorkPlan.TaskFile}"] = "this is not yaml at all: [\n" });
        Lay(repo, new Dictionary<string, string> { [$"{Feature}/feature.yaml"] = "id: other\ntitle: Other\nepochs: []\n" });
        await repo.GitAsync("add", $"{Feature}/feature.yaml");
        Lay(repo, new Dictionary<string, string> { [$"{Feature}/epoch-9/epoch.yaml"] = "id: epoch-9\n" });

        Validate(await OpenedAsync(_launcher, repo.Path, sha, Feature)).Should().Be(before);
        Validate(new DiskWorkFolder(Path.Combine(repo.Path, Feature))).Should().NotBe(before, "the working tree itself is different, so the equality above is not an accident");
    }

    [Fact]
    public async Task D1_TheV2FolderOfThisRepository_AtHead_GivesTheAnswerOfItsArchive()
    {
        var repoRoot = RepoPaths.Root();
        var head = (await _launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "HEAD"], repoRoot))).StdOut.Trim();
        using var temp = TempDir.For("coai-v2-archive-");
        var archive = Path.Combine(temp.Path, "v2.tar");
        var made = await _launcher.RunAsync(new ProcessRequest("git", ["archive", "--format=tar", "-o", archive, head, "v2"], repoRoot));
        made.ExitCode.Should().Be(0, made.StdErr);
        var extracted = Path.Combine(temp.Path, "out");
        Directory.CreateDirectory(extracted);
        await TarFile.ExtractToDirectoryAsync(archive, extracted, overwriteFiles: false, TestContext.Current.CancellationToken);

        var fromGit = Validate(await OpenedAsync(_launcher, repoRoot, head, "v2"));
        var fromDisk = Validate(new DiskWorkFolder(Path.Combine(extracted, "v2")));

        fromGit.Should().Be(fromDisk);
        fromGit.Should().Contain("valid=True", "the v2 folder is a valid plan (B1)");
    }

    [Fact]
    public async Task P8_ALink_IsNotAFolderAndNotAFile_AndASubmoduleIsNotFollowed()
    {
        await using var repo = await RepoAsync(_launcher, Under(Feature, WorkPlan.Base()));
        var oid = (await repo.RunAsync("rev-parse", $"HEAD:{Feature}/feature.yaml")).StdOut.Trim();
        await repo.StageEntryAsync($"{Feature}/epoch-9", "120000", oid);
        await repo.StageEntryAsync($"{Feature}/epoch-1/e1-core/stories/s2-linked", "120000", oid);
        await repo.StageEntryAsync($"{Feature}/epoch-1/e1-core/stories/s1-first/tasks/t2-sub", "160000", "0123456789012345678901234567890123456789");
        await repo.StageEntryAsync($"{Feature}/epoch-1/epoch.link.yaml", "120000", oid);
        await repo.CommitIndexAsync("links and a submodule");
        var sha = await repo.HeadAsync();

        var folder = await OpenedAsync(_launcher, repo.Path, sha, Feature);

        folder.Folders(string.Empty).Should().NotContain("epoch-9", "a link is not a folder");
        folder.Folders("epoch-1/e1-core/stories").Should().NotContain("s2-linked");
        folder.Read("epoch-1/epoch.link.yaml", 1000).Should().BeOfType<WorkFile.Missing>("a link is not a file");
        folder.Files.Select(file => file.Path).Should().NotContain(["epoch-9", "epoch-1/epoch.link.yaml"]);
        folder.Folders("epoch-1/e1-core/stories/s1-first/tasks").Should().Contain("t2-sub", "a checkout holds the submodule as an empty folder");
        folder.Read("epoch-1/e1-core/stories/s1-first/tasks/t2-sub/task.yaml", 1000).Should().BeOfType<WorkFile.Missing>("nothing inside a submodule is read");
    }

    [Fact]
    public async Task P8_EveryBlobIsProvedAgainstItsId_AnAnswerThatDiffersIsRefusedAndNamesTheFile()
    {
        await using var repo = await RepoAsync(_launcher, Under(Feature, WorkPlan.Base()));
        var sha = await repo.HeadAsync();
        var damaged = new Watching(_launcher, (request, result) =>
            request.Arguments.Contains("--batch") ? result with { StdOut = result.StdOut.Replace("title: Core", "title: Cure", StringComparison.Ordinal) } : result);

        var sentence = await RefusedAsync(damaged, repo.Path, sha, Feature);

        sentence.Should().Contain("epic.yaml").And.Contain("blob").And.Contain("differs");
        (await RefusedAsync(new Watching(_launcher), repo.Path, "not-a-sha", Feature)).Should().Contain("40 hex");
        await OpenedAsync(new Watching(_launcher), repo.Path, sha, Feature);
    }

    [Fact]
    public async Task P8_AFolderOver2000Entries_IsAnErrorAndNoBlobIsRead()
    {
        var files = Under(Feature, WorkPlan.Base());
        for (var i = 0; i < GitWorkFolder.MaxEntries; i++)
        {
            files[$"{Feature}/architecture/f{i:D4}.txt"] = "x\n";
        }

        await using var repo = await RepoAsync(_launcher, files);
        var watching = new Watching(_launcher);

        var sentence = await RefusedAsync(watching, repo.Path, await repo.HeadAsync(), Feature);

        sentence.Should().Contain("2005").And.Contain("2000");
        watching.Read.Should().BeFalse("the entries were counted before any blob was fetched");
    }

    [Fact]
    public async Task P8_AFileOver64KiB_IsAnErrorOfTheValidator_AndIsNeverRead()
    {
        var files = Under(Feature, WorkPlan.Base());
        var big = "# " + new string('x', 70_000) + "\n";
        files[$"{Feature}/{WorkPlan.TaskFile}"] = WorkPlan.Task("t1-one") + big;
        await using var repo = await RepoAsync(_launcher, files);
        var sha = await repo.HeadAsync();
        var bigOid = (await repo.RunAsync("rev-parse", $"{sha}:{Feature}/{WorkPlan.TaskFile}")).StdOut.Trim();
        var watching = new Watching(_launcher);

        var folder = await OpenedAsync(watching, repo.Path, sha, Feature);

        folder.Read(WorkPlan.TaskFile, StrictYamlReader.MaxBytes).Should().BeOfType<WorkFile.TooLarge>().Which.Bytes.Should().BeGreaterThan(70_000);
        Validate(folder).Should().Contain("yaml-too-large");
        watching.Ran.Select(request => request.StdIn).Should().NotContain(stdin => stdin.Contains(bigOid, StringComparison.Ordinal), "its blob was never asked for");
    }

    [Fact]
    public async Task AFolderThatIsNotThere_AFile_AndAGitThatDoesNotAnswer_AreRefusedWithASentence()
    {
        var files = Under(Feature, WorkPlan.Base());
        files["notes.txt"] = "a file\n";
        await using var repo = await RepoAsync(_launcher, files);
        var sha = await repo.HeadAsync();

        (await RefusedAsync(_launcher, repo.Path, sha, "nowhere")).Should().Contain("nowhere").And.Contain("not in commit");
        (await RefusedAsync(_launcher, repo.Path, sha, "notes.txt")).Should().Contain("notes.txt").And.Contain("not a folder");
        var silent = new Watching(_launcher, (_, _) => new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true));
        (await RefusedAsync(silent, repo.Path, sha, Feature)).Should().Contain("did not finish");
    }

    [Fact]
    public async Task APreloadedFile_IsReadByItsName_AndTheFilesAreListedInOrderWithTheirBlobIds()
    {
        var files = Under(Feature, WorkPlan.Base());
        files[$"{Feature}/PLAN.md"] = "# The plan\n";
        files[$"{Feature}/CRLF.md"] = "caf\u00e9 \u4e2d\r\nsecond line\r\n\r\nlast, no newline";
        await using var repo = await RepoAsync(_launcher, files);
        var sha = await repo.HeadAsync();
        var folder = await OpenedAsync(_launcher, repo.Path, sha, Feature);

        (await folder.PreloadAsync(["PLAN.md", "CRLF.md"], TestContext.Current.CancellationToken)).Should().BeEmpty();
        folder.Read("CRLF.md", StrictYamlReader.MaxBytes).Should().BeEquivalentTo(new WorkFile.Text("caf\u00e9 \u4e2d\r\nsecond line\r\n\r\nlast, no newline"), "line ends and non-ASCII text come back exactly as git holds them");

        folder.Read("PLAN.md", StrictYamlReader.MaxBytes).Should().BeEquivalentTo(new WorkFile.Text("# The plan\n"));
        folder.Files.Select(file => file.Path).Should().BeInAscendingOrder(StringComparer.Ordinal).And.Contain(["PLAN.md", "feature.yaml"]);
        var entry = folder.Files.Single(file => file.Path == "PLAN.md");
        entry.Size.Should().Be(11);
        entry.Blob.Should().Be((await repo.RunAsync("rev-parse", $"{sha}:{Feature}/PLAN.md")).StdOut.Trim());
    }
}
