using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A hosted OpenAI-compatible API, reviewed with directly: this binary in <c>--ask-api</c> mode, the
/// key in the child's environment and nowhere else, no engine to wait for.
/// </summary>
/// <remarks>
/// Story S1.2 of <c>PLAN_feature_review.md</c>. The three things this adapter must get right are the
/// three things a wrong adapter gets silently wrong: a key on a command line is in every process
/// listing; a shared resource would queue Grok behind a local GPU; and a dialect that is not passed
/// spells the request the way a local engine wants it, which a hosted reasoning model refuses.
/// </remarks>
public sealed class ApiRuntimeTests
{
    private const string Key = "sk-test-0123456789abcdefghijklmnop";

    private static ReviewerSettings Settings(string dialect = "", string effort = "") =>
        new("grok") { Model = "grok-4", ApiKey = Key, Dialect = dialect, ReasoningEffort = effort, Timeout = TimeSpan.FromMinutes(5) };

    private static ReviewerInvocation Build(ReviewerSettings settings, string baseUrl = "https://api.x.ai/v1") =>
        new ApiRuntime("grok", baseUrl).Build(RoleCatalog.ArchitectureRole, "the prompt", "/tmp/wt", "/tmp/schema.json", Path.GetTempPath(), settings);

    [Fact]
    public void ItLaunchesThisBinaryInAskApiMode_ForItsOwnVendor()
    {
        var invocation = Build(Settings());

        invocation.Request.Arguments.Should().Contain("--ask-api");
        invocation.Request.Arguments.Should().ContainInOrder("--vendor", "grok");
        invocation.Request.Arguments.Should().ContainInOrder("--endpoint", "https://api.x.ai/v1");
        invocation.Request.Arguments.Should().ContainInOrder("--model", "grok-4");
        Path.GetFileNameWithoutExtension(invocation.Request.Executable).Should().NotBe("codex").And.NotBe("agy").And.NotBe("claude");
    }

    [Fact]
    public void TheKeyIsInTheEnvironment_AndNoArgvElementCarriesIt()
    {
        var invocation = Build(Settings());

        invocation.Request.Environment.Should().ContainKey(ApiRuntime.KeyVariable).WhoseValue.Should().Be(Key);
        invocation.Request.Arguments.Should().NotContain(a => a.Contains(Key),
            "a command line is visible to every process listing on the machine and ends up in logs");
        File.ReadAllText(invocation.Request.Arguments.ElementAt(invocation.Request.Arguments.ToList().IndexOf("--prompt-file") + 1))
            .Should().NotContain(Key, "the prompt file is kept on disk as evidence");
    }

    [Fact]
    public void WithoutAKey_NothingIsPutInTheEnvironment_AndTheShimRefusesInstead()
    {
        var invocation = Build(Settings() with { ApiKey = string.Empty });

        invocation.Request.Environment.Should().NotContainKey(ApiRuntime.KeyVariable);
    }

    [Fact]
    public void ItDeclaresNoSharedResource_SoItTakesTheMachineLane_NotTheEngineLane()
    {
        var invocation = Build(Settings());

        invocation.SharedResource.Should().BeEmpty("a vendor's fleet is bounded by its rate limit, not by a card on this machine");
        invocation.IsOnEngine.Should().BeFalse("the engine lane and EngineLease are for local engines only");
    }

    [Fact]
    public void ARowThatNamesNoDialect_SpeaksTheGenericOne()
    {
        Build(Settings()).Request.Arguments.Should().ContainInOrder("--dialect", "openai");
        Build(Settings(dialect: " OpenAI ")).Request.Arguments.Should().ContainInOrder("--dialect", "openai");
    }

    [Fact]
    public void TheEffortTravelsOnlyWhenSet_AndIsRecordedOnTheLaunch()
    {
        var none = Build(Settings());
        var high = Build(Settings(effort: " high "));

        none.Request.Arguments.Should().NotContain("--reasoning-effort");
        none.Effort.Should().BeEmpty();
        high.Request.Arguments.Should().ContainInOrder("--reasoning-effort", "high");
        high.Effort.Should().Be("high");
    }

    [Fact]
    public void ABaseUrlWithoutV1_GetsIt_AndAnEmptyOneStaysEmptyForTheShimToRefuse()
    {
        Build(Settings(), "https://api.x.ai").Request.Arguments.Should().ContainInOrder("--endpoint", "https://api.x.ai/v1");
        Build(Settings(), string.Empty).Request.Arguments.Should().ContainInOrder("--endpoint", string.Empty);
    }

    [Fact]
    public void TheDllComesBeforeOurOwnFlags_WhenRunThroughTheDotnetHost()
    {
        var arguments = Build(Settings()).Request.Arguments.ToList();
        var (_, prefix) = LocalRuntime.SelfInvocation();

        arguments.Take(arguments.IndexOf("--ask-api")).Should().Equal(prefix);
    }

    [Fact]
    public void UsageIsReadOffTheShimsStdoutLine_WithNoMoney()
    {
        var runtime = new ApiRuntime("grok", "https://api.x.ai/v1");
        var invocation = Build(Settings());

        var usage = runtime.ReadUsage(invocation, new ProcessResult(0, "{\"tokensIn\":120,\"tokensOut\":34}", "", false));

        usage.TokensIn.Should().Be(120);
        usage.TokensOut.Should().Be(34);
        usage.CostUsd.Should().BeNull("the endpoint reports tokens; the panel's own rate prices them");
        runtime.ReadUsage(invocation, new ProcessResult(0, "not json", "", false)).TokensIn.Should().Be(0);
    }

    [Fact]
    public void TheAnswerIsReadFromTheNamedFile()
    {
        var invocation = Build(Settings());
        File.WriteAllText(invocation.OutputFile, """{"findings":[]}""");

        ((IReviewerRuntime)new ApiRuntime("grok", "https://api.x.ai/v1"))
            .ReadAnswer(invocation, new ProcessResult(0, "", "", false))
            .Should().Be("""{"findings":[]}""");
    }

    [Fact]
    public void TheSelectorKnowsTheName_AndTheMachineOnlySetHoldsIt()
    {
        ReviewerRuntimeSelector.RuntimeNames.Should().Contain("api");
        ReviewerRuntimeSelector.MachineOnlyRuntimes.Should().BeEquivalentTo(["local", "api"]);
        ReviewerRuntimeSelector.MachineOnlyRuntimes.Should().BeSubsetOf(ReviewerRuntimeSelector.RuntimeNames);
    }
}
