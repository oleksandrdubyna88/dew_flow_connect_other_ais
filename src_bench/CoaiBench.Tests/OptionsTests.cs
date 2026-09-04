using Xunit;
using FluentAssertions;
using CoaiBench.Cli;
using CoaiBench.Model;

namespace CoaiBench.Tests;

/// <summary>
/// The command line, which is the whole reason this project exists rather than a script.
/// </summary>
/// <remarks>
/// Each of these shapes had been typed by hand into a throwaway harness at least twice. Written
/// down, they are a surface with tests; written again each time, they are a slightly different
/// measurement each time — which is the cost that mattered, not the typing.
/// </remarks>
public sealed class OptionsTests
{
    private static Options Parse(params string[] args)
    {
        var (options, refusal) = OptionsParser.Parse(args);
        options.Should().NotBeNull(refusal);

        return options!;
    }

    [Fact]
    public void NothingAtAll_IsTheUsage() =>
        OptionsParser.Parse([]).Refusal.Should().Contain("coai-bench");

    [Fact]
    public void AnUnknownFlag_IsRefusedByName() =>
        OptionsParser.Parse(["run", "--nonsense", "x"]).Refusal.Should().Contain("--nonsense");

    // ---------- the runs that kept being written by hand ----------

    [Fact]
    public void EveryVendorThreeTimes()
    {
        var options = Parse("run", "--arm", "codex", "--arm", "gemini", "--arm", "local", "--repeat", "3");

        options.Arms.Should().Equal("codex", "gemini", "local");
        options.Repeat.Should().Be(3);
    }

    [Fact]
    public void OneModelOnItsOwn()
    {
        var options = Parse("run", "--arm", "codex", "--model", "codex=gpt-5.6-sol");

        options.Arms.Should().ContainSingle();
        options.Models["codex"].Should().Be("gpt-5.6-sol");
    }

    [Fact]
    public void TheSameModelLocalAndHosted()
    {
        // Two arms of one vendor each, told apart by the model they are given — which is how "is
        // the local one as good as the hosted one" was asked every previous time.
        var options = Parse(
            "run", "--arm", "local", "--arm", "codex",
            "--model", "local=Qwen3.5-35B", "--model", "codex=gpt-5.6-sol");

        options.Arms.Should().HaveCount(2);
        options.Models.Should().HaveCount(2);
    }

    [Fact]
    public void ThreeVendorsInONEArm_IsOneRoundOfThree()
    {
        // A comma inside an arm is a SET, not a list of arms: this is the ordinary round with three
        // vendors fanning out inside it, which is a different measurement from three arms.
        var options = Parse("run", "--arm", "codex,gemini,local");

        options.Arms.Should().ContainSingle().Which.Should().Be("codex,gemini,local");
    }

    [Fact]
    public void PlansOnly_DiffsOnly_AndBoth()
    {
        Parse("run", "--stages", "plans").Stages.Should().Be(Stages.Plans);
        Parse("run", "--stages", "diffs").Stages.Should().Be(Stages.Diffs);
        Parse("run", "--arm", "codex").Stages.Should().Be(Stages.Both);
    }

    [Fact]
    public void FiveWindows()
    {
        Parse("run", "--parallel", "5").Parallel.Should().Be(5);
    }

    [Fact]
    public void ASettingIsHandedToEveryServer()
    {
        var options = Parse("run", "--set", "COAI_MAX_CONCURRENCY=9", "--set", "COAI_SPLIT_PLAN=true");

        options.Settings["COAI_MAX_CONCURRENCY"].Should().Be("9");
        options.Settings["COAI_SPLIT_PLAN"].Should().Be("true");
    }

    [Fact]
    public void FlagsMayBeWrittenWithAnEquals() =>
        Parse("run", "--repeat=3", "--parallel=5").Should()
            .BeEquivalentTo(new { Repeat = 3, Parallel = 5 }, o => o.ExcludingMissingMembers());

    [Fact]
    public void TheJudgeIsFableUnlessToldOtherwise()
    {
        Parse("judge", "--runs", "x.json").Judge.Should().Be("claude-fable-5-1");
        Parse("judge", "--runs", "x.json", "--judge", "claude-opus-5").Judge.Should().Be("claude-opus-5");
    }

    [Fact]
    public void APairWithoutAValue_IsRefused() =>
        OptionsParser.Parse(["run", "--model", "codex"]).Refusal.Should().NotBeEmpty();
}
