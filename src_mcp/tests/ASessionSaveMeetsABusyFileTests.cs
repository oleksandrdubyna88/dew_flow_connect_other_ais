using CoaiMcp.Runners.Files;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// S26 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c> (E3 S5, C25): a session save that meets a busy
/// session file is retried and then fails loudly — and it never loses the previous session, because the save is ATOMIC (a temp
/// file beside the session, then the one shared, bounded move of <see cref="AtomicFile"/>) rather than an in-place
/// <c>File.WriteAllText</c>, which truncates the file the moment its open succeeds.
/// </summary>
/// <remarks>
/// <para>Measured 2026-10-20 (Windows 11 26300, .NET 10.0.12), 4 s of one in-place writer and one mover on one file: the in-place
/// writer was refused 273 times (<see cref="IOException"/>, HResult 0x80070020), the mover 337 times
/// (<see cref="UnauthorizedAccessException"/>, 0x80070005). The in-place write was the only operation that opened the session
/// file BY NAME for writing, and an I/O error after its truncating open loses the previous session — a retry only repeats that risk.</para>
/// <para>The busy file is held exactly as a reader holds it, and the write goes through <see cref="SessionStore.FileWriter"/> so the pause
/// between two tries is a seam and no arm sleeps. The fault arms run on every OS; the held-file arms are Windows' (the share-mode refusal).
/// Same collection as <see cref="SessionStoreConcurrencyTests"/>.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class ASessionSaveMeetsABusyFileTests : IDisposable
{
    private readonly TempDir _dir = TempDir.For("testsToDelete-session-busy-");

    public void Dispose() => _dir.Dispose();

    private string Repo => _dir.At("repo");

    private SessionStore Store(Action<string, string> writer) => new(_dir.Path) { FileWriter = writer };

    private static Action<string, string> Through(AtomicSeams seams) => (file, json) => AtomicFile.Write(file, json, AtomicFile.SharingRetry, seams);

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "the share-mode refusal is Windows'; .NET's Unix locks are advisory");

    private static FileStream Hold(string file) => new(file, FileMode.Open, FileAccess.Read, FileShare.Read);

    private int RoundsOf(SessionStore store) => store.Load(Repo, "main")!.Rounds.Count;

    private string[] Temps(SessionStore store) => [.. Directory.EnumerateFiles(Path.GetDirectoryName(store.FileFor(Repo, "main"))!, "*.tmp")];

    [Fact]
    public void ASaveThatMeetsAHeldSessionFile_IsRetried_AndLands()
    {
        OnWindows();
        var plain = new SessionStore(_dir.Path);
        plain.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 1));
        var holder = Hold(plain.FileFor(Repo, "main"));
        var pauses = 0;
        var store = Store(Through(new AtomicSeams
        {
            Pause = _ =>
            {
                pauses++;
                holder.Dispose();
            },
        }));

        var save = () => store.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 2));

        save.Should().NotThrow("the holder lets go after one pause, inside the window");
        RoundsOf(store).Should().Be(2, "the second session landed");
        pauses.Should().BeGreaterThanOrEqualTo(1, "the first try met the held file, so the save paused and tried again");
    }

    [Fact]
    public void ASessionFileHeldThroughEveryAttempt_FailsTheSaveNamingIt_AndKeepsTheOldSession()
    {
        OnWindows();
        var plain = new SessionStore(_dir.Path);
        plain.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 1));
        var file = plain.FileFor(Repo, "main");
        var before = File.ReadAllBytes(file);
        var holder = Hold(file);
        var clock = new MovableClock(DateTimeOffset.UnixEpoch);
        var store = Store(Through(new AtomicSeams { Clock = clock, Pause = _ => clock.Advance(TimeSpan.FromMilliseconds(100)) }));

        var save = () => store.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 2));

        save.Should().Throw<SessionStoreException>("a save that never lands is a real problem and must surface").Which.Message.Should().Contain("'main'");
        holder.Dispose();
        File.ReadAllBytes(file).Should().Equal(before, "the held-through failure left the previous session byte for byte");
        RoundsOf(store).Should().Be(1);
        Temps(store).Should().BeEmpty();
    }

    [Fact]
    public void AFailureWhileTheTempFileIsWritten_LeavesThePreviousSessionByteForByte()
    {
        var plain = new SessionStore(_dir.Path);
        plain.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 1));
        var file = plain.FileFor(Repo, "main");
        var before = File.ReadAllBytes(file);
        var store = Store(Through(new AtomicSeams
        {
            WriteText = (path, text) =>
            {
                File.WriteAllText(path, text[..(text.Length / 2)]);
                throw new IOException("There is not enough space on the disk.");
            },
        }));

        var save = () => store.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 2));

        save.Should().Throw<SessionStoreException>().Which.InnerException.Should().BeOfType<IOException>();
        File.ReadAllBytes(file).Should().Equal(before, "the half-written temp file never touched the session");
        Temps(store).Should().BeEmpty("the failed save removed its own temp file");
        AnotherSaveLandsAndIsLoaded(plain);
    }

    [Fact]
    public void AFailureAfterTheTempFileIsWritten_LeavesThePreviousSessionByteForByte()
    {
        var plain = new SessionStore(_dir.Path);
        plain.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 1));
        var file = plain.FileFor(Repo, "main");
        var before = File.ReadAllBytes(file);
        var store = Store(Through(new AtomicSeams { AfterTemp = _ => throw new IOException("The move was refused.") }));

        var save = () => store.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 2));

        save.Should().Throw<SessionStoreException>().Which.Message.Should().Contain("'main'");
        File.ReadAllBytes(file).Should().Equal(before, "a failure between the temp file and the move leaves the previous session whole");
        Temps(store).Should().BeEmpty("the failed save removed its own temp file");
        AnotherSaveLandsAndIsLoaded(plain);
    }

    private void AnotherSaveLandsAndIsLoaded(SessionStore plain)
    {
        plain.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 3));

        RoundsOf(plain).Should().Be(3, "the next load after a successful write sees it");
    }

    [Fact]
    public void TheSessionsTurnIsHeldFromTheTempFileToTheMove()
    {
        var seen = new List<bool>();
        var file = new SessionStore(_dir.Path).FileFor(Repo, "main");
        var store = Store(Through(new AtomicSeams
        {
            AfterTemp = _ =>
            {
                // Between the temp file and the move another writer asks for the turn: it must not get it.
                using var other = SessionTurn.Take(file);
                seen.Add(other.Held);
            },
        }));

        store.Save(SessionStoreConcurrencyTests.SessionFor(Repo, rounds: 1));

        seen.Should().Equal([false], "the writer holds the turn through the move, so a reader or a second writer waits for the whole file");
    }
}
