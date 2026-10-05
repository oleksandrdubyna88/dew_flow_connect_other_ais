using CoaiMcp.Core.Collecting;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The ranking model is allowed by the RUNTIME of the row it names, not by the row being called <c>local</c>
/// (todo/PLAN_one_model_catalog.md, epic 2, story 1).
/// </summary>
/// <remarks>
/// <para>A Bugz model is <c>rowId/model</c>, and the allowlist compared the row id: a second local instance
/// (<c>local-2</c>) — or the <c>bugz-local</c> row the catalog migration makes — was refused although it runs on this
/// machine, and a row named <c>local</c> pointed at a cloud runtime would have passed. The runtime is what decides
/// whether a finding's un-anonymised words leave the machine, so it is what the list is matched against.</para>
/// <para>The caller says the runtime (<c>--runtime</c>, resolved from the catalog row); without it the row id stands in
/// for it, which is exactly what a terminal's <c>--model local/qwen</c> and an older extension have always meant.</para>
/// </remarks>
public sealed class BugzRankingMatchesTheRuntimeTests
{
    [Theory]
    [InlineData("local-2/qwen3.5:35b", "local")]
    [InlineData("bugz-local/qwen3.5:35b", "local")]
    [InlineData("LOCAL-2/qwen", "Local")]
    public void ARowOnTheLocalRuntime_IsAllowed_WhateverItIsCalled(string model, string runtime)
    {
        RankingModels.IsAllowed(model, runtime).Should().BeTrue($"'{model}' runs on '{runtime}', on this machine");
    }

    [Fact]
    public void ARowCalledLocal_OnACloudRuntime_IsRefused()
    {
        RankingModels.IsAllowed("local/gpt-5", "codex").Should().BeFalse("the name is not where the text goes; the runtime is");
    }

    [Theory]
    [InlineData("local/qwen", true)]
    [InlineData("local-2/qwen", false)]
    [InlineData("gemini/pro", false)]
    [InlineData("", true)]
    public void WithNoRuntimeSaid_TheRowIdStandsInForIt_AsItAlwaysHas(string model, bool allowed)
    {
        RankingModels.IsAllowed(model, runtime: string.Empty).Should().Be(allowed);
    }

    [Fact]
    public void TheRefusal_NamesTheRuntimeThatWasRefused()
    {
        var refusal = RankingModels.Refusal("local/gpt-5", "codex");

        refusal.Should().Contain("'local/gpt-5'").And.Contain("'codex'").And.Contain("Allowed runtimes: local");
    }
}
