using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// What <c>PanelService.PruneOldScratchDirs</c> DOES, recorded before E5 S2-A touches anything around it
/// (<c>todo/PLAN_test_leftovers_E5_S2_spec.md</c> § A.4).
/// </summary>
/// <remarks>
/// <para>A characterization test states the function's observed behaviour, not what somebody wishes it did: if a result here ever
/// differs from what the function gives, the test records the result and the change says so — it never corrects the function.
/// They landed FIRST in the story, green on the unchanged function, and they change in no later E5 PR: the story that retires the test
/// sweep must not move the product's own sweep by a byte.</para>
/// <para>Every case makes its own private root (<see cref="TempDir.For"/>, inside the run's folder), makes the folders with
/// <see cref="Directory.CreateDirectory(string)"/> and gives each a last-write time relative to an EXPLICIT cutoff passed in — nothing here reads the clock,
/// and the assertion is the EXACT set that remains, never "contains". No collection: the function touches no process static (its throttle is
/// outside it).</para>
/// </remarks>
public sealed class PruneOldScratchDirsCharacterizationTests
{
    /// <summary>A fixed instant, so no case depends on the day it runs.</summary>
    private static readonly DateTime Cutoff = new(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Old = Cutoff.AddDays(-2);

    private static string Folder(string root, string name, DateTime lastWrite)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        Directory.SetLastWriteTimeUtc(dir, lastWrite);

        return dir;
    }

    private static string[] Remaining(string root) =>
        [.. Directory.EnumerateDirectories(root).Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal)];

    [Fact]
    public void TheDefaultPrefixes_TakeExactlyTheFourProductKinds()
    {
        using var root = TempDir.For("prune-characterization-");
        // coai-question-a holds no lock file: FolderLock.RemoveIfFree takes a free lock and removes (FolderLock.cs:19-20).
        foreach (var name in (string[])["coai-answers-a", "coai-repair-a", "coai-noworkspace-a", "coai-question-a", "coai-panel-a", "coai-chat-a", "coai-0.17.5", "other-a"])
        {
            Folder(root, name, Old);
        }

        PanelService.PruneOldScratchDirs(root, Cutoff);

        Remaining(root).Should().Equal(["coai-0.17.5", "coai-chat-a", "coai-panel-a", "other-a"]);
    }

    [Fact]
    public void AnExplicitPrefixList_TakesOnlyItsMatches()
    {
        using var root = TempDir.For("prune-characterization-");
        foreach (var name in (string[])["coai-panel-a", "coai-answers-a", "coai-ctx-a"])
        {
            Folder(root, name, Old);
        }

        PanelService.PruneOldScratchDirs(root, Cutoff, ["coai-panel-*", "coai-ctx-*"]);

        Remaining(root).Should().Equal(["coai-answers-a"]);
    }

    [Fact]
    public void TheExceptList_KeepsItsPrefixes_WhateverTheAge()
    {
        using var root = TempDir.For("prune-characterization-");
        Folder(root, "coai-chat-a", Cutoff.AddDays(-30));
        Folder(root, "coai-panel-a", Old);
        Folder(root, "coai-chatter-a", Old);

        PanelService.PruneOldScratchDirs(root, Cutoff, ["coai-*"], ["coai-chat-"]);

        // The prefix match is ordinal, so coai-chatter-a — which does not start with "coai-chat-" — goes.
        Remaining(root).Should().Equal(["coai-chat-a"]);
    }

    [Fact]
    public void AVersionCache_IsKeptUnderAnyPrefixList()
    {
        using var root = TempDir.For("prune-characterization-");
        foreach (var name in (string[])["coai-0.17.5", "coai-9", "coai-panel-a"])
        {
            Folder(root, name, Old);
        }

        PanelService.PruneOldScratchDirs(root, Cutoff, ["coai-*"]);

        Remaining(root).Should().Equal(["coai-0.17.5", "coai-9"]);
    }

    /// <remarks>
    /// The Windows file-tunnelling remark of the retired <c>TheSweepRemovesWhatIsOldAndLeavesWhatIsInUse</c> belongs here: the sweep reads the LAST WRITE,
    /// not the creation — Windows hands a recreated name its predecessor's creation time, so a directory made this minute under a name used yesterday
    /// would read as a day old. This case sets the last write directly, which is the stamp the function reads.
    /// </remarks>
    [Fact]
    public void TheCutoffIsExclusive_OlderGoes_AtTheCutoffAndYoungerStay()
    {
        using var root = TempDir.For("prune-characterization-");
        Folder(root, "coai-panel-older", Cutoff.AddMinutes(-1));
        Folder(root, "coai-panel-at", Cutoff);
        Folder(root, "coai-panel-younger", Cutoff.AddMinutes(1));

        PanelService.PruneOldScratchDirs(root, Cutoff, ["coai-panel-*"]);

        Remaining(root).Should().Equal(["coai-panel-at", "coai-panel-younger"]);
    }

    [Fact]
    public void AReadOnlyFileInside_DoesNotKeepTheFolder()
    {
        using var root = TempDir.For("prune-characterization-");
        var scratch = Path.Combine(root, "coai-panel-a");
        var objects = Directory.CreateDirectory(Path.Combine(scratch, ".git", "objects", "e9")).FullName;
        var blob = Path.Combine(objects, "bb8d72");
        File.WriteAllText(blob, "an object git would never let you write twice");
        File.SetAttributes(blob, FileAttributes.ReadOnly);
        Directory.SetLastWriteTimeUtc(scratch, Old);

        PanelService.PruneOldScratchDirs(root, Cutoff, ["coai-panel-*"]);

        Remaining(root).Should().BeEmpty();
    }

    [Fact]
    public void AMissingRoot_IsNotAFailure()
    {
        using var root = TempDir.For("prune-characterization-");
        var missing = Path.Combine(root, "gone");

        var act = () => PanelService.PruneOldScratchDirs(missing, Cutoff);

        act.Should().NotThrow();
        Remaining(root).Should().BeEmpty();
    }
}
