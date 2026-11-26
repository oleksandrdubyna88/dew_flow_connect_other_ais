using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Collecting;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s2 t6: golden detection on fixture trees, with REAL git. Each tree is a data file (<c>fixtures/toolchain/trees/&lt;name&gt;.tree.json</c>: the path and the text of each file, and the heads to commit), made into a temporary git repository by the test — so this repository
/// holds no fixture manifest that its own detection could find. The golden answer of a tree (<c>&lt;name&gt;.golden.txt</c>) is the projects, the facts, the argv, the working folder and the network of every step, at the base and after each head commit (a project added, a test script or test project removed, a
/// <c>Cargo.toml</c> added, a folder renamed). A difference fails the test and prints both answers.
/// </summary>
/// <remarks>
/// <b>How the goldens were made.</b> Written from the detector's own first output and then READ by a person against SPEC §A and the measured facts of story s1 (the answer to each tree is in the sentence at the top of its golden); a golden is a regression net, not a proof. To regenerate after a deliberate change,
/// set <c>COAI_GOLDEN_OUT</c> to a folder: the test writes the actual answers there (and fails) instead of comparing.
/// </remarks>
public sealed class ADetectionIsGoldenTests : IAsyncLifetime
{
    private static readonly string TreesFolder = Path.Combine(AppContext.BaseDirectory, "fixtures", "toolchain", "trees");

    private readonly ProcessLauncher _launcher = new();

    private TempGitRepo _repo = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_repo is not null)
        {
            await _repo.DisposeAsync();
        }
    }

    private static string[] TreeNames() => [.. Directory.GetFiles(TreesFolder, "*.tree.json").Select(path => Path.GetFileName(path)[..^".tree.json".Length]).Order(StringComparer.Ordinal)];

    public static TheoryData<string> Trees => [.. TreeNames()];

    [Fact]
    public void TheSevenTrees_AreThere_AndHoldNoManifestOfThisRepositorysOwnDetection()
    {
        var names = TreeNames();

        names.Should().BeEquivalentTo("vstest-solution", "mtp-solution", "loose-projects", "npm-jest", "npm-vitest-typescript", "npm-workspace", "monorepo");
        Directory.GetFiles(TreesFolder).Select(Path.GetFileName).Should().OnlyContain(name => name!.EndsWith(".tree.json") || name.EndsWith(".golden.txt"), "no file of the fixtures is named like a manifest");
    }

    [Theory]
    [MemberData(nameof(Trees))]
    public async Task TheGoldenAnswer_OfATree_HoldsAtTheBaseAndAfterEveryHead(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(TreesFolder, name + ".tree.json")));
        var root = document.RootElement;
        var files = Files(root.GetProperty("base"));
        _repo = await TempGitRepo.InitAsync(_launcher, "golden-");
        var reader = new ManifestsAtCommit(new GitHistory(_launcher));
        var text = new StringBuilder($"# {name}: {root.GetProperty("description").GetString()}\n");

        await Commit(files);
        var baseSha = await _repo.HeadAsync();
        text.Append(Render("base", await Resolve(reader, baseSha, baseSha)));
        foreach (var head in root.GetProperty("heads").EnumerateArray())
        {
            var next = Apply(files, head);
            await Commit(next);
            text.Append(Render($"head: {head.GetProperty("name").GetString()}", await Resolve(reader, baseSha, await _repo.HeadAsync())));
        }

        Check(name, text.ToString());
    }

    // ---- D7, D10 ----

    [Fact]
    public async Task D7_AnEmptyTree_AndATreeOfDocumentsOnly_AreNotConfigured()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "golden-empty-");
        await _repo.WriteAsync("README.md", "documents only\n");
        await _repo.WriteAsync("notes.txt", "x\n");
        await _repo.CommitAsync("docs");
        var sha = await _repo.HeadAsync();

        var resolution = await Resolve(new ManifestsAtCommit(new GitHistory(_launcher)), sha, sha);

        resolution.Should().BeOfType<ToolchainResolution.NotConfigured>();
    }

    [Fact]
    public async Task D7_ACommitWithNoFileAtAll_IsNotConfigured()
    {
        _repo = await TempGitRepo.InitAsync(_launcher, "golden-void-");
        await _repo.GitAsync("-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "empty");
        var sha = await _repo.HeadAsync();

        (await Resolve(new ManifestsAtCommit(new GitHistory(_launcher)), sha, sha)).Should().BeOfType<ToolchainResolution.NotConfigured>();
    }

    [Fact]
    public async Task D10_ThisRepositorysOwnHead_WithDetectionForced_IsOneDotnetUnitWithTheMtpRunner_AndTheNodeProjectsWithNpm()
    {
        var root = RepoPaths.Root();
        var head = await new TempHead(_launcher, root).ShaAsync();
        Assert.SkipWhen(head.Length == 0, "this checkout is not a git repository, so its own HEAD cannot be read");

        var resolution = await Resolve(new ManifestsAtCommit(new GitHistory(_launcher)), head, head, root);

        if (Environment.GetEnvironmentVariable("COAI_GOLDEN_OUT") is { Length: > 0 } outFolder)
        {
            Directory.CreateDirectory(outFolder);
            File.WriteAllText(Path.Combine(outFolder, "this-repository.txt"), Render("this repository", resolution));
        }

        var toolchain = resolution.Should().BeOfType<ToolchainResolution.Resolved>(Render("this repository", resolution)).Subject.Toolchain;
        var dotnet = toolchain.Projects.Select(project => project.Detected!).Where(project => project.Stack == "dotnet").ToList();
        dotnet.Should().ContainSingle(Render("this repository", resolution)).Which.Should().Match<DetectedProject>(project => project.Id == "dew_flow_connect_other_ais.slnx" && project.Fact("runner") == "mtp" && project.Fact("kind") == "solution");
        var node = toolchain.Projects.Select(project => project.Detected!).Where(project => project.Stack == "node").ToList();
        node.Should().Contain(project => project.Id == "src_vs_code/package.json" && project.Fact("packageManager") == "npm");
        node.Should().OnlyContain(project => project.Fact("packageManager") == "npm");
        toolchain.Source.Should().Be(ToolchainSource.Detected);
        toolchain.Notes.Should().Contain(note => note.Contains(".agents/conventions"), "the rules submodule is named and never entered");
    }

    [Fact]
    public async Task D10_ThisRepositorysOwnFile_Wins_AndDetectionIsNeverAsked()
    {
        var file = ToolchainFile.Read(new WorkFile.Text(await File.ReadAllTextAsync(Path.Combine(RepoPaths.Root(), ".coai", "toolchain.yaml"))));
        var asked = 0;

        var resolution = ToolchainResolve.Resolve(file, () => { asked++; return new ManifestRead.Refused("must not be asked"); }, () => { asked++; return new ManifestRead.Refused("must not be asked"); }, null, ToolchainDefaults.Current);

        var toolchain = resolution.Should().BeOfType<ToolchainResolution.Resolved>().Subject.Toolchain;
        toolchain.Source.Should().Be(ToolchainSource.File);
        toolchain.Projects.Should().ContainSingle().Which.FileProject!.Path.Should().Be("src_mcp");
        asked.Should().Be(0);
    }

    // ---- the harness ----

    private async Task<ToolchainResolution> Resolve(ManifestsAtCommit reader, string baseSha, string headSha, string? repoPath = null)
    {
        var path = repoPath ?? _repo.Path;
        var atBase = await reader.ReadAsync(path, baseSha, ToolchainDefaults.Current);
        var atHead = await reader.ReadAsync(path, headSha, ToolchainDefaults.Current);

        return ToolchainResolve.Resolve(new ToolchainRead.NotConfigured(), () => atBase, () => atHead, null, ToolchainDefaults.Current);
    }

    private static Dictionary<string, string> Files(JsonElement element) =>
        element.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);

    private static Dictionary<string, string> Apply(Dictionary<string, string> files, JsonElement head)
    {
        var next = new Dictionary<string, string>(files, StringComparer.Ordinal);
        if (head.TryGetProperty("remove", out var remove))
        {
            foreach (var path in remove.EnumerateArray())
            {
                next.Remove(path.GetString()!).Should().BeTrue($"the head removes {path}, which the base has");
            }
        }

        if (head.TryGetProperty("rename", out var rename))
        {
            foreach (var pair in rename.EnumerateArray())
            {
                var (from, to) = (pair[0].GetString()!, pair[1].GetString()!);
                var moved = next.Where(file => file.Key.StartsWith(from + "/", StringComparison.Ordinal)).ToList();
                moved.Should().NotBeEmpty($"the head renames {from}");
                foreach (var file in moved)
                {
                    next.Remove(file.Key);
                    next[to + file.Key[from.Length..]] = file.Value;
                }
            }
        }

        if (head.TryGetProperty("write", out var write))
        {
            foreach (var (path, text) in Files(write))
            {
                next[path] = text;
            }
        }

        return next;
    }

    private async Task Commit(Dictionary<string, string> files)
    {
        foreach (var entry in Directory.GetFileSystemEntries(_repo.Path).Where(entry => Path.GetFileName(entry) != ".git"))
        {
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }

        foreach (var (path, text) in files)
        {
            var full = Path.Combine(_repo.Path, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, text);
        }

        await _repo.CommitAsync($"tree {files.Count}");
    }

    /// <summary>One answer as text: everything a person reads to judge it.</summary>
    internal static string Render(string title, ToolchainResolution resolution)
    {
        var text = new StringBuilder($"\n== {title}\n");
        switch (resolution)
        {
            case ToolchainResolution.NotConfigured notConfigured:
                text.Append($"not_configured: {notConfigured.Note}\n");
                break;
            case ToolchainResolution.Refused refused:
                text.Append($"toolchain_refused: {refused.Sentence}\n");
                break;
            case ToolchainResolution.Resolved resolved:
                var toolchain = resolved.Toolchain;
                text.Append($"resolved: source {toolchain.Source}\n");
                foreach (var project in toolchain.Projects)
                {
                    var detected = project.Detected!;
                    text.Append($"project {project.Id} [{detected.Stack}] origin {project.Origin} definedByHead {project.DefinedByHead} folder {detected.Folder} testsNone {detected.TestsNone}\n");
                    foreach (var (key, value) in detected.Facts)
                    {
                        text.Append($"  fact {key} = {value}\n");
                    }

                    foreach (var note in detected.Notes)
                    {
                        text.Append($"  note {note}\n");
                    }

                    foreach (var step in detected.Steps)
                    {
                        text.Append($"  step {step.Name} network {step.Network} cwd {step.WorkingFolder} subject {step.Subject}{(step.Extensions.Count > 0 ? " extensions " + string.Join(',', step.Extensions) : string.Empty)}: {string.Join(' ', step.Argv)}\n");
                    }
                }

                foreach (var difference in toolchain.Differences)
                {
                    text.Append($"difference: {difference}\n");
                }

                foreach (var lost in toolchain.Changes.LostSteps)
                {
                    text.Append($"lost: {lost}\n");
                }

                foreach (var changed in toolchain.Changes.ChangedDefinitions)
                {
                    text.Append($"changed: {changed}\n");
                }

                foreach (var note in toolchain.Notes)
                {
                    text.Append($"note: {note}\n");
                }

                break;
        }

        return text.ToString();
    }

    private static void Check(string name, string actual)
    {
        if (Environment.GetEnvironmentVariable("COAI_GOLDEN_OUT") is { Length: > 0 } outFolder)
        {
            Directory.CreateDirectory(outFolder);
            File.WriteAllText(Path.Combine(outFolder, name + ".golden.txt"), actual);
            Assert.Fail($"COAI_GOLDEN_OUT is set: the actual answer of {name} was written to {outFolder}, nothing was compared");
        }

        var golden = Path.Combine(TreesFolder, name + ".golden.txt");
        File.Exists(golden).Should().BeTrue($"the golden answer {golden} is missing; set COAI_GOLDEN_OUT to write the actual answer and read it");
        var expected = File.ReadAllText(golden).Replace("\r\n", "\n", StringComparison.Ordinal);

        actual.Should().Be(expected, $"the detection of '{name}' changed; the ACTUAL answer is above in the message, the golden is {golden}");
    }

    /// <summary>The sha of HEAD of a checkout, or empty when it is not a git repository.</summary>
    private sealed class TempHead(IProcessLauncher launcher, string folder)
    {
        public async Task<string> ShaAsync()
        {
            var result = await launcher.RunAsync(new ProcessRequest("git", ["rev-parse", "--verify", "HEAD"], folder) { Timeout = TimeSpan.FromSeconds(30) });

            return result.ExitCode == 0 ? result.StdOut.Trim() : string.Empty;
        }
    }
}
