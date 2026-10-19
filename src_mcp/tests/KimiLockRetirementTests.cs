using System.Runtime.Versioning;
using System.Text.Json;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// DL7's lock-file retirement (F15, S-K19 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>): an identity key changes whenever the sign-in folder is made again, so on Linux the
/// <c>&lt;key&gt;.lock</c> and <c>&lt;key&gt;.tree.lock</c> files would pile up. The gate records, once, the identity it was keyed by and the folder spelling; a sweep removes a key's files when
/// it can take BOTH at once without waiting AND the folder is gone or is another folder. Removing a lock file under a lock is the classic two-holders race, so every taker verifies
/// after its take that the path still names the file it locked (<see cref="HeldFile.TakeVerified(string, TimeSpan, bool)"/>). Linux only: Windows keys are path-stable and a file held with
/// <see cref="FileShare.None"/> cannot be deleted, so the sweep is inert there.
/// </summary>
public sealed class KimiLockRetirementTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimiretire-");

    private readonly List<string> _notes = [];

    private string Locks => _dir.At("locks");

    private string Persons => _dir.At("persons", "credentials");

    public KimiLockRetirementTests() => Directory.CreateDirectory(Persons);

    public void Dispose() => _dir.Dispose();

    /// <summary>The sign-in's gate taken once by the product and given back: the record is written, the key's file exists.</summary>
    private async Task<string> GateRecordedAsync(string folder)
    {
        using var rooted = KimiCredentialsLock.RootFor(Locks);
        var hold = (Hold.Taken)await KimiCredentialsLock.TakeAsync(folder, TestContext.Current.CancellationToken);
        var gate = KimiCredentialsLock.PathFor(folder);
        hold.Stream.Dispose();

        return gate;
    }

    private int Swept() => KimiLockRetirement.Sweep(Locks, _notes.Add);

    // ---------- the record ----------

    [Fact]
    public async Task TheGate_WritesItsRecordOnTheFirstTake_AndNeverAgain()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the identity text of a folder is Linux's (device:inode); the record exists on Windows too, but its identity is the final path there");
        var gate = await GateRecordedAsync(Persons);
        var first = File.ReadAllText(gate);

        var record = JsonSerializer.Deserialize(first, KimiLockJsonContext.Default.KimiLockRecord)!;
        record.Folder.Should().Be(Persons, "the spelling the launch used");
        record.Identity.Should().MatchRegex("^[0-9]+:[0-9]+:[0-9]+$").And.Be(FolderIdentity.Of(Persons), "the identity the key was made from");
        record.WrittenUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        await Task.Delay(30, TestContext.Current.CancellationToken);
        await GateRecordedAsync(Persons);

        File.ReadAllText(gate).Should().Be(first, "the record is written once, under the first take, and the file is reused as it is");
    }

    // ---------- S-K19a / S-K19b ----------

    [Fact]
    public async Task ALockOfAVanishedFolder_IsRetired_WhenNobodyHoldsIt()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's: a Windows key is path-stable and a held file cannot be deleted");
        var gate = await GateRecordedAsync(Persons);
        File.WriteAllText(gate.Replace(".lock", ".tree.lock"), string.Empty);
        Directory.Delete(Persons);

        var retired = Swept();

        File.Exists(gate).Should().BeFalse("a lock file of a vanished sign-in folder outlived the sweep");
        File.Exists(gate.Replace(".lock", ".tree.lock")).Should().BeFalse("its tree file goes with it");
        retired.Should().Be(1);
        _notes.Should().Contain(note => note.Contains("retired") && note.Contains("is gone"));
    }

    [Fact]
    public async Task ALockWhoseFolderIsAnotherFolderNow_IsRetired()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        var gate = await GateRecordedAsync(Persons);
        var replacement = Directory.CreateDirectory(_dir.At("persons", "replacement")).FullName;
        Directory.Move(Persons, _dir.At("persons", "old"));
        Directory.Move(replacement, Persons);

        var retired = Swept();

        File.Exists(gate).Should().BeFalse("the path names another folder now, so the old identity's lock can never be asked for again");
        retired.Should().Be(1);
        _notes.Should().Contain(note => note.Contains("has another identity now"));
    }

    [Fact]
    public async Task ALockOfAFolderThatIsStillThere_IsKept()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        var gate = await GateRecordedAsync(Persons);

        Swept().Should().Be(0);

        File.Exists(gate).Should().BeTrue("the folder it was keyed by is still that folder");
    }

    [Fact]
    public async Task ALock_IsLeft_WhileTheGateIsHeld()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        var gate = await GateRecordedAsync(Persons);
        Directory.Delete(Persons);
        using var held = ((Hold.Taken)HeldFile.Take(gate)).Stream;

        Swept().Should().Be(0);

        File.Exists(gate).Should().BeTrue("the sweep removed a lock file somebody held");
    }

    [Fact]
    public async Task ALock_IsLeft_WhileTheTreeFileIsHeld()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        var gate = await GateRecordedAsync(Persons);
        var tree = gate.Replace(".lock", ".tree.lock");
        using var held = ((Hold.Taken)HeldFile.Take(tree)).Stream;
        Directory.Delete(Persons);

        Swept().Should().Be(0);

        File.Exists(gate).Should().BeTrue("a held tree file keeps the whole key — the gate first of all");
        File.Exists(tree).Should().BeTrue();
    }

    [Fact]
    public async Task AGateHeldByAnotherProcess_IsLeft()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        var gate = await GateRecordedAsync(Persons);
        var (shim, grandchild) = await ShimChild.StartAsync("gate", Locks, Persons, _dir.At("owned", "kh-g"));
        try
        {
            Directory.Delete(Persons);

            Swept().Should().Be(0);

            File.Exists(gate).Should().BeTrue("the sweep removed a lock file another process held");
        }
        finally
        {
            await ShimChild.EndAsync(grandchild);
            await ShimChild.EndAsync(shim);
        }
    }

    [Fact]
    public void ALockWithNoReadableRecord_IsLeft_AndSaid()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        Directory.CreateDirectory(Locks);
        var bare = Path.Combine(Locks, "0123456789abcdef.lock");
        var garbled = Path.Combine(Locks, "fedcba9876543210.lock");
        File.WriteAllText(bare, string.Empty);
        File.WriteAllText(garbled, "{ not a record");

        Swept().Should().Be(0, "D20's rule: a file the sweep cannot decide about is left, never the reason the sweep fails");

        File.Exists(bare).Should().BeTrue();
        File.Exists(garbled).Should().BeTrue();
        _notes.Should().Contain(note => note.Contains("0123456789abcdef.lock") && note.Contains("no readable record"));
        _notes.Should().Contain(note => note.Contains("fedcba9876543210.lock") && note.Contains("no readable record"));
    }

    [Fact]
    public void AnOversizedRecord_IsNotRead_AndTheLockIsLeft_AndSaid()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        Directory.CreateDirectory(Locks);
        var big = Path.Combine(Locks, "0123456789abcdef.lock");
        var record = JsonSerializer.Serialize(new KimiLockRecord("1:1:1", Path.Combine(_dir.Path, "a-folder-that-is-gone"), DateTimeOffset.UtcNow), KimiLockJsonContext.Default.KimiLockRecord);
        File.WriteAllText(big, record + new string(' ', BoundedFile.MaxBytes));

        Swept().Should().Be(0, "a record larger than 64 KiB is not read: its first bytes would have said 'retire me'");

        File.Exists(big).Should().BeTrue();
        _notes.Should().Contain(note => note.Contains("0123456789abcdef.lock") && note.Contains("no readable record"));
    }

    [Fact]
    public void AFileThatIsNotAKeysLock_IsNeverTouched_AndAMissingRootIsNothingToDo()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the sweep is Linux's");
        Swept().Should().Be(0, "no lock folder yet");
        Directory.CreateDirectory(Locks);
        var other = Path.Combine(Locks, "notes.lock");
        File.WriteAllText(other, "x");

        Swept().Should().Be(0);

        File.Exists(other).Should().BeTrue("only <16 hex>.lock files are keys");
    }

    // ---------- S-K19d ----------

    [Fact]
    public async Task OnWindows_TheSweepIsInert()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the platform map: Windows keys are path-stable and a held file cannot be unlinked there");
        using var standIn = WindowsStandIn.Begin();
        var gate = await GateRecordedAsync(Persons);
        Directory.Delete(Persons);

        Swept().Should().Be(0);

        File.Exists(gate).Should().BeTrue("nothing is retired on Windows");
        _notes.Should().BeEmpty();
    }

    // ---------- S-K19c: the two-holders guard ----------

    [Fact]
    [SupportedOSPlatform("linux")]
    public void ATakerWhoseFileWasUnlinkedUnderIt_RetakesTheNewFile()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the guard is Linux's: a Windows file held open cannot be unlinked");
        Directory.CreateDirectory(Locks);
        var path = Path.Combine(Locks, "0123456789abcdef.lock");
        var unlinks = 0;

        var hold = HeldFile.TakeVerified(path, Patience, false, afterOpen: file =>
        {
            if (unlinks++ == 0)
            {
                File.Delete(file);
            }
        });

        using var stream = hold.Should().BeOfType<Hold.Taken>().Subject.Stream;
        var held = LinuxStatx.OfHandle(stream.SafeFileHandle, LinuxStatx.StatxBasic).Should().BeOfType<StatxRead.Read>().Subject.Status;
        var named = LinuxStatx.OfPath(path, LinuxStatx.AtSymlinkNoFollow, LinuxStatx.StatxBasic).Should().BeOfType<StatxRead.Read>("the path names a file again").Subject.Status;
        held.SameFileAs(named).Should().BeTrue("two holders of one sign-in lock after a sweep: the taker holds an inode nobody else will ever open");
        unlinks.Should().Be(2, "one mismatch, one retake");
    }

    [Fact]
    public void ATakerWhoseFileIsReplacedThreeTimesInARow_IsRefused()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the guard is Linux's");
        Directory.CreateDirectory(Locks);
        var path = Path.Combine(Locks, "0123456789abcdef.lock");

        var hold = HeldFile.TakeVerified(path, Patience, false, afterOpen: File.Delete);

        hold.Should().BeOfType<Hold.Unusable>().Which.Cause.Message.Should().Contain("was replaced under its holder three times in a row — refused");
    }

    [Fact]
    public void ABusyOrUnusableFile_IsReturnedAsItIs()
    {
        Directory.CreateDirectory(Locks);
        var path = Path.Combine(Locks, "0123456789abcdef.lock");
        using var other = ((Hold.Taken)HeldFile.Take(path)).Stream;

        HeldFile.TakeVerified(path, TimeSpan.FromMilliseconds(50)).Should().BeOfType<Hold.Busy>();
        HeldFile.TakeVerified(Locks, TimeSpan.FromMilliseconds(50)).Should().BeOfType<Hold.Unusable>("a folder where a file should be");
    }

    [Fact]
    public void AFileNobodyTouches_IsTakenAsOrdinary_OnEveryHost()
    {
        Directory.CreateDirectory(Locks);
        var path = Path.Combine(Locks, "0123456789abcdef.lock");

        using var stream = HeldFile.TakeVerified(path, Patience).Should().BeOfType<Hold.Taken>().Subject.Stream;

        HeldFile.Probe(path).Should().Be(LockProbe.Held);
    }
}
