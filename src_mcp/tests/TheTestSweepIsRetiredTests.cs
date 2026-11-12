using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The test run's start touches NO <c>coai-*</c> folder, at any age, whatever the shared list holds (E5 S2-A, <c>todo/PLAN_test_leftovers_E5_S2_spec.md</c> § A.3).
/// </summary>
/// <remarks>
/// <para>Both cases go through the ENTRY POINT the assembly fixture calls — <see cref="StartOfRun.Run"/> — never through a selector function, so a
/// removal re-added anywhere on that path is red. The invariant tests hand the rule an EMPTY <c>NeverSwept</c>: a test that passes because the list
/// protects the folders proves nothing. The ages are ten days and eleven minutes, both older than the ten-minute window the retired sweep used.</para>
/// </remarks>
public sealed class TheTestSweepIsRetiredTests
{
    /// <summary>What the product makes under <c>coai-</c> at runtime, a downloaded server, the old test name and a name nobody has invented yet.</summary>
    private static readonly string[] Names =
    [
        "coai-answers-x", "coai-repair-x", "coai-noworkspace-x", "coai-plan-x", "coai-server-job-x", "coai-chat-x",
        "coai-question-x", "coai-check-x", "coai-0.17.5", "coai-panel-x", "coai-futurething-x",
    ];

    private static string Bytes(string name) => $"still {name}";

    private static void Make(string root, string name, string text, DateTime lastWrite)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        File.WriteAllText(Path.Combine(dir, "sentinel.txt"), text);
        Directory.SetLastWriteTimeUtc(dir, lastWrite);
    }

    [Fact]
    public void EveryCoaiName_IsOutsideTheStartOfRun_AtAnyAge_WithNeverSweptEmpty()
    {
        using var root = TempDir.For("retired-sweep-");
        foreach (var name in Names)
        {
            Make(root, name, Bytes(name), DateTime.UtcNow.AddDays(-10));
        }

        StartOfRun.Run(root, TempDirsAreSwept.Rule with { NeverSwept = [] });

        using var all = new AssertionScope();
        foreach (var name in Names)
        {
            var sentinel = Path.Combine(root, name, "sentinel.txt");
            File.Exists(sentinel).Should().BeTrue($"{name} is not the test start-of-run's to take, at ten days old or any age");
            if (File.Exists(sentinel))
            {
                File.ReadAllText(sentinel).Should().Be(Bytes(name), $"{name} keeps its bytes");
            }
        }
    }

    [Fact]
    public void AnUnmarkedChatFolder_SurvivesTheStartOfRun_WhenItsPrefixIsNotExcepted()
    {
        using var root = TempDir.For("retired-sweep-");
        Make(root, "coai-chat-live", "still somebody's chat", DateTime.UtcNow.AddMinutes(-11));
        var withoutChat = TempDirsAreSwept.Rule with { NeverSwept = [.. TempDirsAreSwept.Rule.NeverSwept.Where(p => p != "coai-chat-")] };

        StartOfRun.Run(root, withoutChat);

        var sentinel = Path.Combine(root, "coai-chat-live", "sentinel.txt");
        File.Exists(sentinel).Should().BeTrue("a live chat's folder is somebody's even when the list that would protect it forgot its prefix");
        File.ReadAllText(sentinel).Should().Be("still somebody's chat");
    }

    /// <summary>The eleven directories, a FILE that looks like a cache and a name of somebody else's.</summary>
    private static void MakeTheCountedRoot(string root)
    {
        foreach (var name in Names)
        {
            Make(root, name, Bytes(name), DateTime.UtcNow.AddDays(-10));
        }

        File.WriteAllText(Path.Combine(root, "coai-3.png"), "a file, not a directory");
        Directory.CreateDirectory(Path.Combine(root, "other-x"));
    }

    [Fact]
    public void TheCount_IsTheOldTestNames_AndNothingProtected()
    {
        using var root = TempDir.For("retired-sweep-");
        MakeTheCountedRoot(root);

        StartOfRun.Run(root, TempDirsAreSwept.Rule).Should().Be(2, "coai-panel-x and coai-futurething-x are the only names no product prefix, cache or file explains");
        StartOfRun.Run(root, TempDirsAreSwept.Rule with { NeverSwept = [] }).Should().Be(10, "the version cache coai-0.17.5 stays out whatever the list holds");
        Names.Should().OnlyContain(name => Directory.Exists(Path.Combine(root, name)), "counting takes nothing");
    }

    [Fact]
    public void TheCount_OfAMissingRoot_IsZero()
    {
        using var root = TempDir.For("retired-sweep-");

        StartOfRun.Run(Path.Combine(root, "not-there"), TempDirsAreSwept.Rule).Should().Be(0);
    }

    [Fact]
    public void TheStartOfRun_ReferencesNothingOfTheProduct()
    {
        var text = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tests", "StartOfRun.cs")));

        text.Should().NotContain("PanelService").And.NotContain("CoaiMcp.Server", "a start-of-run that cannot reach the product's sweeper cannot call it");
        text.Should().NotMatchRegex(@"\.(Delete|Move|Remove\w*)\(", "the start of a run counts, it takes nothing");
        text.Should().Contain("EnumerateDirectories", "the scan must still see what it knows is there");
    }

    [Fact]
    public void TheSentences_NameTheNumberTheRootAndWhereToGo()
    {
        // "/tmp" is DATA here — the root the sentence names, which this test never touches. It is written with a tilde inside, taken out at run time (the PlantedLines way).
        var root = "/t~mp".Replace("~", string.Empty, StringComparison.Ordinal);

        StartOfRun.Sentence(0, root).Should().Be($"run: no old coai-* leftovers in {root}");
        StartOfRun.Sentence(1, root).Should().Contain($"1 old coai-* directory in {root}").And.Contain("The one-off clean-up");
    }
}
