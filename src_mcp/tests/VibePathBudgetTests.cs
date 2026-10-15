using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Vibe;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A Vibe home whose deepest write would pass Windows' path limit is refused before anything starts (D2 of
/// <c>todo/PLAN_vibe_live_round_fixes.md</c>; research/RESULTS_vibe_confinement.md §27): Vibe writes 156 characters below its
/// home, a FILE path stops at 259, so a home over 102 characters dies after the launch was paid for.
/// </summary>
/// <remarks>Pure: nothing is made on disk, so a path is a string padded to the length the case needs.</remarks>
public sealed class VibePathBudgetTests
{
    private static readonly VibePathBudget Windows = VibePathBudget.Windows;

    /// <summary>An absolute home of exactly <paramref name="length"/> UTF-16 units — a folder nobody makes, padded with segments of x.</summary>
    private static string HomeOfLength(int length, string tail = "")
    {
        var home = Path.Combine(Path.GetTempPath(), "testsToDelete-vh-budget") + tail;
        while (home.Length < length)
        {
            home += Path.DirectorySeparatorChar + new string('x', Math.Min(40, Math.Max(1, length - home.Length - 1)));
        }

        home.Length.Should().Be(length, "the fixture is exactly the length the case names");

        return home;
    }

    [Fact]
    public void AHomeOf103Characters_IsRefused_NamingTheNumbersAndTheSetting()
    {
        var home = HomeOfLength(103);

        var said = Windows.Refusal(home);

        said.Should().Contain(home).And.Contain("103").And.Contain("260").And.Contain("259").And.Contain("156")
            .And.Contain("COAI_CONSULTANT_HOMES");
        said.Should().NotContain(@"C:\coai", "the sentence names no example folder (D2, review R5)");
    }

    [Fact]
    public void The102CharacterHome_IsTheLargestAccepted_AndThe103rdTheFirstRefused()
    {
        Windows.Refusal(HomeOfLength(102)).Should().BeEmpty("102 + 1 + 156 = 259, the longest file path Windows allows");
        Windows.Refusal(HomeOfLength(103)).Should().NotBeEmpty("103 + 1 + 156 = 260");
    }

    [Fact]
    public void TheLimitIsCountedInUtf16Units_NotBytes()
    {
        // 102 UTF-16 units holding one é: 103 UTF-8 bytes. A guard that counted bytes would refuse it; Windows counts units (§27).
        var home = HomeOfLength(102, tail: "é");

        home.Length.Should().Be(102);
        System.Text.Encoding.UTF8.GetByteCount(home).Should().NotBe(home.Length);
        Windows.Refusal(home).Should().BeEmpty();
    }

    [Fact]
    public void NoBudget_RefusesNothing_AndTheWindowsValuesAreWhatWasProbed()
    {
        VibePathBudget.None.Refusal(HomeOfLength(300)).Should().BeEmpty();
        Windows.Should().Be(new VibePathBudget(259, 156));
        (OperatingSystem.IsWindows() ? VibePathBudget.ForThisProcess : VibePathBudget.None).Should().Be(
            VibePathBudget.ForThisProcess, "off Windows the process's budget is none — a Windows number for a platform nobody probed is not written down");
        if (!OperatingSystem.IsWindows())
        {
            VibePathBudget.ForThisProcess.Should().Be(VibePathBudget.None);
        }
        else
        {
            VibePathBudget.ForThisProcess.Should().Be(Windows);
        }
    }

    /// <summary>A5: the default-root shape, as strings — each site's home at the profile lengths D1's table promises.</summary>
    [Theory]
    [InlineData("round", 55, 56)]
    [InlineData("row", 60, 61)]
    [InlineData("conversation", 34, 35)]
    public void EachSitesHome_FitsTheBudget_UpToTheProfileLengthD1Names_AndNotOneCharacterMore(string site, int fits, int fails)
    {
        string HomeAt(int profile)
        {
            var root = @"C:\Users\" + new string('u', profile) + @"\AppData\Local";

            return site switch
            {
                "round" => VibeRuntime.HomeFor(VibeHomes.OneShotIn(root, "a1b2c3d4"), "vibe", "PlanCritique"),
                "row" => VibeRuntime.ConsultantHome(VibeHomes.OneShotIn(root, "a1b2c3d4")),
                _ => VibeRuntime.ConsultantHome(ConsultantHomes.ConversationFolder(root, ConsultantHomes.Vibe, new string('a', 32))),
            };
        }

        Windows.RefusalForLength(HomeAt(fits), HomeAt(fits).Length).Should().BeEmpty($"{site} at a profile of {fits}");
        Windows.RefusalForLength(HomeAt(fails), HomeAt(fails).Length).Should().NotBeEmpty($"{site} at a profile of {fails}");
    }

    /// <summary>The test seam every Windows-only scenario leans on: the budget it names is the process's until it is disposed, then the real one is back.</summary>
    [Fact]
    public void UsingABudget_ReplacesTheProcessesUntilItIsDisposed_ThenRestoresTheOneBefore()
    {
        var real = VibePathBudget.ForThisProcess;
        var narrow = new VibePathBudget(100, 50);

        using (VibePathBudget.Using(narrow))
        {
            VibePathBudget.ForThisProcess.Should().BeSameAs(narrow);
            using (VibePathBudget.Using(VibePathBudget.None))
            {
                VibePathBudget.ForThisProcess.Should().BeSameAs(VibePathBudget.None, "a nested seam wins while it lasts");
            }

            VibePathBudget.ForThisProcess.Should().BeSameAs(narrow, "leaving the inner one puts the outer one back");
        }

        VibePathBudget.ForThisProcess.Should().BeSameAs(real);
    }
}
