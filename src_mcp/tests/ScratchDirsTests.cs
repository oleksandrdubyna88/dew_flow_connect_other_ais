using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every temp directory a round takes is swept by a later round.
/// </summary>
/// <remarks>
/// <para>An audit of this machine once found 1384 answer directories, and that is what the sweep
/// was written for. It then knew ONE of the three prefixes a round creates: the repair launch has
/// always taken an empty directory per stage, and since the Fast/Full switch the REVIEW launch
/// takes one too — on the default path, so it is now every code round rather than an opt-in.</para>
/// <para>Three directories per round, one of them swept. This test is on the sweep rather than on
/// a round, because a round cannot be run without vendors and the leak is in the sweep.</para>
/// </remarks>
public class ScratchDirsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("coai-sweeptest-").FullName;

    [Fact]
    public void EveryKindOfScratchDirectoryIsSwept_NotOnlyTheAnswers()
    {
        var stale = new[] { "coai-answers-old", "coai-repair-old", "coai-noworkspace-old" }
            .Select(Create).ToArray();

        PanelService.PruneOldScratchDirs(_root, DateTime.UtcNow.AddMinutes(1));

        foreach (var dir in stale)
        {
            Directory.Exists(dir).Should().BeFalse($"{Path.GetFileName(dir)} is a round's leftover");
        }
    }

    [Fact]
    public void ADirectoryStillInUseByARunningRoundIsLeftAlone()
    {
        // The sweep runs on the way IN, while another server may be half-way through a round of
        // its own. Age is the whole guard: a directory younger than the cutoff belongs to somebody.
        var fresh = Create("coai-noworkspace-live");

        PanelService.PruneOldScratchDirs(_root, DateTime.UtcNow.AddHours(-6));

        Directory.Exists(fresh).Should().BeTrue();
    }

    [Fact]
    public void SomebodyElsesTempDirectoryIsNotOurs()
    {
        var theirs = Create("dotnet-something");

        PanelService.PruneOldScratchDirs(_root, DateTime.UtcNow.AddMinutes(1));

        Directory.Exists(theirs).Should().BeTrue();
    }

    private string Create(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
