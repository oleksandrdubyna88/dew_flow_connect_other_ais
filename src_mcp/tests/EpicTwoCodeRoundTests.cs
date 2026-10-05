using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The rest of epic 2's code round (coai session 589a145b): what each runtime is told is decided by the runtime, a
/// consultant on somebody else's endpoint cannot lose its provider by a cast, and a placement that fails leaves the
/// prompt as it was.
/// </summary>
public sealed class EpicTwoCodeRoundTests
{
    private static ProviderSettings Row(string id, string runtime, string effort = "") =>
        new(id) { Enabled = true, Runtime = runtime, Model = "m", CliEffort = effort, BaseUrl = runtime == "local" ? "http://127.0.0.1:11434" : string.Empty };

    [Theory]
    [InlineData("codex")]
    [InlineData("antigravity")]
    public void ARuntimeThatTakesNoLocalEffort_IsToldNone(string runtime) =>
        // The panel's local-engine setting was handed to every other runtime "because only a local engine reads it" — so
        // the first new runtime that does read ReasoningEffort would have been sent a llama.cpp level.
        RosterBuilder.EffortFor(Row(runtime, runtime), localEffort: "low").Should().BeEmpty();

    [Fact]
    public void ALocalRow_StillFallsBackToThePanelsSetting() =>
        RosterBuilder.EffortFor(Row("local", "local"), localEffort: "low").Should().Be("low");

    [Fact]
    public void WhetherARowsTimeoutApplies_IsTheRuntimesToSay()
    {
        ((IReviewerRuntime)new ApiRuntime("qwen", "https://api.example/v1")).TakesItsOwnTimeout.Should().BeFalse("its limit is the whole review");
        ((IReviewerRuntime)new ClaudeRuntime()).TakesItsOwnTimeout.Should().BeTrue();
        RosterBuilder.TimeoutFor(Row("claude", "claude") with { TimeoutMinutes = 3 }, new ClaudeRuntime(), TimeSpan.FromMinutes(10))
            .Should().Be(TimeSpan.FromMinutes(3));
        RosterBuilder.TimeoutFor(Row("qwen", "api") with { TimeoutMinutes = 3 }, new ApiRuntime("qwen", "https://api.example/v1"), TimeSpan.FromMinutes(10))
            .Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void ACodexConsultant_CanOnlyBeBuiltOnTheCodexRuntime()
    {
        // The provider overrides and the key's variable were read through `inner as CodexRuntime` — any other runtime
        // quietly became OpenAI's service with the endpoint's key. The constructor now takes nothing else.
        typeof(CodexConsultant).GetConstructors().Should().ContainSingle()
            .Which.GetParameters()[0].ParameterType.Should().Be<CodexRuntime>();
    }

    [Fact]
    public void APromptWithNoContractHeading_IsLeftAsItWas()
    {
        PersonInstruction.HasContract("review this").Should().BeFalse();
        PersonInstruction.PlacedIn("review this", "be terse").Should().Be("review this", "a failed placement is never an empty prompt");
        PersonInstruction.HasContract($"x\n{PersonInstruction.ContractHeading}\n").Should().BeTrue();
    }

    [Fact]
    public async Task AnUnreadableSystemPromptFile_IsCalledThat()
    {
        var dir = Directory.CreateTempSubdirectory("coai-c2-ask-").FullName;
        var prompt = Path.Combine(dir, "p.prompt");
        await File.WriteAllTextAsync(prompt, "review this");
        var token = Path.Combine(dir, "t.token");
        await File.WriteAllTextAsync(token, "a-token");
        var notes = new List<string>();

        var exit = await AskRemote.RunAsync(
            new Dictionary<string, string>
            {
                ["--server"] = "https://coai.example",
                ["--vendor"] = "claude",
                ["--prompt-file"] = prompt,
                ["--out"] = Path.Combine(dir, "a.json"),
                ["--token-file"] = token,
                ["--system-prompt-file"] = Path.Combine(dir, "missing.system"),
            },
            notes.Add,
            TextWriter.Null);

        exit.Should().Be(RemoteAsk.BadUsage);
        notes.Should().ContainSingle().Which.Should().StartWith("the system prompt file");
    }
}
