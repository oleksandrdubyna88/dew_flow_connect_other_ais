using Xunit;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using FluentAssertions;

namespace CoaiMcp.Tests;

/// <summary>Real git: the exclusions and binary handling only mean anything against the real diff.</summary>
public sealed class ContextAssemblerTests : IAsyncLifetime
{
    private readonly ProcessLauncher _launcher = new();
    private readonly ContextAssembler _assembler;
    private string _repo = string.Empty;

    public ContextAssemblerTests() => _assembler = new ContextAssembler(_launcher);

    public async ValueTask InitializeAsync()
    {
        _repo = Directory.CreateTempSubdirectory("coai-ctx-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v1\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, ".gitignore"), "ignored/\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");

        // The branch changes real code, a lock file, a dist artefact, a binary, and an ignored file.
        await File.WriteAllTextAsync(Path.Combine(_repo, "app.cs"), "v2 with a real change\n");
        await File.WriteAllTextAsync(Path.Combine(_repo, "package-lock.json"), "{\"locked\": true}\n");
        Directory.CreateDirectory(Path.Combine(_repo, "dist"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "dist", "bundle.js"), "minified noise\n");
        await File.WriteAllBytesAsync(Path.Combine(_repo, "logo.png"), [0, 1, 2, 0, 255, 0, 7]);
        Directory.CreateDirectory(Path.Combine(_repo, "ignored"));
        await File.WriteAllTextAsync(Path.Combine(_repo, "ignored", "scratch.txt"), "never committed\n");
        await Git("add", ".");
        await Git("commit", "-m", "feature work");
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

    private async Task Git(params string[] args)
    {
        var result = await _launcher.RunAsync(new ProcessRequest(
            "git",
            ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args],
            _repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    private Task<IReadOnlyList<FileDiff>> Collect() =>
        _assembler.CollectAsync(_repo, "main", "feature", ct: TestContext.Current.CancellationToken);

    [Fact]
    public async Task LockFilesAndBuildOutput_NeverReachTheDiff()
    {
        var files = await Collect();

        var paths = files.Select(f => f.Path);
        paths.Should().Contain("app.cs");
        paths.Should().NotContain("package-lock.json", "a context window spent on a lock file is a finding not made");
        paths.Should().NotContain("dist/bundle.js");
    }

    [Fact]
    public async Task GitignoredFiles_NeverReachTheDiff()
    {
        var files = await Collect();

        files.Select(f => f.Path).Should().NotContain(p => p.StartsWith("ignored/"),
            "git never diffs what the repository itself ignores");
    }

    [Fact]
    public async Task Binary_ComesBackNamed_WithItsBlobSize_NotInlined()
    {
        var files = await Collect();

        var binary = files.Should().ContainSingle(f => f.Path == "logo.png").Subject;
        binary.IsBinary.Should().BeTrue();
        binary.BinaryBytes.Should().Be(7);
        binary.Text.Should().BeEmpty();
    }

    [Fact]
    public async Task TheRealChange_RidesWholeIntoTheShapedBundle()
    {
        var shaped = DiffShaper.Shape(await Collect());

        shaped.Text.Should().Contain("real change");
        shaped.WasElided.Should().BeFalse();
    }
}
