using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;
using static CoaiMcp.Tests.Leftovers.SweepPlants;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// Two sweeps at once, the quarantine name, a FIFO at the claim path, and the same verdicts under a root as given and as resolved (E5 S2-B, B8, B10, B11, B13, B13-W).
/// </summary>
/// <remarks>
/// Every private root is an owned scope's folder, never the machine's temp root; the plants and helpers are <see cref="SweepPlants"/>. Split from one 839-line file ([S2-B code1 1]); the spec's B-numbers are the same.
/// </remarks>
public sealed class StartOfRunSweepRaceAndRootTests : IDisposable
{
    /// <summary>On macOS every test here stands on the identity read, so the layout self-test must hold first.</summary>
    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    // ---- B8 -------------------------------------------------------------------------------------------------------

    [Fact]
    public void TwoSweepsAtOnce_NeverFail_AndNeverRemoveEachOthersWork()
    {
        var dead = Dead();

        // (a) another sweep got there first: it removed the folder and its claim between this sweep's verdict and its removal.
        var rootA = _owned.Dir("b8-a");
        var gone = PlantFolder(rootA, Name("raced"), Rec(dead));
        var a = LeftoversSweep.Run(Options(rootA, hooks: new SweepHooks(AfterVerdict: dir =>
        {
            Directory.Delete(dir, true);
            File.Delete(dir + ".owner");
        })));

        a.Errors.Should().BeEmpty("a folder the other sweep removed is no error");
        a.Removed.Should().NotContain(gone, "this sweep did not remove it");
        a.Preserved.Count(p => p.Path == gone).Should().BeLessThanOrEqualTo(1);
        (ReasonOf(a, gone) is null or "replaced").Should().BeTrue("the folder is not preserved for any reason but a change under the sweep's feet");

        // (b) the other sweep removed the QUARANTINED object between this sweep's rename and its removal.
        var rootB = _owned.Dir("b8-b");
        var quarantined = PlantFolder(rootB, Name("quarantined"), Rec(dead));
        var b = LeftoversSweep.Run(Options(rootB, hooks: new SweepHooks(AfterRename: (_, quarantine) => Directory.Delete(quarantine, true))));

        b.Errors.Should().HaveCountLessThanOrEqualTo(1);
        foreach (var error in b.Errors)
        {
            error.Should().Contain(QuarantineWord, "an error, if any, names the quarantine");
        }

        Directory.Exists(quarantined).Should().BeFalse();
        Directory.GetFileSystemEntries(rootB).Where(entry => !entry.EndsWith(".owner", StringComparison.Ordinal)).Should().BeEmpty("nothing of the folder is left in the root");
        TestContext.Current.AddWarning($"E5 S2-B B8 (b): errors={b.Errors.Count} removed={b.Removed.Count}");

        // (c) twenty gone-owner folders, two real sweeps at once.
        var rootC = _owned.Dir("b8-c");
        var twenty = Enumerable.Range(0, 20).Select(i => PlantFolder(rootC, Name("many" + i), Rec(dead))).ToList();
        SweepResult first = SweepResult.Empty, second = SweepResult.Empty;

        Parallel.Invoke(
            () => first = LeftoversSweep.Run(Options(rootC)),
            () => second = LeftoversSweep.Run(Options(rootC)));

        // STRICT again (E5 entry 5.1). On POSIX the sweep that loses the rename of a folder the other already moved reports ONE error line naming that rename (ENOENT) — never a
        // throw, never a removal of the other's work: the template's behaviour. On Windows `Directory.Move` has NO single winner (two threads racing one source both succeed in 39951
        // of 40000 races, hosted windows-latest run 41873954643), so the sweeps no longer interleave there: a named mutex over the root holds the second until the first ends, and
        // the arm expects ZERO error lines — the "Could not find" line and the double report of the run before were that race.
        if (OperatingSystem.IsWindows())
        {
            first.Errors.Should().BeEmpty("the sweeps do not interleave on Windows: the second waits for the first");
            second.Errors.Should().BeEmpty("the sweeps do not interleave on Windows: the second waits for the first");
        }
        else
        {
            foreach (var error in first.Errors.Concat(second.Errors))
            {
                error.Should().Contain(QuarantineWord, "an error, if any, is the lost rename of a folder the other sweep took");
            }
        }

        // A claim whose folder the other sweep had just removed is an ORPHAN claim to this sweep, and the copy removes it (and says so in Removed, with the claim's own path):
        // measured on macOS (a CI leg, 21 entries for 20 folders). So Removed holds folders AND claims; the promises are on each kind.
        var removed = first.Removed.Concat(second.Removed).ToList();
        var folders = removed.Where(path => !path.EndsWith(".owner", StringComparison.Ordinal)).ToList();
        var claims = removed.Where(path => path.EndsWith(".owner", StringComparison.Ordinal)).ToList();
        TestContext.Current.AddWarning($"E5 S2-B B8 (c): error lines of the two sweeps: {first.Errors.Count} + {second.Errors.Count}; removed folders {folders.Count}, removed orphan claims {claims.Count}");
        folders.Should().BeEquivalentTo(twenty, "no folder is lost twice and none is foreign: each is removed once, by exactly one of the two sweeps");
        foreach (var claim in claims)
        {
            twenty.Should().Contain(claim.Substring(0, claim.Length - ".owner".Length), "an orphan claim removed is one of the twenty planted, never another name");
        }

        Directory.GetFileSystemEntries(rootC).Should().BeEmpty("folders, claims and quarantines are all gone");

        // (d) the interleaving macOS met in CI, made on purpose: the other sweep runs in the gap between this sweep's removal of the folder and its deletion of the claim.
        // The claim has no folder any more, so the other sweep removes it as an ORPHAN claim and reports it in ITS Removed, by the claim's own path; this sweep then finds the
        // claim gone and says nothing. One folder, one report; one claim, one report; nothing is removed twice and nothing is an error.
        var rootD = _owned.Dir("b8-d");
        var split = PlantFolder(rootD, Name("split"), Rec(dead));
        SweepResult other = SweepResult.Empty;
        var mine = LeftoversSweep.Run(Options(rootD, hooks: new SweepHooks(AfterRemove: (_, _) => other = LeftoversSweep.Run(Options(rootD)))));

        mine.Removed.Should().Equal([split], "this sweep removed the folder");
        other.Removed.Should().Equal([split + ".owner"], "the other sweep removed the claim, as an orphan, and reported the claim's path");
        mine.Errors.Should().BeEmpty();
        other.Errors.Should().BeEmpty();
        Directory.GetFileSystemEntries(rootD).Should().BeEmpty();
        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    // ---- the Windows sweep lock (E5 entry 5.1) ---------------------------------------------------------------------

    [Fact]
    public void OnWindows_TwoSweepsAtOnce_25Rounds_NeverInterleave()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only: Directory.Move has no single winner there; POSIX's rename does (arm (c) above)");
        }

        var dead = Dead();
        for (var round = 0; round < 25; round++)
        {
            var root = _owned.Dir($"lock-r{round}");
            var twenty = Enumerable.Range(0, 20).Select(i => PlantFolder(root, Name("lk" + i), Rec(dead))).ToList();
            SweepResult first = SweepResult.Empty, second = SweepResult.Empty;

            Parallel.Invoke(
                () => first = LeftoversSweep.Run(Options(root)),
                () => second = LeftoversSweep.Run(Options(root)));

            first.Errors.Concat(second.Errors).Should().BeEmpty($"round {round}: no interleaving, no error line");
            first.Removed.Concat(second.Removed).Where(path => !path.EndsWith(".owner", StringComparison.Ordinal)).Should().BeEquivalentTo(twenty, $"round {round}: each folder once");
            Directory.GetFileSystemEntries(root).Should().BeEmpty($"round {round}: folders, claims and quarantines are all gone");
        }
    }

    [Fact]
    public async Task OnWindows_ASecondSweepWaitsForTheFirst_AndEntersItsPassOnlyAfterItEnds()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only: the named mutex over the temp root is the Windows sweep lock; POSIX's rename has one winner");
        }

        var dead = Dead();
        var root = _owned.Dir("lock-wait");
        var a = PlantFolder(root, Name("lka"), Rec(dead));
        var b = PlantFolder(root, Name("lkb"), Rec(dead));
        using var paused = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        SweepResult first = SweepResult.Empty, second = SweepResult.Empty;

        var one = Task.Run(() => first = LeftoversSweep.Run(Options(root, hooks: new SweepHooks(AfterRename: (_, _) => { paused.Set(); release.Wait(TimeSpan.FromSeconds(60)); }))));
        paused.Wait(TimeSpan.FromSeconds(60)).Should().BeTrue("the first sweep reached its rename");
        var two = Task.Run(() => second = LeftoversSweep.Run(Options(root)));

        try
        {
            (await Task.WhenAny(two, Task.Delay(TimeSpan.FromMilliseconds(1500)))).Should().NotBe(two, "the second sweep is held while the first is paused after its rename");
        }
        finally
        {
            release.Set(); // a red assertion must not leave the first sweep paused, holding the lock, while the fixture removes its root
        }

        await Task.WhenAll(one, two).WaitAsync(TimeSpan.FromSeconds(60));

        first.Errors.Concat(second.Errors).Should().BeEmpty();
        first.Removed.Concat(second.Removed).Where(path => !path.EndsWith(".owner", StringComparison.Ordinal)).Should().BeEquivalentTo([a, b], "each folder once");
        Directory.GetFileSystemEntries(root).Should().BeEmpty();
    }

    [Fact]
    public async Task OnWindows_ASweepThatCannotTakeTheLock_ListsOneLine_AndRemovesNothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only: the named mutex over the temp root is the Windows sweep lock");
        }

        var dead = Dead();
        var root = _owned.Dir("lock-timeout");
        var a = PlantFolder(root, Name("lta"), Rec(dead));
        var b = PlantFolder(root, Name("ltb"), Rec(dead));
        using var paused = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => LeftoversSweep.Run(Options(root, hooks: new SweepHooks(AfterRename: (_, _) => { paused.Set(); release.Wait(TimeSpan.FromSeconds(60)); }))));
        try
        {
            paused.Wait(TimeSpan.FromSeconds(60)).Should().BeTrue("the holder reached its rename");

            var waiter = LeftoversSweep.Run(Options(root, lockWait: TimeSpan.FromMilliseconds(300)));

            waiter.Listed.Should().Equal(["another sweep holds this temp root: nothing swept"]);
            waiter.Removed.Should().BeEmpty();
            waiter.Preserved.Should().BeEmpty();
            waiter.Errors.Should().BeEmpty("a sweep that cannot take the lock never fails the run");
            Directory.Exists(b).Should().BeTrue("the waiter touched nothing");
        }
        finally
        {
            release.Set();
        }

        var held = await holder.WaitAsync(TimeSpan.FromSeconds(60));
        held.Removed.Where(path => !path.EndsWith(".owner", StringComparison.Ordinal)).Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public async Task OnWindows_EverySpellingOfOneRoot_TakesTheSameLock()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only: the named mutex over the temp root is the Windows sweep lock");
        }

        var dead = Dead();
        var root = _owned.Dir("lock-spell");
        var a = PlantFolder(root, Name("lsa"), Rec(dead));
        var b = PlantFolder(root, Name("lsb"), Rec(dead));
        var link = Path.Combine(_owned.Dir("lock-spell-link"), "to-root");
        DirectoryLinks.CreateOrSkip(link, root);
        using var paused = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => LeftoversSweep.Run(Options(root, hooks: new SweepHooks(AfterRename: (_, _) => { paused.Set(); release.Wait(TimeSpan.FromSeconds(60)); }))));
        try
        {
            paused.Wait(TimeSpan.FromSeconds(60)).Should().BeTrue("the holder reached its rename");

            foreach (var spelling in new[] { root.ToUpperInvariant(), root + Path.DirectorySeparatorChar, link })
            {
                var waiter = LeftoversSweep.Run(Options(spelling, lockWait: TimeSpan.FromMilliseconds(300)));

                waiter.Listed.Should().Equal(["another sweep holds this temp root: nothing swept"], $"{spelling} is the root the holder holds");
                waiter.Removed.Should().BeEmpty($"{spelling} is the root the holder holds");
            }
        }
        finally
        {
            release.Set();
            Unlink(link);
        }

        var held = await holder.WaitAsync(TimeSpan.FromSeconds(60));
        held.Removed.Where(path => !path.EndsWith(".owner", StringComparison.Ordinal)).Should().BeEquivalentTo([a, b]);
    }

    // ---- two sweep PROCESSES on one root (E5 entry 5.1) --------------------------------------------------------------

    private const string ChildRoot = "COAI_E5_SWEEP_ROOT";
    private const string ChildGo = "COAI_E5_SWEEP_GO";
    private const string ChildReady = "COAI_E5_SWEEP_READY";
    private const string ChildOut = "COAI_E5_SWEEP_OUT";

    private sealed record ChildReport(List<string> Removed, List<string> Listed, List<string> Errors, int Preserved);

    /// <summary>
    /// The CHILD of <see cref="OnWindows_TwoSweepProcessesOnOneRoot_NeverInterleave"/>: this same test executable, started with a filter that names only this method. Without the
    /// environment it does nothing (the ordinary run passes through it). With it: say ready, wait for the go file, sweep the root once, write the report as JSON.
    /// </summary>
    [Fact]
    public void SweepChild_SweepsTheGivenRootOnce_WhenTheProcessTestStartsIt()
    {
        var root = Environment.GetEnvironmentVariable(ChildRoot);
        if (root is null) return;

        File.WriteAllText(Environment.GetEnvironmentVariable(ChildReady)!, "ready");
        var go = Environment.GetEnvironmentVariable(ChildGo)!;
        var until = DateTime.UtcNow.AddSeconds(60);
        while (!File.Exists(go) && DateTime.UtcNow < until) Thread.Sleep(2);
        File.Exists(go).Should().BeTrue("the process test releases its children together; a child that sweeps alone proves nothing");

        var result = LeftoversSweep.Run(Options(root));

        File.WriteAllText(Environment.GetEnvironmentVariable(ChildOut)!, JsonSerializer.Serialize(new ChildReport([.. result.Removed], [.. result.Listed], [.. result.Errors], result.Preserved.Count)));
    }

    [Fact]
    public async Task OnWindows_TwoSweepProcessesOnOneRoot_NeverInterleave()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows only: the named mutex over the root is the Windows sweep lock, and a mutex exists for PROCESSES — threads in one process do not prove it");
        }

        var dead = Dead();
        for (var round = 0; round < 5; round++)
        {
            var root = _owned.Dir($"lock-p{round}");
            var twenty = Enumerable.Range(0, 20).Select(i => PlantFolder(root, Name("pr" + i), Rec(dead))).ToList();
            var control = _owned.Dir($"lock-c{round}");
            var go = Path.Combine(control, "go");
            var children = Enumerable.Range(0, 2).Select(index => StartChild(root, go, Path.Combine(control, $"ready{index}"), Path.Combine(control, $"out{index}.json"))).ToList();
            try
            {
                var until = DateTime.UtcNow.AddSeconds(60);
                bool BothReady() => Enumerable.Range(0, 2).All(index => File.Exists(Path.Combine(control, $"ready{index}")));
                while (!BothReady() && DateTime.UtcNow < until) await Task.Delay(10);
                BothReady().Should().BeTrue($"round {round}: two children that do not overlap prove nothing");
                File.WriteAllText(go, "go");
                foreach (var child in children)
                {
                    child.Process.WaitForExit(120_000).Should().BeTrue($"round {round}: a sweep process ended");
                }
            }
            finally
            {
                foreach (var child in children.Where(c => !c.Process.HasExited)) child.Process.Kill(entireProcessTree: true);
            }

            foreach (var child in children) child.Process.ExitCode.Should().Be(0, $"round {round}: {await child.StdOut}{await child.StdErr}"); // first: a crashed child wrote no report, and its output is the diagnosis
            var reports = Enumerable.Range(0, 2).Select(index => JsonSerializer.Deserialize<ChildReport>(File.ReadAllText(Path.Combine(control, $"out{index}.json")))!).ToList();
            reports.SelectMany(r => r.Errors).Should().BeEmpty($"round {round}: no interleaving, no error line");
            reports.SelectMany(r => r.Listed).Should().BeEmpty($"round {round}: neither sweep gave up the lock");
            reports.SelectMany(r => r.Removed).Where(path => !path.EndsWith(".owner", StringComparison.Ordinal)).Should().BeEquivalentTo(twenty, $"round {round}: each folder removed exactly once across the two reports");
            Directory.GetFileSystemEntries(root).Should().BeEmpty($"round {round}: folders, claims and quarantines are all gone");
        }
    }

    private static (System.Diagnostics.Process Process, Task<string> StdOut, Task<string> StdErr) StartChild(string root, string go, string ready, string output)
    {
        var env = new Dictionary<string, string?> { [ChildRoot] = root, [ChildGo] = go, [ChildReady] = ready, [ChildOut] = output };
        var start = SuiteChild.StartInfoFor(Environment.ProcessPath!, ["--filter-method", "*" + nameof(SweepChild_SweepsTheGivenRootOnce_WhenTheProcessTestStartsIt)], env);
        var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("the sweep child did not start");

        return (process, process.StandardOutput.ReadToEndAsync(), process.StandardError.ReadToEndAsync());
    }

    // ---- B10 ------------------------------------------------------------------------------------------------------

    private static readonly Regex QuarantineShape = new(@"^\.(.+)\." + "teststo" + @"delete-quarantine-([0-9]+)-[0-9a-f]{8}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [Fact]
    public void TheQuarantineName_IsAlwaysLowerHex_AndTheRestorePatternReadsEveryOne()
    {
        // (a) the private drawer, 5 000 times.
        var drawer = typeof(LeftoversSweep).GetNestedType("Pass", BindingFlags.NonPublic)?.GetMethod("QuarantinePath", BindingFlags.NonPublic | BindingFlags.Static);
        drawer.Should().NotBeNull("the drawer moved: template-manifest.json must say how a test reaches it (plan B.10 item 7)");
        var root = _owned.Dir("b10-root");
        var target = Path.Combine(root, Name("draw"));
        for (var draw = 0; draw < 5000; draw++)
        {
            var name = Path.GetFileName((string)drawer!.Invoke(null, [target])!);

            var match = QuarantineShape.Match(name);
            match.Success.Should().BeTrue($"draw {draw} was {name}");
            match.Groups[2].Value.Should().Be(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // (b) fifty real removals, the drawn name recorded after each rename.
        var dead = Dead();
        var removing = _owned.Dir("b10-removals");
        var drawn = new List<string>();
        foreach (var i in Enumerable.Range(0, 50))
        {
            PlantFolder(removing, Name("rm" + i), Rec(dead));
        }

        var result = LeftoversSweep.Run(Options(removing, hooks: new SweepHooks(AfterRename: (_, quarantine) => drawn.Add(Path.GetFileName(quarantine)))));

        result.Removed.Should().HaveCount(50);
        drawn.Should().HaveCount(50).And.OnlyContain(name => QuarantineShape.IsMatch(name));
        foreach (var name in drawn.Take(20))
        {
            Directory.CreateDirectory(Path.Combine(removing, name));
        }

        var read = LeftoversSweep.QuarantinedNames(removing);
        read.Should().HaveCount(20, "the restore pattern reads every name that was drawn");
        read.Should().BeEquivalentTo(drawn.Take(20).Select(name => QuarantineShape.Match(name).Groups[1].Value), "and gives back the original name of each");
        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    // ---- B11 ------------------------------------------------------------------------------------------------------

    [Fact]
    public void AFifoAtTheClaimPath_IsUnreadable_AndTheFolderIsKept_WithinFiveSeconds()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            PosixFifoAtTheClaimPath();
            return;
        }

        Assert.Skip("POSIX only: Windows has no FIFO");
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private void PosixFifoAtTheClaimPath()
    {
        var root = _owned.Dir("b11-root");
        var dead = Dead();
        var dir = PlantFolder(root, Name("fifo"), Rec(dead));
        File.Delete(dir + ".owner");
        Fifo(dir + ".owner");
        AgeFolder(dir);
        try
        {
            var sweep = Task.Run(() => LeftoversSweep.Run(Options(root)), TestContext.Current.CancellationToken);

            sweep.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).Should().BeTrue("a FIFO is never opened for reading: a sweep that opens it blocks for ever");

            var result = sweep.Result;
            ReasonOf(result, dir).Should().Be("unreadable");
            Directory.Exists(dir).Should().BeTrue();
            File.Exists(dir + ".owner").Should().BeTrue("the FIFO is still there");
            result.Removed.Should().BeEmpty();
        }
        finally
        {
            ReleaseReaders(dir + ".owner"); // a plant that opens it would be blocked on this: let that thread end
        }

        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void Fifo(string path)
    {
        PosixFifo.Make(path);
        var mode = Native.IdentityOf(path);
        mode.Should().NotBeNull();
        mode!.Value.IsRegular.Should().BeFalse("the premise: the claim path is a FIFO, not a regular file");
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void ReleaseReaders(string path) => PosixFifo.ReleaseReaders(path);

    // ---- B13 ------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheSweep_JudgesTheSame_UnderTheRootAsGivenAndAsResolved()
    {
        var real = _owned.Dir("b13-root");
        var alias = Path.Combine(_owned.Dir("b13-aliases"), "alias");
        DirectoryLinks.CreateOrSkip(alias, real);
        var dead = Dead();
        Func<string, bool> mine = OperatingSystem.IsWindows() ? _ => true : Scope.IsMine; // Windows: B13-W holds the real key
        var live = PlantFolder(real, Name("live"), Rec(Environment.ProcessId));
        var gone = PlantFolder(real, Name("gone"), Rec(dead));

        var viaAlias = LeftoversSweep.Run(new SweepOptions(alias, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, mine));
        PlantFolder(real, Name("gone"), Rec(dead));
        var viaReal = LeftoversSweep.Run(new SweepOptions(real, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, mine));

        using (new AssertionScope())
        {
            viaAlias.Removed.Should().Equal([Path.Combine(alias, Path.GetFileName(gone))], "the entry names the root it was given");
            viaReal.Removed.Should().Equal(gone);
            ReasonOf(viaAlias, Path.Combine(alias, Path.GetFileName(live))).Should().Be("live");
            ReasonOf(viaReal, live).Should().Be("live");
            viaAlias.Errors.Should().BeEmpty();
            viaReal.Errors.Should().BeEmpty();
        }

        if (OperatingSystem.IsMacOS())
        {
            TheRunFolderIsJudgedAlikeUnderBothSpellingsOfTheRoot();
        }

        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    /// <summary>Row 12 on macOS: the temp path is <c>/var/folders/…</c>, a link to <c>/private/var/folders/…</c>. The REAL run folder, under both spellings: <c>mine</c> both ways, the sweep's listing agrees, and the claim has one identity.</summary>
    private static void TheRunFolderIsJudgedAlikeUnderBothSpellingsOfTheRoot()
    {
        var given = RunTemp.Real;
        var target = Directory.ResolveLinkTarget("/var", returnFinalTarget: true);
        target.Should().NotBeNull("the premise: /var is a link on macOS");
        given.Should().StartWith("/var/", "the premise: the machine's temp root is spelled through the /var link");
        var resolved = target!.FullName.TrimEnd('/') + given["/var".Length..];
        resolved.Should().NotBe(given, "the premise: the two spellings differ");
        var folderGiven = RunTemp.Folder;
        var folderResolved = resolved + folderGiven[given.Length..];

        Scope.IsMine(folderGiven).Should().BeTrue("mine under the root as the temp path spells it");
        Scope.IsMine(folderResolved).Should().BeTrue("mine under the resolved root");
        var late = DateTimeOffset.UtcNow + Old;
        var viaGiven = LeftoversSweep.Run(new SweepOptions(given, late, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));
        var viaResolved = LeftoversSweep.Run(new SweepOptions(resolved, late, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));

        ReasonOf(viaGiven, folderGiven).Should().Be("live", "the sweep's listing: the run folder, as given");
        ReasonOf(viaResolved, folderResolved).Should().Be("live", "the sweep's listing: the run folder, as resolved");
        var a = Native.IdentityOf(folderGiven + ".owner");
        var b = Native.IdentityOf(folderResolved + ".owner");
        a.Should().NotBeNull();
        b.Should().NotBeNull();
        (a!.Value.Dev, a.Value.Ino).Should().Be((b!.Value.Dev, b.Value.Ino), "the claim's identity is one under both spellings");
    }

    // ---- B13-W ----------------------------------------------------------------------------------------------------

    [Fact]
    public void OnWindows_TheScopeKey_ReadsTheFinalPath_NotTheGivenOne()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsScopeKey();
            return;
        }

        Assert.Skip("Windows only: the path key of Scope.IsMine is the Windows one");
    }

    [SupportedOSPlatform("windows")]
    private void WindowsScopeKey()
    {
        var runnerTemp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        var profile = Native.FinalPathOfFollowing(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var outside = runnerTemp is { Length: > 0 } && Native.FinalPathOfFollowing(runnerTemp) is { } final && profile is not null && !IsStrictlyInside(final, profile);
        if (!outside)
        {
            var why = $"premise: RUNNER_TEMP ('{runnerTemp}') is set and is NOT inside the profile ('{profile}'); on a hosted runner it is on D:";
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                Assert.Fail("failed premise on the leg that exists to run this test — " + why);
            }

            Assert.Skip(why);
        }

        var dead = Dead();
        using var ownership = new WindowsOwnership();
        var inside = _owned.Dir("b13w-inside");
        var suffix = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
        var junctionA = Path.Combine(runnerTemp!, "e5s2b-" + suffix);
        var outsideFolder = Path.Combine(runnerTemp!, Name("w-outside-" + suffix));
        var junctionB = Path.Combine(inside, "to-runner-temp");
        try
        {
            // ONE scope over both arms: a red run reports (i) AND (ii), each with its reason, not only the first that failed.
            using var both = new AssertionScope();

            // (i) a root INSIDE the profile reached through a junction OUTSIDE it: the entry's final path is inside, the given path is not.
            DirectoryLinks.CreateOrSkip(junctionA, inside);
            var g = PlantFolder(inside, Name("w-gone"), Rec(dead));
            ownership.OwnedByCaller(g);
            ownership.OwnedByCaller(g + ".owner");
            Scope.IsMine(g).Should().BeTrue("the positive premise: the folder is the caller's, inside the profile");

            var viaA = LeftoversSweep.Run(new SweepOptions(junctionA, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));

            var reasonA = ReasonOf(viaA, Path.Combine(junctionA, Path.GetFileName(g)));
            TestContext.Current.AddWarning($"E5 S2-B B13-W (i): removed={viaA.Removed.Count} reason={reasonA ?? "(none)"} errors={viaA.Errors.Count}");
            viaA.Removed.Should().Equal([Path.Combine(junctionA, Path.GetFileName(g))], $"the final path of the entry is inside the profile and its SID is the caller's (reason seen: {reasonA})");

            // (ii) a folder OUTSIDE the profile reached through a junction INSIDE it: the given path is inside, the final path is not.
            PlantFolder(runnerTemp!, Path.GetFileName(outsideFolder), Rec(dead));
            ownership.OwnedByCaller(outsideFolder);
            ownership.OwnedByCaller(outsideFolder + ".owner");
            DirectoryLinks.CreateOrSkip(junctionB, runnerTemp!);

            var viaB = LeftoversSweep.Run(new SweepOptions(junctionB, DateTimeOffset.UtcNow, LeftoversSweep.MinAge, Liveness.ProbeProcess, Scope.IsMine));

            var reasonB = ReasonOf(viaB, Path.Combine(junctionB, Path.GetFileName(outsideFolder)));
            TestContext.Current.AddWarning($"E5 S2-B B13-W (ii): removed={viaB.Removed.Count} reason={reasonB ?? "(none)"} errors={viaB.Errors.Count}");
            reasonB.Should().Be("foreign-uid", "the final path of the entry is outside the profile, whatever the given path says");
            viaB.Listed.Should().Contain(line => line.StartsWith("foreign: folder " + Path.GetFileName(outsideFolder), StringComparison.Ordinal));
            Directory.Exists(outsideFolder).Should().BeTrue("kept, bytes intact");
            File.Exists(Path.Combine(outsideFolder, "file.txt")).Should().BeTrue();
        }
        finally
        {
            Unlink(junctionA);
            Unlink(junctionB);
            TryDelete(outsideFolder);
            TryDelete(outsideFolder + ".owner");
        }

        UnmatchablePid.AssertNoSuchProcess(dead);
    }

    /// <summary>The same compare as <see cref="Scope"/>'s own: strictly inside, by path segments, ignoring case.</summary>
    private static bool IsStrictlyInside(string child, string parent)
    {
        var root = parent.TrimEnd('\\');
        return child.Length > root.Length + 1 && child.StartsWith(root, StringComparison.OrdinalIgnoreCase) && child[root.Length] == '\\';
    }

    /// <summary>A junction goes by its OWN name: a non-recursive delete never follows it.</summary>
    private static void Unlink(string link)
    {
        try
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link, false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The scope's disposal reports what stays; a link left behind must never take its target with it.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            else
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the folder is claimed and owner-form, so a later sweep judges it.
        }
    }
}
