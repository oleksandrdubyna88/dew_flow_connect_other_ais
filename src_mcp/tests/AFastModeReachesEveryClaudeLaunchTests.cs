using CoaiMcp.Core.Catalog;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A claude row's fast mode reaches every claude launch (todo/PLAN_fast_mode.md, Story A): a <c>--settings</c> FILE
/// holding exactly one key, <c>fastMode</c> — the documented headless spelling (research/RESULTS_fast_mode_measured_
/// 2026-10-07.md), as a path rather than inline JSON, whose quotes an npm <c>.cmd</c> shim re-tokenises. Only an Opus
/// model has a fast tier; any other model, and an empty one (the CLI's own default), is sent nothing in either state.
/// </summary>
public sealed class AFastModeReachesEveryClaudeLaunchTests : IDisposable
{
    private const string Repo = "D:/rsd/some-checkout";

    private readonly string _data = Directory.CreateTempSubdirectory("coai-fast-claude-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not the behaviour under test.
        }
    }

    private ReviewerSettings Settings(FastMode fast, string model) =>
        new("claude") { Fast = fast, Model = model, DataDir = _data, ClaudeCli = ClaudeCapability.WithRestricted };

    private IReadOnlyList<string> Reviewer(FastMode fast, string model) =>
        new ClaudeRuntime().Build(RoleCatalog.ArchitectureRole, "review this", Repo, "D:/s.json", "D:/out", Settings(fast, model)).Request.Arguments;

    private IReadOnlyList<string> Consultant(FastMode fast, string model) =>
        new ClaudeConsultant(new ClaudeRuntime())
            .Build(new ConsultantLaunch(Repo, "help me", string.Empty, "D:/answers", Settings(fast, model)))
            .Request.Arguments;

    /// <summary>What the <c>--settings</c> file says, or null when the launch carries none.</summary>
    private static string? SettingsSent(IReadOnlyList<string> args)
    {
        var at = args.ToList().IndexOf("--settings");

        return at < 0 ? null : File.ReadAllText(args[at + 1]);
    }

    [Theory]
    [InlineData(FastMode.Off, "{\"fastMode\":false}")]
    [InlineData(FastMode.On, "{\"fastMode\":true}")]
    public void AnOpusReviewerAndConsultant_AreToldTheirState_ThroughAOneKeyFile(FastMode fast, string file)
    {
        SettingsSent(Reviewer(fast, "opus")).Should().Be(file);
        SettingsSent(Consultant(fast, "claude-opus-5-5")).Should().Be(file);
    }

    [Theory]
    [InlineData("opus")]
    [InlineData("claude-opus-5-5")]
    [InlineData("claude-opus-5-5[1m]")]
    [InlineData("claude-opus-5")]
    [InlineData("claude-opus-4-8")]
    public void EveryOpusSpelling_HasTheTier(string model)
    {
        SettingsSent(Reviewer(FastMode.On, model)).Should().Be("{\"fastMode\":true}", model);
    }

    [Theory]
    [InlineData("sonnet")]
    [InlineData("claude-sonnet-5")]
    [InlineData("claude-opus-4-1")]
    [InlineData("")]
    public void AModelWithoutTheTier_IsSentNothing_InEitherState(string model)
    {
        SettingsSent(Reviewer(FastMode.On, model)).Should().BeNull(model);
        SettingsSent(Reviewer(FastMode.Off, model)).Should().BeNull(model);
    }

    [Fact]
    public void AsTheCliIsSet_SendsNothing()
    {
        SettingsSent(Reviewer(FastMode.Cli, "opus")).Should().BeNull();
        SettingsSent(Consultant(FastMode.Cli, "opus")).Should().BeNull();
    }

    [Fact]
    public void TheFile_IsWrittenOnce_AndOnlyRewrittenWhenItDiffers()
    {
        var first = Reviewer(FastMode.On, "opus");
        first.Should().Contain("--settings");
        var path = first[first.ToList().IndexOf("--settings") + 1];
        File.Exists(path).Should().BeTrue();
        var written = File.GetLastWriteTimeUtc(path);

        Reviewer(FastMode.On, "opus");

        File.GetLastWriteTimeUtc(path).Should().Be(written, "several processes start launches; an unchanged file is left alone");
    }
}
