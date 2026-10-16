using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The six-hour scratch sweep (<c>PanelService.PruneOldScratchDirs</c>) is the backstop of a round's kept scratch and of a crashed server's leftovers, so it
/// must obey the same rule as the round itself (E4.4 review B1; E4.2 decision 11): a Kimi home whose per-home tree hold is HELD has a <c>kimi</c> still running on
/// it, and unlinking its <c>credentials</c> junction would make that process's next refresh sign the person out. A folder's last-write time says nothing about
/// it — writes deep inside a home never move the top folder's time — so the sweep asks the hold, junction-first, exactly as <c>ConsultantHomeSweep</c> does.
/// </summary>
/// <remarks>The hold is a plain non-inheritable <c>HeldFile.Take</c> in the test host: the probe only needs the file to be busy.</remarks>
public sealed class TheScratchSweepNeverDeletesAKimiHomeUnderALiveTreeTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _root = TempDir.For("testsToDelete-sweep-live-");

    public void Dispose()
    {
        _root.Dispose();
        _person.Dispose();
    }

    [Fact]
    public void AnOldScratchWhoseKimiHomeIsUnderALiveTree_IsKept_JunctionIntact_AndRemovedOnceTheTreeEnds()
    {
        using var standIn = WindowsStandIn.Begin();
        var scratch = Directory.CreateDirectory(_root.At("coai-answers-x")).FullName;
        var places = _person.PlacesAt(Path.Combine(scratch, "kh-0123456789"));
        KimiHome.Made(HostKind.Windows, places, _person.ExpectedConfig()).Should().BeEmpty();
        var before = _person.Hash();
        List<string> warned = [];
        var hold = ((Hold.Taken)HeldFile.Take(Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName))).Stream;
        try
        {
            Directory.SetLastWriteTimeUtc(scratch, DateTime.UtcNow.AddDays(-3));

            PanelService.PruneOldScratchDirs(_root, DateTime.UtcNow.AddDays(-1), ["coai-answers-*"], warn: warned.Add);

            Directory.Exists(scratch).Should().BeTrue("a kimi is still running on that home: the sweep leaves the whole folder for the next beat");
            OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("the junction was not unlinked under a live tree");
            File.Exists(places.Config).Should().BeTrue();
            warned.Should().ContainSingle().Which.Should().Contain("still running");
        }
        finally
        {
            hold.Dispose();
        }

        Directory.SetLastWriteTimeUtc(scratch, DateTime.UtcNow.AddDays(-3));
        warned.Clear();

        PanelService.PruneOldScratchDirs(_root, DateTime.UtcNow.AddDays(-1), ["coai-answers-*"], warn: warned.Add);

        Directory.Exists(scratch).Should().BeFalse("the tree ended: the next beat removes the folder, junction first");
        warned.Should().BeEmpty();
        _person.Hash().Should().Equal(before, "the person's folder keeps every file, bytes and read-only bit");
    }
}
