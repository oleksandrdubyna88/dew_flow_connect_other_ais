using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A second row on one runtime probes that runtime's CLI, not a program named after the row
/// (todo/PLAN_one_model_catalog.md, epic 2, story 1).
/// </summary>
/// <remarks>
/// The catalog makes "the same vendor many times" ordinary: <c>claude</c> and <c>claude-2</c>, both on the claude runtime.
/// The probe asks <see cref="IReviewerRuntime.DefaultExecutable"/> what to run, and its default was the row id — so
/// <c>providers</c> tried to start a program called <c>claude-2</c> and reported a working reviewer as missing, while the
/// launch, which names its CLI itself, ran fine. One source for the program both use is the cure.
/// </remarks>
public sealed class ARowIdIsNotAProgramTests
{
    [Theory]
    [InlineData("claude-2", "claude", "claude")]
    [InlineData("my-claude", "claude", "claude")]
    [InlineData("codex-2", "codex", "codex")]
    [InlineData("gemini-2", "gemini", "gemini")]
    public void ASecondRowOnACliRuntime_ProbesThatRuntimesCli(string rowId, string runtime, string program)
    {
        var resolved = RuntimeResolution.For(new VendorIdentity(rowId, runtime, string.Empty));

        resolved.Should().NotBeNull();
        resolved!.DefaultExecutable.Should().Be(program, $"a row named '{rowId}' is not a program; the {runtime} CLI is");
    }

    [Fact]
    public void ACodexRowOnAnEndpoint_ProbesTheCodexCli()
    {
        var resolved = RuntimeResolution.For(new VendorIdentity("deepseek-2", "codex", "https://api.deepseek.example/v1"));

        resolved!.DefaultExecutable.Should().Be("codex");
    }
}
