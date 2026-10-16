using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Vibe;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Every ONE-SHOT Kimi home — a question row's, a consultant check's — is disposed junction-first by its owner, by the one-shot sweep
/// after a crash, and by the root-less fallbacks (E5.3 decision g and judgement R4–R6 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>;
/// S5.3.7–S5.3.10, S5.3.20): <c>KimiHome.DisposeAllUnder</c> runs UNDER the folder's lock, a <c>Stopped</c> keeps the folder and its lock
/// file, every other entry goes under the lock, and the lock file and the folder go last, after the release.
/// </summary>
/// <remarks>
/// Every junction points at a fake person's folder inside this test's own <c>testsToDelete-kimi-</c> fixture, which lives OUTSIDE the
/// one-shot root being deleted (a past CI failure: a person's folder inside the swept tree is deleted by enumeration order). The
/// tree hold is a plain non-inheriting <c>HeldFile.Take</c> in the test host; no test ever runs a real <c>kimi</c>.
/// </remarks>
public sealed class AOneShotKimiHomeIsDisposedJunctionFirstTests : IDisposable
{
    private readonly KimiFixture _person = new();
    private readonly TempDir _root = TempDir.For("testsToDelete-kimi-oneshot-");
    private readonly List<string> _warned = [];
    private readonly List<string> _unlinkedByTheDeleter = [];

    public void Dispose()
    {
        GitScratch.AfterUnlink = null;
        KimiHome.AfterUnlink = null;
        _root.Dispose();
        _person.Dispose();
    }

    /// <summary>The deleter's own unlink recorder — a Kimi home disposed junction-first never lets the deleter meet its junction.</summary>
    private void RecordingTheDeleter() => GitScratch.AfterUnlink = _unlinkedByTheDeleter.Add;

    /// <summary>A made Kimi home <c>kh-x</c> inside <paramref name="folder"/>, with a session file Kimi would write.</summary>
    private KimiPlaces MadeHomeIn(string folder)
    {
        var places = _person.PlacesAt(Path.Combine(folder, "kh-x"));
        KimiHome.Made(HostKind.Windows, places, _person.ExpectedConfig()).Should().BeEmpty();
        Directory.CreateDirectory(Path.Combine(places.Home, "sessions"));
        File.WriteAllText(Path.Combine(places.Home, "sessions", "index.jsonl"), "x");

        return places;
    }

    private OneShotHome Reserved() =>
        VibeHomes.ReserveOneShot(_root, _warned.Add).Should().BeOfType<OneShotReservation.Reserved>().Subject.Home;

    private void ThePersonsFolderIsWhole(IReadOnlyList<string> before) =>
        _person.Hash().Should().Equal(before, "the person's folder: the same names, bytes and read-only bits as before");

    private void TheDeleterNeverMetTheJunction() =>
        _unlinkedByTheDeleter.Should().BeEmpty("the deleter's unlink recorder must not be invoked, because KimiHome unlinks the junction first");

    // ---------- S5.3.7 ----------

    [Fact]
    public void AOneShotHome_IsDisposedJunctionFirst_UnderItsLock()
    {
        using var standIn = WindowsStandIn.Begin();
        var home = Reserved();
        MadeHomeIn(home.Folder);
        var before = _person.Hash();
        RecordingTheDeleter();

        home.Dispose();

        Directory.Exists(home.Folder).Should().BeFalse("the owner takes its folder whole when it ends");
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.3.8 ----------

    [Fact]
    public void AOneShotHomeUnderALiveTree_IsKept_AndTheSweepTakesItLater()
    {
        using var standIn = WindowsStandIn.Begin();
        var home = Reserved();
        var places = MadeHomeIn(home.Folder);
        var before = _person.Hash();
        RecordingTheDeleter();
        var tree = ((Hold.Taken)HeldFile.Take(Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName))).Stream;
        try
        {
            home.Dispose();

            Directory.Exists(home.Folder).Should().BeTrue("a live tree keeps its home");
            OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("the junction is still a link: unlinking it under a live kimi would sign the person out");
            File.Exists(Path.Combine(home.Folder, VibeHomes.LockName)).Should().BeTrue("a kept folder keeps its lock file (R4)");
            _warned.Where(said => !said.StartsWith("made ", StringComparison.Ordinal)).Should().ContainSingle()
                .Which.Should().Contain("kept").And.Contain("still running").And.Contain(home.Folder);
            HeldFile.IsHeld(Path.Combine(home.Folder, VibeHomes.LockName)).Should().BeFalse("the owner gave the lock back when it kept the folder");
        }
        finally
        {
            tree.Dispose();
        }

        _warned.Clear();

        VibeHomes.SweepOneShot(_root, DateTime.UtcNow + TimeSpan.FromDays(1), _warned.Add).Should().Be(1, "the tree ended, so the sweep finishes the folder");

        Directory.Exists(home.Folder).Should().BeFalse();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.3.9 ----------

    [Fact]
    public void SweepOneShot_StopsOnAKimiStop_AndSaysWhy()
    {
        using var standIn = WindowsStandIn.Begin();
        var folder = Directory.CreateDirectory(VibeHomes.OneShotIn(_root, "0ddba11c")).FullName;
        var places = MadeHomeIn(folder);
        var lost = Path.Combine(_person.Credentials, "kimi-code.json");
        var cutoff = DateTime.UtcNow + TimeSpan.FromDays(1);
        KimiHome.AfterUnlink = _ =>
        {
            File.SetAttributes(lost, FileAttributes.Normal);
            File.Delete(lost);
        };
        try
        {
            VibeHomes.SweepOneShot(_root, cutoff, _warned.Add).Should().Be(0, "a Stopped keeps the folder this pass");
        }
        finally
        {
            KimiHome.AfterUnlink = null;
        }

        Directory.Exists(folder).Should().BeTrue("nothing more was deleted after the stop");
        File.Exists(places.Config).Should().BeTrue();
        File.Exists(Path.Combine(places.Home, "sessions", "index.jsonl")).Should().BeTrue();
        _warned.Where(said => !said.StartsWith("made ", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Should().Contain("kimi-code.json").And.Contain(_person.Credentials, "the warn carries the Stopped sentence, unparaphrased").And.Contain(folder);

        VibeHomes.SweepOneShot(_root, cutoff, _warned.Add).Should().Be(1, "the link is gone already, so the next pass finishes");
        Directory.Exists(folder).Should().BeFalse();
    }

    // ---------- S5.3.10 ----------

    [Theory]
    [InlineData("question row")]
    [InlineData("consultant check")]
    public void TheRootlessFallback_DisposesJunctionFirst(string site)
    {
        using var standIn = WindowsStandIn.Begin();
        var scratch = _root.At(site == "question row" ? "coai-question-0123abcd" : "coai-check-0123abcd");
        var handed = site == "question row" ? scratch : Path.Combine(scratch, ConsultantCheck.OwnName);
        Directory.CreateDirectory(handed);
        File.WriteAllText(Path.Combine(scratch, "notes.txt"), "the row's own file");
        MadeHomeIn(handed);
        var before = _person.Hash();
        RecordingTheDeleter();

        if (site == "question row")
        {
            QuestionRowLaunch.RemoveScratch(scratch, Serilog.Core.Logger.None);
        }
        else
        {
            ConsultCheckScratch.Delete(scratch, _warned.Add);
        }

        Directory.Exists(scratch).Should().BeFalse($"the {site}'s scratch goes whole");
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
        _warned.Should().BeEmpty();
    }

    [Theory]
    [InlineData("question row")]
    [InlineData("consultant check")]
    public void TheRootlessFallback_KeepsTheScratchUnderALiveTree(string site)
    {
        using var standIn = WindowsStandIn.Begin();
        var scratch = _root.At(site == "question row" ? "coai-question-0123abcd" : "coai-check-0123abcd");
        var handed = site == "question row" ? scratch : Path.Combine(scratch, ConsultantCheck.OwnName);
        Directory.CreateDirectory(handed);
        var places = MadeHomeIn(handed);
        var sink = new ListSink();
        var log = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(sink).CreateLogger();
        using var tree = ((Hold.Taken)HeldFile.Take(Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName))).Stream;

        if (site == "question row")
        {
            QuestionRowLaunch.RemoveScratch(scratch, log);
        }
        else
        {
            ConsultCheckScratch.Delete(scratch, _warned.Add);
        }

        OwnedPaths.IsLinkIfThere(places.Link).Should().BeTrue("the junction is never unlinked under a live tree; the scratch sweep finishes it once the tree has ended");
        File.Exists(places.Config).Should().BeTrue("nothing of the scratch was deleted");
        (site == "question row" ? sink.Lines : _warned).Should().ContainSingle().Which.Should().Contain("still running").And.Contain(places.OwnedRoot);
    }

    // ---------- S5.3.20 ----------

    [Fact]
    public void AOneShotOwner_DeletesEveryEntryUnderItsLock_TheLockFileAndTheFolderLast()
    {
        using var standIn = WindowsStandIn.Begin();
        var folder = Directory.CreateDirectory(VibeHomes.OneShotIn(_root, "c0ffee00")).FullName;
        MadeHomeIn(folder);
        Directory.CreateDirectory(Path.Combine(folder, "vh-aaaaaa", "logs"));
        File.WriteAllText(Path.Combine(folder, "vh-aaaaaa", "logs", "x"), "x");
        File.WriteAllText(Path.Combine(folder, "export.md"), "a launch's export beside the home");
        var lockPath = Path.Combine(folder, VibeHomes.LockName);
        // The owner as the reservation makes it: the folder's lock held for its life (the pin is nothing here — no coai-vh handle is needed to prove the order).
        var locked = ((Hold.Taken)HeldFile.Take(lockPath)).Stream;
        var entries = Directory.EnumerateFileSystemEntries(folder).Where(entry => !entry.Equals(lockPath, StringComparison.OrdinalIgnoreCase)).ToList();
        var deleted = new List<(string Entry, LockProbe Lock)>();
        var home = new OneShotHome(folder, locked, _warned.Add)
        {
            Io = HomeIo.Real with
            {
                Delete = entry =>
                {
                    deleted.Add((entry, HeldFile.Probe(lockPath)));
                    GitScratch.DeleteEntry(entry);
                },
            },
        };
        var before = _person.Hash();
        RecordingTheDeleter();

        home.Dispose();

        entries.Should().HaveCount(3, "kh-x, vh-aaaaaa and export.md beside the lock file");
        foreach (var entry in entries)
        {
            var went = deleted.Where(one => one.Entry.Equals(entry, StringComparison.OrdinalIgnoreCase)).ToList();
            went.Should().ContainSingle($"every entry of {folder} is deleted by name while its lock is still held, but \"{Path.GetFileName(entry)}\" was never deleted by name (the folder went whole after the lock was released)");
            went[0].Lock.Should().Be(LockProbe.Held, $"every entry of {folder} is deleted while its lock is still held, but \"{Path.GetFileName(entry)}\" was deleted after the lock was released");
        }

        deleted.Last().Entry.Should().Be(folder, "the lock file and the folder go last, together");
        deleted.Last().Lock.Should().Be(LockProbe.Free, "after the release");
        Directory.Exists(folder).Should().BeFalse();
        TheDeleterNeverMetTheJunction();
        ThePersonsFolderIsWhole(before);
    }

    // ---------- S5.3.14, this story's collectors ----------

    /// <summary>
    /// The sentences this story ADDS — the owner's kept-home warn, the one-shot sweep's warn, the resolved-ancestor refusal — carry paths and
    /// entry names, never a byte of the person's sign-in (judgement R11). The service-level two-turn arm waits for E5.4's <c>KimiConsultant</c>.
    /// </summary>
    [Fact]
    public void NothingOfTheSignIn_AppearsInWhatThisStoryWrites()
    {
        using var standIn = WindowsStandIn.Begin();
        var home = Reserved();
        var places = MadeHomeIn(home.Folder);
        using (var tree = ((Hold.Taken)HeldFile.Take(Path.Combine(places.OwnedRoot, KimiCredentialsLock.HomeHoldName))).Stream)
        {
            home.Dispose();
        }

        var lost = Path.Combine(_person.Credentials, "kimi-code.json");
        KimiHome.AfterUnlink = _ =>
        {
            File.SetAttributes(lost, FileAttributes.Normal);
            File.Delete(lost);
        };
        try
        {
            VibeHomes.SweepOneShot(_root, DateTime.UtcNow + TimeSpan.FromDays(1), _warned.Add);
        }
        finally
        {
            KimiHome.AfterUnlink = null;
        }

        _warned.Add(Junctions.ResolvedRefusal(_person.Credentials));

        _warned.Should().NotBeEmpty("the collectors saw the owner's warn and the sweep's");
        _warned.Should().NotContain(said => said.Contains(KimiFixture.RefreshCanary, StringComparison.Ordinal) || said.Contains(KimiFixture.ApiKeyCanary, StringComparison.Ordinal));
        File.ReadAllText(Path.Combine(_person.Credentials, "kimi-code-env-x.json")).Should().Contain(KimiFixture.RefreshCanary, "the control: the canary is served where the detector looks");
    }
}
