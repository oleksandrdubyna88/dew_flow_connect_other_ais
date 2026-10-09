using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A row whose runtime this build does not run is refused, naming that runtime — never run as something else
/// (research/PLAN_one_model_catalog.md, epic 2, story 1).
/// </summary>
/// <remarks>
/// <para>It used to become <c>codex</c> at parsing, so "a name from a newer panel still launches something". What it
/// launched was the Codex CLI on the person's own codex account, for a row they had set to a different runtime — the
/// failure <c>api</c> was once the example of, when a Qwen row rode the Codex CLI against xAI's endpoint. With the
/// catalog a newer extension writing a runtime this binary predates is the ordinary case, and <c>--features</c> is
/// what tells the extension so; the binary's part is to refuse the row by its runtime's name.</para>
/// <para>The id must not stand in either: <c>For</c> fell back to the row id, so a row called <c>claude</c> with an
/// unknown runtime ran the claude CLI.</para>
/// </remarks>
public sealed class AnUnknownRuntimeIsRefusedByNameTests
{
    [Fact]
    public void Parsing_KeepsTheRuntimeItWasGiven_SoTheRefusalCanNameIt()
    {
        PanelSettings.ParseVendors("""[{"id":"x","runtime":"Llama.CPP"}]""")[0].Runtime
            .Should().Be("llama.cpp", "a row is not quietly made into a codex row");
    }

    [Fact]
    public void AnUnknownRuntime_HasNoAdapter_EvenWhenTheRowIdNamesOne()
    {
        RuntimeResolution.For(new VendorIdentity("claude", "llama.cpp", string.Empty))
            .Should().BeNull("the row said llama.cpp; running the claude CLI because of its id is a guess");
        RuntimeResolution.For(new VendorIdentity("x", "llama.cpp", "https://api.example/v1"))
            .Should().BeNull("an endpoint does not make an unknown runtime a codex one");
    }

    [Fact]
    public void AnAbsentRuntime_StillLetsTheIdDecide()
    {
        RuntimeResolution.For(new VendorIdentity("claude", string.Empty, string.Empty)).Should().BeOfType<ClaudeRuntime>();
        RuntimeResolution.For(new VendorIdentity("deepseek", string.Empty, string.Empty)).Should().BeOfType<DeepseekRuntime>();
    }

    [Fact]
    public async Task TheProbe_NamesTheRuntime_AndWhatThisBuildRuns()
    {
        var health = await VendorProbe.RunAsync(
            new NeverLaunched(), new VendorIdentity("claude", "llama.cpp", string.Empty), enabled: true,
            executablePath: string.Empty, model: string.Empty, hasVaultKey: false, TestContext.Current.CancellationToken);

        health.Auth.Should().Be("unavailable");
        health.Note.Should().Contain("'llama.cpp'").And.Contain("codex").And.Contain("local").And
            .NotContain("unknown provider 'claude'", "the row id is not what is wrong with the row");
    }

    [Fact]
    public void TheRefusal_ListsEveryRuntimeThisBuildRuns()
    {
        var refusal = RuntimeResolution.UnknownRuntime("llama.cpp");

        refusal.Should().Contain("'llama.cpp'");
        foreach (var runtime in ReviewerRuntimeSelector.RuntimeNames)
        {
            refusal.Should().Contain(runtime);
        }
    }

    private sealed class NeverLaunched : IProcessLauncher
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException($"a row with an unknown runtime must not start '{request.Executable}'");
    }
}
