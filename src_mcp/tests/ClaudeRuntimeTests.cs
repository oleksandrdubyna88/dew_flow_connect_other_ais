using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What a Claude reviewer is told it may not do — both ways, because the two callers want opposite
/// things from the one adapter.
/// </summary>
/// <remarks>
/// <para>The local code round hands its reviewer a read-only worktree so it can check the diff
/// against the code around it. The Team server hands its reviewer an empty directory and a prompt
/// that already carries the diff, on a box where a <c>Read</c> reaches every other slot's sign-in.
/// <see cref="ReviewerSettings.Confined"/> is the switch between them.</para>
/// <para>These assert what is SENT. Whether the CLI honours a name in that list is the CLI's
/// decision, and it is not observed here — the adapter's own remarks say what was checked.</para>
/// </remarks>
public sealed class ClaudeRuntimeTests
{
    private const string Worktree = "D:/storage/coai-wt-s1-r1";
    private const string Schema = "D:/storage/schema.json";
    private const string OutDir = "D:/storage/out";

    private const string Flag = "--disallowedTools";

    /// <summary>The tools that change the tree — denied to every reviewer, confined or not.</summary>
    private static readonly string[] Writes = ["Edit", "Write", "NotebookEdit"];

    /// <summary>The tools that reach past the prompt: the filesystem, a shell, the web, a sub-agent.</summary>
    private static readonly string[] ReachesPastThePrompt =
        ["Bash", "Read", "Glob", "Grep", "WebFetch", "WebSearch", "Task", "Agent"];

    [Fact]
    public void AConfinedReviewerIsDeniedEveryFileAndShellTool()
    {
        var denied = Denied(new("claude") { Confined = true });

        denied.Should().Contain(ReachesPastThePrompt,
            "on the Team server every one of these is a way to read another slot's credentials and quote them back");
        denied.Should().Contain(Writes, "the write denial does not go away when the reach denial arrives");
    }

    [Fact]
    public void AnUnconfinedReviewerKeepsRead()
    {
        var denied = Denied(new("claude"));

        denied.Should().BeEquivalentTo(Writes,
            "the local code round reads its worktree, and nothing that already calls this adapter asked for less");
    }

    /// <summary>The names that follow the one <c>--disallowedTools</c>, up to the next flag.</summary>
    private static List<string> Denied(ReviewerSettings settings)
    {
        var args = new ClaudeRuntime()
            .Build(ReviewRole.Architecture, "review this", Worktree, Schema, OutDir, settings)
            .Request.Arguments;

        args.Should().ContainSingle(a => a == Flag,
            "a variadic option given twice is whichever the CLI reads last, and that is not a list anybody wrote");

        return args
            .SkipWhile(a => a != Flag)
            .Skip(1)
            .TakeWhile(a => !a.StartsWith("--", StringComparison.Ordinal))
            .ToList();
    }
}
