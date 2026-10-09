using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A runtime that writes its prompt to a file names that file on the invocation, so the scheduler can delete it when
/// the turn ends (<see cref="ReviewerInvocation.TempFiles"/>; research/PLAN_one_model_catalog.md, epic 2).
/// </summary>
public sealed class ARuntimeNamesThePromptFileItWroteTests : IDisposable
{
    private readonly string _out = Directory.CreateTempSubdirectory("coai-prompt-named-").FullName;

    public static TheoryData<string> FileRuntimes => ["api", "local", "remote"];

    [Theory]
    [MemberData(nameof(FileRuntimes))]
    public void ThePromptFileItWrote_IsTheOneItNames(string kind)
    {
        IReviewerRuntime runtime = kind switch
        {
            "api" => new ApiRuntime("qwen", "https://q.example/v1"),
            "local" => new LocalRuntime("local", "http://127.0.0.1:11434"),
            _ => new RemoteRuntime("srv-codex", "https://coai.example", "codex"),
        };

        var invocation = runtime.Build(RoleCatalog.ArchitectureRole, "the change", _out, "D:/s.json", _out, new ReviewerSettings(runtime.Provider));
        var written = Directory.GetFiles(_out, "*.prompt");

        written.Should().ContainSingle();
        invocation.TempFiles.Should().Equal(written, "the file it wrote is the file the scheduler is told to delete");
    }

    [Fact]
    public void ACliRuntime_WritesNoPromptFile_AndNamesNone()
    {
        var invocation = new ClaudeRuntime().Build(RoleCatalog.ArchitectureRole, "the change", _out, "D:/s.json", _out, new ReviewerSettings("claude"));

        invocation.TempFiles.Should().BeEmpty("a CLI reviewer is handed its prompt on stdin");
        Directory.GetFiles(_out, "*.prompt").Should().BeEmpty();
    }

    public void Dispose() => Directory.Delete(_out, recursive: true);
}
