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
}
