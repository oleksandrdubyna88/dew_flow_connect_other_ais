using CoaiMcp.Runners.Reviewers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The model an Antigravity row launches with when it names none must be one agy offers
/// (research/PLAN_the_agy_default_is_a_model_agy_offers.md): agy 1.4.4 refuses <c>gemini-3.7-flash-high</c> before any
/// model call ("invalid model selection ... not recognized"), so a row with no model of its own never ran. The default
/// assumes agy 1.4.4 or later, where <c>agy models</c> lists the 3.8 Flash ids.
/// </summary>
public sealed class AntigravityDefaultModelTests
{
    private static readonly string Repo = Path.GetTempPath();

    private static string[] ModelFlag(ReviewerSettings settings)
    {
        var args = new AntigravityRuntime()
            .Build("arch", "p", Repo, Path.Combine(Repo, "s.json"), Repo, settings).Request.Arguments.ToArray();
        var at = Array.IndexOf(args, "--model");
        at.Should().BeGreaterThanOrEqualTo(0, "agy is always told which model to run");
        return args[at..(at + 2)];
    }

    [Fact]
    public void ARowWithNoModel_LaunchesTheDefaultAgyOffers()
    {
        ModelFlag(new ReviewerSettings("antigravity")).Should().Equal("--model", "gemini-3.8-flash-low");
        AntigravityRuntime.DefaultModel.Should().Be("gemini-3.8-flash-low");
    }

    [Fact]
    public void ARowThatNamesAModel_KeepsIt_EvenOneAgyNoLongerOffers()
    {
        // A saved 3.7 row is not rewritten silently: agy refuses it with its own sentence (plan, "What must be true" 4).
        ModelFlag(new ReviewerSettings("antigravity") { Model = "gemini-3.7-flash-high" })
            .Should().Equal("--model", "gemini-3.7-flash-high");
    }
}
