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

    private static void OnWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "the lock key is the handle's final path, which is Windows-only (D18)");

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
        OnWindows();
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
        OnWindows();
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
        OnWindows();
        var normalised = KimiCredentialsLock.Root.Replace('\\', '/');
        normalised.Should().EndWith("/coai-mcp/kimi-locks");
        ConsultantHomes.All.Should().OnlyContain(homes => !normalised.Contains("/" + homes + "/") && !normalised.EndsWith("/" + homes),
            "the D20 sweep walks every folder under a homes folder and would judge a locks folder a foreign conversation");
        KimiCredentialsLock.Root.Should().NotStartWith(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), "no sweep ever takes it");

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
        OnWindows();
        File.WriteAllText(Locks, "not a folder");
        using var rooted = Rooted();

        var hold = await KimiCredentialsLock.TakeAsync(Persons, TestContext.Current.CancellationToken);

        hold.Should().BeOfType<Hold.Unusable>();
    }

    // ---------- S2.43 ----------

    /// <summary>The shim as a child: <c>held &lt;grandchild pid&gt;</c> on stdout once it has taken <paramref name="mode"/>'s lock.</summary>
    internal static async Task<(Process Shim, Process Grandchild)> StartShimAsync(string mode, string locks, string target, string home)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");
        var shim = Process.Start(new ProcessStartInfo(exe, ["tree-hold", mode, locks, target, home]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var line = await shim.StandardOutput.ReadLineAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        if (line is null)
        {
            await shim.WaitForExitAsync(TestContext.Current.CancellationToken);
            Assert.Fail($"the child ended (exit {shim.ExitCode}) without taking its lock: {await shim.StandardError.ReadToEndAsync()}");
        }

        line.Should().StartWith("held ", "the child takes its lock before it says so");

        return (shim, Process.GetProcessById(int.Parse(line![5..])));
    }

    /// <summary>The shim as a child that is expected NOT to take its lock: its exit code and what it said on stderr.</summary>
    private static async Task<(int Exit, string Error)> ShimThatFailsAsync(string mode, string locks, string target, string home)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");
        using var shim = Process.Start(new ProcessStartInfo(exe, ["tree-hold", mode, locks, target, home]) { RedirectStandardError = true, UseShellExecute = false })!;
        var error = await shim.StandardError.ReadToEndAsync().WaitAsync(Patience, TestContext.Current.CancellationToken);
        await shim.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (shim.ExitCode, error);
    }

    /// <summary>
    /// Whether the file is Held at EVERY probe for 1.5 s. Never one probe: handle rundown after a process exit takes a moment, so a
    /// single probe right after a kill proves nothing in either direction.
    /// </summary>
    private static async Task<bool> StaysHeldAsync(string path)
    {
        var until = DateTime.UtcNow.AddMilliseconds(1500);
        while (DateTime.UtcNow < until)
        {
            if (HeldFile.Probe(path) != LockProbe.Held)
            {
                return false;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return true;
    }

    internal static async Task EndAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill();
        }

        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TheTreeHold_OutlivesItsOpener_WhileAChildLives()
    {
        OnWindows();
        var home = _dir.At("owned", "kh-x");
        Directory.CreateDirectory(home);
        var tree = In(() => KimiCredentialsLock.TreePathFor(Persons));
        var homeHold = Path.Combine(home, KimiCredentialsLock.HomeHoldName);
        var (shim, grandchild) = await StartShimAsync("tree", Locks, Persons, home);
        try
        {
            await EndAsync(shim);

            (await StaysHeldAsync(tree)).Should().BeTrue("the sleeping grandchild inherited the sign-in hold, so it stays Held after the opener is gone");
            (await StaysHeldAsync(homeHold)).Should().BeTrue("and the per-home one");
            var second = await ShimThatFailsAsync("tree", Locks, Persons, home);
            second.Exit.Should().Be(69, "a second shim on the same sign-in waits its second, then gives up");
            second.Error.Should().Contain("Busy");
            using (Rooted())
            {
                KimiCredentialsLock.HomeProbe(home).Should().Be(LockProbe.Held);
            }

            await EndAsync(grandchild);

            (await Polls.Until(() => HeldFile.Probe(tree) == LockProbe.Free && HeldFile.Probe(homeHold) == LockProbe.Free, Patience))
                .Should().BeTrue("both holds are free once the last process that inherited them is gone");
            // Re-taken with the NON-inheriting calls: the test host never holds an inheritable handle (the scan next door pins it).
            using var signIn = HeldFile.TakeWithin(tree, TimeSpan.FromSeconds(1)).Should().BeOfType<Hold.Taken>("the sign-in's tree file is free to take again").Subject.Stream;
            using var homeFile = HeldFile.Take(homeHold).Should().BeOfType<Hold.Taken>("and the home's").Subject.Stream;
        }
        finally
        {
            await EndAsync(grandchild);
            await EndAsync(shim);
        }
    }

    [Fact]
    public async Task AShimOnAFolderTheKernelCannotName_TakesNoHold_AndSaysSo()
    {
        var shim = await ShimThatFailsAsync("tree", Locks, _dir.At("persons", "never-made"), _dir.At("owned", "kh-z"));

        shim.Exit.Should().Be(69);
        shim.Error.Should().Contain("Unusable").And.Contain("named by its handle");
        Directory.Exists(Locks).Should().BeFalse("no lock file was made on a spelling");
    }

    /// <summary>M1: a disposal's probe opens the per-home file exclusively for a moment; a shim that meets it waits it out and still takes the hold.</summary>
    [Fact]
    public async Task AShimThatMeetsADisposalsProbe_WaitsItOut_AndTakesItsHold()
    {
        OnWindows();
        var home = _dir.At("owned", "kh-p");
        Directory.CreateDirectory(home);
        var (shim, grandchild) = await StartShimAsync("tree-probed", Locks, Persons, home);
        try
        {
            (await StaysHeldAsync(Path.Combine(home, KimiCredentialsLock.HomeHoldName))).Should().BeTrue("the shim took the per-home hold once the probe let go");
        }
        finally
        {
            await EndAsync(grandchild);
            await EndAsync(shim);
        }
    }

    [Fact]
    public async Task TheGateAlone_DoesNotOutliveItsOpener_SoItIsNotWhatKeepsTheTreeSafe()
    {
        OnWindows();
        var (shim, grandchild) = await StartShimAsync("gate", Locks, Persons, _dir.At("owned", "kh-y"));
        try
        {
            await EndAsync(shim);

            var gate = In(() => KimiCredentialsLock.PathFor(Persons));
            (await Polls.Until(() => HeldFile.Probe(gate) == LockProbe.Free, Patience))
                .Should().BeTrue("the gate is non-inheritable: a launcher that died frees it while its grandchild still lives");
            grandchild.HasExited.Should().BeFalse();
        }
        finally
        {
            await EndAsync(grandchild);
            await EndAsync(shim);
        }
    }
}
