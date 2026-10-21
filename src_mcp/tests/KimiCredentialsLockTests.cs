using System.Diagnostics;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The sign-in's two locks (E4.2 decisions 1, 9 and 10, S2.27–S2.28 and S2.41–S2.43 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>):
/// the server's GATE serialises launchers per sign-in, keyed by the folder's IDENTITY as the kernel names it and never by its
/// spelling; the shim's TREE HOLD outlives its opener while any process that inherited it lives.
/// </summary>
/// <remarks>
/// Every lock folder and every sign-in folder here is under this test's own <c>testsToDelete-kimilock-</c> folder, and the lock root is
/// overridden per async flow (<see cref="KimiCredentialsLock.RootFor"/>), never by assigning a static: the suite runs eight tests at once.
/// NO inheritable handle ever lives in the test host — S2.43 plays the shim as a child process.
/// </remarks>
public sealed class KimiCredentialsLockTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly TempDir _dir = TempDir.For("testsToDelete-kimilock-");

    private string Locks => _dir.At("locks");

    private string Persons => _dir.At("persons", "credentials");

    public KimiCredentialsLockTests() => Directory.CreateDirectory(Persons);

    public void Dispose() => _dir.Dispose();

    private IDisposable Rooted() => KimiCredentialsLock.RootFor(Locks);

    private T In<T>(Func<T> read)
    {
        using var rooted = Rooted();

        return read();
    }

    // ---------- S2.41 ----------

    public static TheoryData<string> Spellings => ["upper", "trailing", "slashes", "dotdot", "alias"];

    private async Task<string> Spelled(string row)
    {
        switch (row)
        {
            case "upper":
                return Persons.ToUpperInvariant();
            case "trailing":
                return Persons + Path.DirectorySeparatorChar;
            case "slashes":
                return Persons.Replace('\\', '/');
            case "dotdot":
                Directory.CreateDirectory(_dir.At("persons", "x"));
                return Path.Combine(_dir.At("persons", "x"), "..", "credentials");
            default:
                var alias = _dir.At("alias");
                await DirectoryLink.MakeAsync(new ProcessLauncher(), alias, _dir.At("persons"));
                return Path.Combine(alias, "credentials");
        }
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public async Task TheLockKey_IsTheFoldersIdentity_NotItsSpelling(string row)
    {
        Assert.SkipWhen(row == "upper" && !OperatingSystem.IsWindows(), "a case-sensitive file system keeps FOLDER and folder apart — they are two folders there");
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var spelled = await Spelled(row);

        KimiCredentialsLock.PathFor(spelled).Should().Be(KimiCredentialsLock.PathFor(Persons), $"{row}: one folder, one gate");
        KimiCredentialsLock.TreePathFor(spelled).Should().Be(KimiCredentialsLock.TreePathFor(Persons), $"{row}: one folder, one tree hold");
        KimiCredentialsLock.PathFor(Persons).Should().StartWith(Locks + Path.DirectorySeparatorChar).And.EndWith(".lock");
        Path.GetFileName(KimiCredentialsLock.PathFor(Persons)).Should().MatchRegex("^[0-9a-f]{16}\\.lock$");
        Path.GetFileName(KimiCredentialsLock.TreePathFor(Persons)).Should().MatchRegex("^[0-9a-f]{16}\\.tree\\.lock$");

        var ct = TestContext.Current.CancellationToken;
        var first = (Hold.Taken)await KimiCredentialsLock.TakeAsync(Persons, ct);
        var second = KimiCredentialsLock.TakeAsync(spelled, ct);
        (await Task.WhenAny(second, Task.Delay(1500, ct))).Should().NotBe(second, $"{row}: the second launch on the same folder waits");
        first.Stream.Dispose();
        var taken = await second.WaitAsync(Patience, ct);
        taken.Should().BeOfType<Hold.Taken>();
        ((Hold.Taken)taken).Stream.Dispose();
    }

    // ---------- S-K3 / S-K11 (DL7: the Linux key is the folder's identity — device and inode — never a spelling) ----------

    /// <summary><c>KeyFor</c> of <paramref name="path"/> in a CHILD whose working folder is <paramref name="cwd"/> — a <c>/proc/self/cwd</c> spelling can only be asked from inside the process whose cwd it is (the test host's is process-wide).</summary>
    private static async Task<string> KeyInAChildAsync(string cwd, string path)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "FakeCli");
        using var child = Process.Start(new ProcessStartInfo(exe, ["lock-key", path]) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var key = await child.StandardOutput.ReadToEndAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        await child.WaitForExitAsync(TestContext.Current.CancellationToken);
        child.ExitCode.Should().Be(0, await child.StandardError.ReadToEndAsync());

        return key.Trim();
    }

    [Fact]
    public async Task TwoSpellingsOfOneFolder_GiveOneKey_AndADeletedFolder_NoKey()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the Windows key is the final path of the opened handle (TheLockKey_IsTheFoldersIdentity_NotItsSpelling); this is the Linux one, with no stand-in");
        var real = Path.Combine(_dir.At("a"), "real");
        Directory.CreateDirectory(real);
        Directory.CreateSymbolicLink(_dir.At("b"), _dir.At("a"));
        var elsewhere = Directory.CreateDirectory(_dir.At("other", "real")).FullName;

        var key = KimiCredentialsLock.KeyFor(real);

        key.Should().NotBeEmpty("a folder that exists is named by its device and inode");
        KimiCredentialsLock.KeyFor(Path.Combine(_dir.At("b"), "real")).Should().Be(key, "two spellings of one sign-in folder gave two lock keys (a symlinked parent)");
        (await KeyInAChildAsync(_dir.At("a"), "/proc/self/cwd/real")).Should().Be(key, "two spellings of one sign-in folder gave two lock keys (/proc/self/cwd)");
        KimiCredentialsLock.KeyFor(elsewhere).Should().NotBe(key, "two folders are two keys");

        Directory.Delete(real);

        KimiCredentialsLock.KeyFor(real).Should().BeEmpty("a deleted folder has no key — never its spelling");
    }

    [Fact]
    public void AFileWhereTheFolderShouldBe_HasNoKey_OnLinux()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the Linux identity names a DIRECTORY only");
        var file = _dir.At("not-a-folder");
        File.WriteAllText(file, "x");

        KimiCredentialsLock.KeyFor(file).Should().BeEmpty("a file has an inode too — and must not be keyed as if it were the sign-in folder");
    }

    [Fact]
    public async Task ABindMountedSpelling_IsTheSameKey()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "a bind mount is Linux's");
        var real = Directory.CreateDirectory(Path.Combine(_dir.At("a"), "real")).FullName;
        var mount = Directory.CreateDirectory(_dir.At("mnt")).FullName;
        var ct = TestContext.Current.CancellationToken;
        if (await Sudo(ct, "true") != 0)
        {
            Assert.SkipWhen(Environment.GetEnvironmentVariable("CI") is null, "a bind mount needs passwordless sudo, which this machine has not — a silently skipped check is a deleted one, so CI fails instead");
            Assert.Fail("bind-mount test needs passwordless sudo on the runner");
        }

        (await Sudo(ct, "mount", "--bind", _dir.At("a"), mount)).Should().Be(0, "the bind mount was made");
        try
        {
            KimiCredentialsLock.KeyFor(real).Should().NotBeEmpty("the folder has a key at all — two empty keys are not 'the same key'");
            KimiCredentialsLock.KeyFor(Path.Combine(mount, "real")).Should().Be(KimiCredentialsLock.KeyFor(real), "a bind-mounted spelling of one sign-in folder gave a second lock key");
        }
        finally
        {
            (await Sudo(CancellationToken.None, "umount", mount)).Should().Be(0, "the bind mount was taken away again");
        }
    }

    private static async Task<int> Sudo(CancellationToken ct, params string[] command)
    {
        using var sudo = Process.Start(new ProcessStartInfo("sudo", ["-n", .. command]) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false })!;
        await Task.WhenAll(sudo.StandardOutput.ReadToEndAsync(ct), sudo.StandardError.ReadToEndAsync(ct));
        await sudo.WaitForExitAsync(ct);

        return sudo.ExitCode;
    }

    // ---------- S2.42 ----------

    [Fact]
    public async Task AFolderThatDoesNotExist_HasNoKey_AndTakesNoLock_WithNoSeam()
    {
        using var rooted = Rooted();
        var missing = _dir.At("persons", "never-made");

        KimiCredentialsLock.KeyFor(missing).Should().BeEmpty("a folder that does not exist has no handle to name");
        KimiCredentialsLock.PathFor(missing).Should().BeEmpty();
        KimiCredentialsLock.TreePathFor(missing).Should().BeEmpty();
        var hold = await KimiCredentialsLock.TakeAsync(missing, TestContext.Current.CancellationToken);

        hold.Should().BeOfType<Hold.Unusable>("a key through a spelling fallback would be Real's spelling of the missing path");
        ((Hold.Unusable)hold).Cause.Message.Should().Contain("named by its handle");
        Directory.Exists(Locks).Should().BeFalse("no file, not even the folder, was made under the lock root");
    }

    [Fact]
    public async Task AFolderTheKernelCannotNameForAnyReason_IsNeverKeyedBySpelling()
    {
        using var rooted = Rooted();
        KimiCredentialsLock.Final = _ => string.Empty;
        try
        {
            KimiCredentialsLock.KeyFor(Persons).Should().BeEmpty();
            KimiCredentialsLock.PathFor(Persons).Should().BeEmpty();
            (await KimiCredentialsLock.TakeAsync(Persons, TestContext.Current.CancellationToken)).Should().BeOfType<Hold.Unusable>();
            Directory.Exists(Locks).Should().BeFalse();
        }
        finally
        {
            KimiCredentialsLock.Final = null;
        }
    }

    // ---------- S2.27 / S2.28 ----------

    [Fact]
    public async Task TwoLaunchesOnOneSignIn_WaitOnEachOther_AndOnTwoSignInsDoNot()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var other = _dir.At("persons2", "credentials");
        Directory.CreateDirectory(other);
        var ct = TestContext.Current.CancellationToken;

        var first = (Hold.Taken)await KimiCredentialsLock.TakeAsync(Persons, ct);
        var elsewhere = (Hold.Taken)await KimiCredentialsLock.TakeAsync(other, ct);
        elsewhere.Stream.Dispose();
        var second = KimiCredentialsLock.TakeAsync(Persons, ct);

        (await Task.WhenAny(second, Task.Delay(1500, ct))).Should().NotBe(second, "the second launch on one sign-in waits for the first");
        first.Stream.Dispose();
        ((Hold.Taken)await second.WaitAsync(Patience, ct)).Stream.Dispose();
    }

    [Fact]
    public async Task TheDefaultRoot_IsOutsideTheHomesFoldersAndTemp_AndItsWaitIsCutByTheToken()
    {
        using var standIn = WindowsStandIn.Begin();
        var normalised = KimiCredentialsLock.Root.Replace('\\', '/');
        normalised.Should().EndWith("/coai-mcp/kimi-locks");
        ConsultantHomes.All.Should().OnlyContain(homes => !normalised.Contains("/" + homes + "/") && !normalised.EndsWith("/" + homes),
            "the D20 sweep walks every folder under a homes folder and would judge a locks folder a foreign conversation");
        KimiCredentialsLock.Root.Should().NotStartWith(RunTemp.Real, "no sweep ever takes it");

        using var rooted = Rooted();
        Directory.CreateDirectory(Locks);
        using var holder = ((Hold.Taken)HeldFile.Take(KimiCredentialsLock.PathFor(Persons))).Stream;
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var waited = Stopwatch.StartNew();

        var wait = async () => await KimiCredentialsLock.TakeAsync(Persons, cancelled.Token);

        await wait.Should().ThrowAsync<OperationCanceledException>();
        waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ARootThatIsAFile_IsUnusable()
    {
        using var standIn = WindowsStandIn.Begin();
        File.WriteAllText(Locks, "not a folder");
        using var rooted = Rooted();

        var hold = await KimiCredentialsLock.TakeAsync(Persons, TestContext.Current.CancellationToken);

        hold.Should().BeOfType<Hold.Unusable>();
    }

    // ---------- S2.43 ----------

    [Fact]
    public async Task TheTreeHold_OutlivesItsOpener_WhileAChildLives()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "the child names the sign-in folder with the product's own key, and macOS has no folder-identity rule (a Kimi launch is refused there, D18) — a test's stand-in cannot reach into the child");
        var home = _dir.At("owned", "kh-x");
        Directory.CreateDirectory(home);
        var tree = In(() => KimiCredentialsLock.TreePathFor(Persons));
        var homeHold = Path.Combine(home, KimiCredentialsLock.HomeHoldName);
        var (shim, grandchild) = await ShimChild.StartAsync("tree", Locks, Persons, home);
        try
        {
            await ShimChild.EndAsync(shim);

            (await ShimChild.StaysHeldAsync(tree)).Should().BeTrue("the sleeping grandchild inherited the sign-in hold, so it stays Held after the opener is gone");
            (await ShimChild.StaysHeldAsync(homeHold)).Should().BeTrue("and the per-home one");
            var second = await ShimChild.FailsAsync("tree", Locks, Persons, home);
            second.Exit.Should().Be(69, "a second shim on the same sign-in waits its second, then gives up");
            second.Error.Should().Contain("Busy");
            using (Rooted())
            {
                KimiCredentialsLock.HomeProbe(home).Should().Be(LockProbe.Held);
            }

            await ShimChild.EndAsync(grandchild);

            (await Polls.Until(() => HeldFile.Probe(tree) == LockProbe.Free && HeldFile.Probe(homeHold) == LockProbe.Free, Patience))
                .Should().BeTrue("both holds are free once the last process that inherited them is gone");
            // Re-taken with the NON-inheriting calls: the test host never holds an inheritable handle (the scan next door pins it).
            using var signIn = HeldFile.TakeWithin(tree, TimeSpan.FromSeconds(1)).Should().BeOfType<Hold.Taken>("the sign-in's tree file is free to take again").Subject.Stream;
            using var homeFile = HeldFile.Take(homeHold).Should().BeOfType<Hold.Taken>("and the home's").Subject.Stream;
        }
        finally
        {
            await ShimChild.EndAsync(grandchild);
            await ShimChild.EndAsync(shim);
        }
    }

    [Fact]
    public async Task AShimOnAFolderTheKernelCannotName_TakesNoHold_AndSaysSo()
    {
        var shim = await ShimChild.FailsAsync("tree", Locks, _dir.At("persons", "never-made"), _dir.At("owned", "kh-z"));

        shim.Exit.Should().Be(69);
        shim.Error.Should().Contain("Unusable").And.Contain("named by its handle");
        Directory.Exists(Locks).Should().BeFalse("no lock file was made on a spelling");
    }

    /// <summary>M1: a disposal's probe opens the per-home file exclusively for a moment; a shim that meets it waits it out and still takes the hold.</summary>
    [Fact]
    public async Task AShimThatMeetsADisposalsProbe_WaitsItOut_AndTakesItsHold()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "the child names the sign-in folder with the product's own key, and macOS has no folder-identity rule (a Kimi launch is refused there, D18) — a test's stand-in cannot reach into the child");
        var home = _dir.At("owned", "kh-p");
        Directory.CreateDirectory(home);
        var (shim, grandchild) = await ShimChild.StartAsync("tree-probed", Locks, Persons, home);
        try
        {
            (await ShimChild.StaysHeldAsync(Path.Combine(home, KimiCredentialsLock.HomeHoldName))).Should().BeTrue("the shim took the per-home hold once the probe let go");
        }
        finally
        {
            await ShimChild.EndAsync(grandchild);
            await ShimChild.EndAsync(shim);
        }
    }

    [Fact]
    public async Task TheGateAlone_DoesNotOutliveItsOpener_SoItIsNotWhatKeepsTheTreeSafe()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), "the child names the sign-in folder with the product's own key, and macOS has no folder-identity rule (a Kimi launch is refused there, D18) — a test's stand-in cannot reach into the child");
        var (shim, grandchild) = await ShimChild.StartAsync("gate", Locks, Persons, _dir.At("owned", "kh-y"));
        try
        {
            await ShimChild.EndAsync(shim);

            var gate = In(() => KimiCredentialsLock.PathFor(Persons));
            (await Polls.Until(() => HeldFile.Probe(gate) == LockProbe.Free, Patience))
                .Should().BeTrue("the gate is non-inheritable: a launcher that died frees it while its grandchild still lives");
            grandchild.HasExited.Should().BeFalse();
        }
        finally
        {
            await ShimChild.EndAsync(grandchild);
            await ShimChild.EndAsync(shim);
        }
    }
    // ---------- the tree hold's decisions, with the NON-inheriting take (no test holds an inheritable handle) ----------

    private static readonly TimeSpan Brief = TimeSpan.FromMilliseconds(80);

    private static Hold Take(string path, TimeSpan within) => HeldFile.TakeWithin(path, within);

    private string HomeRoot() => Directory.CreateDirectory(_dir.At("owned", "kh-tree")).FullName;

    [Fact]
    public void TheTreeHold_TakesTheSignInsFileAndTheHomesFile_AndDisposingReleasesBoth()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var home = HomeRoot();

        var holding = KimiCredentialsLock.TakeTreeThrough(Persons, home, Brief, Take);

        var taken = holding.Should().BeOfType<TreeHolding.Taken>().Subject;
        var signIn = KimiCredentialsLock.TreePathFor(Persons);
        var homes = Path.Combine(home, KimiCredentialsLock.HomeHoldName);
        HeldFile.Probe(signIn).Should().Be(LockProbe.Held, "the sign-in's tree file is held");
        KimiCredentialsLock.HomeProbe(home).Should().Be(LockProbe.Held, "a disposal asks the home's file, and the sign-in's folder is never opened");
        taken.Hold.Dispose();
        taken.Hold.Dispose();
        HeldFile.Probe(signIn).Should().Be(LockProbe.Free);
        HeldFile.Probe(homes).Should().Be(LockProbe.Free, "disposing is once, and releases both");
    }

    [Fact]
    public void ATreeHoldOnAFolderTheKernelCannotName_IsUnusable_AndMakesNothing()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();

        var holding = KimiCredentialsLock.TakeTreeThrough(_dir.At("persons", "never-made"), HomeRoot(), Brief, Take);

        holding.Should().BeOfType<TreeHolding.Unusable>().Which.Cause.Message.Should().Contain("named by its handle");
        Directory.Exists(Locks).Should().BeFalse("no lock file was made on a spelling");
    }

    [Fact]
    public void ATreeHoldWhoseLockFolderCannotBeMade_IsUnusable_NotAThrow()
    {
        using var standIn = WindowsStandIn.Begin();
        File.WriteAllText(Locks, "not a folder");
        using var rooted = Rooted();

        KimiCredentialsLock.TakeTreeThrough(Persons, HomeRoot(), Brief, Take).Should().BeOfType<TreeHolding.Unusable>();
    }

    [Fact]
    public void ATreeHoldOnASignInAnotherTreeHolds_IsBusy_AfterItsWait()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        Directory.CreateDirectory(Locks);
        using var other = HeldFile.TryHold(KimiCredentialsLock.TreePathFor(Persons));
        other.Should().NotBeNull("another tree holds this sign-in");

        KimiCredentialsLock.TakeTreeThrough(Persons, HomeRoot(), Brief, Take).Should().BeOfType<TreeHolding.Busy>();
    }

    [Fact]
    public void ATreeHoldWhoseHomeFileIsHeldElsewhere_IsUnusable_AndGivesTheSignInBack()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var home = HomeRoot();
        using var other = HeldFile.TryHold(Path.Combine(home, KimiCredentialsLock.HomeHoldName));
        other.Should().NotBeNull();

        var holding = KimiCredentialsLock.TakeTreeThrough(Persons, home, Brief, Take);

        holding.Should().BeOfType<TreeHolding.Unusable>().Which.Cause.Message.Should().Contain("held by another process");
        HeldFile.Probe(KimiCredentialsLock.TreePathFor(Persons)).Should().Be(LockProbe.Free, "the first file is released when the second cannot be had");
    }

    [Fact]
    public void ATreeHoldWhoseFileCannotBeOpened_IsUnusable_WhicheverFileItWas()
    {
        using var standIn = WindowsStandIn.Begin();
        using var rooted = Rooted();
        var home = HomeRoot();
        var broken = new IOException("no access to the lock");

        var firstFile = KimiCredentialsLock.TakeTreeThrough(Persons, home, Brief, (_, _) => new Hold.Unusable(broken));
        var secondFile = KimiCredentialsLock.TakeTreeThrough(Persons, home, Brief, (path, within) => path.EndsWith(KimiCredentialsLock.HomeHoldName, StringComparison.Ordinal) ? new Hold.Unusable(broken) : Take(path, within));

        firstFile.Should().BeOfType<TreeHolding.Unusable>().Which.Cause.Should().BeSameAs(broken);
        secondFile.Should().BeOfType<TreeHolding.Unusable>().Which.Cause.Should().BeSameAs(broken);
        HeldFile.Probe(KimiCredentialsLock.TreePathFor(Persons)).Should().Be(LockProbe.Free, "the sign-in's file taken first was given back");
    }
}
