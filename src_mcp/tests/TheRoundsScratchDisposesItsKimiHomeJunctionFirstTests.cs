using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A round's scratch holding a Kimi reviewer's home is disposed JUNCTION-FIRST (S4.15 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, the plan round's
/// judge's edit): the round KNOWS it holds Kimi homes, so D19's order is done explicitly by <see cref="KimiHome.DisposeAllUnder"/> before the shared
/// deleter — which is the second belt and meets no link any more. The person's folder keeps every file, bytes and read-only bit.
/// </summary>
/// <remarks>The scratch lives in this test's own <c>testsToDelete-</c> folder (<c>RoundScratch.At</c>); the person is a fake one in another.</remarks>
public sealed class TheRoundsScratchDisposesItsKimiHomeJunctionFirstTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _dir = TempDir.For("testsToDelete-kimi-roundscratch-");
    private readonly List<string> _notes = [];

    public void Dispose()
    {
        _dir.Dispose();
        _person.Dispose();
    }

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "Kimi runs on Windows hosts only (D18)");

    /// <summary>A scratch holding <c>kh-0123456789</c> with a junction, a <c>sessions/</c> folder with a file, and a <c>vh-</c> folder.</summary>
    private (RoundScratch Scratch, KimiPlaces Places) ScratchWithAKimiHome()
    {
        var scratch = RoundScratch.At(_dir.At("round"), _notes.Add);
        var places = _person.PlacesAt(Path.Combine(scratch.Directory, "kh-0123456789"));
        KimiHome.Made(HostKind.Windows, places, _person.ExpectedConfig()).Should().BeEmpty();
        Directory.CreateDirectory(Path.Combine(places.Home, "sessions"));
        File.WriteAllText(Path.Combine(places.Home, "sessions", "index.jsonl"), "x");
        Directory.CreateDirectory(Path.Combine(scratch.Directory, "vh-aaaaaaaaaa"));
        File.WriteAllText(Path.Combine(scratch.Directory, "vh-aaaaaaaaaa", "vibehistory"), "x");

        return (scratch, places);
    }

    [Fact]
    public void TheScratch_IsGone_TheJunctionWentFirst_AndThePersonsFolderKeepsEveryFile()
    {
        OnWindows();
        var (scratch, _) = ScratchWithAKimiHome();
        var before = _person.Hash();
        var metALink = new List<string>();
        GitScratch.AfterUnlink = met => metALink.Add(met);
        try
        {
            scratch.Dispose();
        }
        finally
        {
            GitScratch.AfterUnlink = null;
        }

        Directory.Exists(scratch.Directory).Should().BeFalse("the whole scratch went with the round");
        metALink.Should().BeEmpty("the junction was removed by KimiHome.Dispose BEFORE the deleter, which therefore met no link");
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits");
        _notes.Should().BeEmpty();
    }

    [Fact]
    public void ALossOnTheUnlink_IsNoted_AndTheScratchIsKept_NotDeletedUnderAHomeThatIsNotKnownToBeSafe()
    {
        OnWindows();
        var (scratch, places) = ScratchWithAKimiHome();
        var lost = Path.Combine(_person.Credentials, "kimi-code.json");
        KimiHome.AfterUnlink = _ =>
        {
            File.SetAttributes(lost, FileAttributes.Normal);
            File.Delete(lost);
        };
        try
        {
            scratch.Dispose();
        }
        finally
        {
            KimiHome.AfterUnlink = null;
        }

        _notes.Should().ContainSingle().Which.Should().Contain("kimi-code.json").And.Contain("lost");
        File.Exists(places.Config).Should().BeTrue("a stopped disposal deletes nothing more: the scratch is kept for the sweep");
    }

    [Fact]
    public void AHomeUnderALiveTree_IsNeverUnlinked_AndTheScratchIsKept()
    {
        OnWindows();
        var (scratch, places) = ScratchWithAKimiHome();
        using var held = ((Hold.Taken)HeldFile.Take(Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName))).Stream;

        scratch.Dispose();

        _notes.Should().ContainSingle().Which.Should().Contain("still running");
        OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("the junction was not unlinked under a live Kimi: its next refresh would otherwise sign the person out");
        File.Exists(places.Config).Should().BeTrue();
    }
}
