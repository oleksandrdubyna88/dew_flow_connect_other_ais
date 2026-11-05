using System.Reflection;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A test run keeps everything it makes in "temp" inside ONE folder of its own, in the owner form, claimed first, and a run that cannot have it does not start
/// (<c>todo/PLAN_test_leftovers_E5.md</c>, S3; the run folder first came with E2, scenarios S5a-S5i).
/// </summary>
/// <remarks>
/// <para>The folder is made, claimed and registered by <see cref="RunTemp.Start"/>, called from this assembly's module initializer (<c>RunTempStart.cs</c>), and the
/// gated <c>Main</c> (<c>TestEntryPoint.cs</c>) refuses a run whose folder is missing, before discovery. No ORDER is assumed between collections (they run in
/// parallel): the gate is the guarantee. The three arms below that record <c>Path.GetTempPath()</c> from a class fixture, a <c>MemberData</c> and a static
/// constructor stay as a witness that nothing read the machine's temp first.</para>
/// <para><b>This file is the suite's proof file</b> (S6 marks it for the <c>raw-temp</c> baseline): the TEMP assertion and the three forced-failure child runs —
/// a folder that cannot be made, a claim that cannot be written, and a TEMP, TMP or TMPDIR that does not name the run folder — each exit 78 before discovery.</para>
/// <para>This class writes no environment variable of the process: the folder logic is tested through <see cref="RunTempFolder"/> against a private root and a
/// dictionary standing in for the environment, and a child is started on its own variables through <see cref="SuiteChild"/>.</para>
/// </remarks>
public sealed class RunTempTests : IClassFixture<RunTempTests.TempSeenByAClassFixture>, IDisposable
{
    private static readonly string TempSeenByTheStaticConstructor;

    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    private readonly TempSeenByAClassFixture _fixture;
    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    static RunTempTests() => TempSeenByTheStaticConstructor = Path.GetTempPath();

    public RunTempTests(TempSeenByAClassFixture fixture) => _fixture = fixture;

    public void Dispose() => _owned.Dispose();

    /// <summary>What a class fixture saw when it was built: before the first test of the class.</summary>
    public sealed class TempSeenByAClassFixture
    {
        public string Temp { get; } = Path.GetTempPath();
    }

    /// <summary>A <c>MemberData</c> is read when the tests are DISCOVERED — before any fixture is built.</summary>
    public static TheoryData<string> TempSeenAtDiscovery() => new() { Path.GetTempPath() };

    private static string Trimmed(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>The leaf of the run folder: this owner's base name, <c>run-</c>, six characters of [A-Za-z0-9].</summary>
    private static Regex RunLeaf { get; } = new("^" + Regex.Escape(Owned.NameBase) + "run-[A-Za-z0-9]{6}\\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static Dictionary<string, string?> AllThreeAt(string path) => RunTempFolder.Variables.ToDictionary(name => name, _ => (string?)path);

    private static string[] LinesOf(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void ATestRun_KeepsItsTempInsideItsOwnFolder()
    {
        RunTemp.Refusal().Should().BeNull("a run that reached a test passed the gate");
        RunTemp.IsActive.Should().BeTrue("Gate checked and passed this run");
        Trimmed(Path.GetTempPath()).Should().Be(RunTemp.Folder, "the run's temp folder is where every test's temp path points");
        Path.GetDirectoryName(RunTemp.Folder).Should().Be(RunTemp.Real, "the run folder is INSIDE the machine's temp folder, never it");
        Path.GetFileName(RunTemp.Folder).Should().MatchRegex(RunLeaf.ToString(), "the owner form: the name base of this owner and repository, run-, six characters");
        RunTempFolder.Variables.Select(Environment.GetEnvironmentVariable).Should().OnlyContain(value => value == RunTemp.Folder, "TEMP, TMP and TMPDIR all name the run folder");
    }

    /// <summary>D8's merge condition (E5 S2-B): this suite makes an owner-form run folder AND registers the start-of-run sweep (the copied fixture) AND the order proof, in ONE assembly. A producer without the sweep, or the sweep without the producer, is red.</summary>
    [Fact]
    public void TheProducerAndTheSweep_LandTogether()
    {
        RunTemp.IsActive.Should().BeTrue("the producer: Gate checked and passed this run");
        System.IO.Path.GetFileName(RunTemp.Folder).Should().MatchRegex(
            "^" + System.Text.RegularExpressions.Regex.Escape(Owned.NameBase + "run-") + "[A-Za-z0-9]{6}\\z", "the producer: the run folder is of the owner form");
        var assembly = Assembly.GetExecutingAssembly();

        var fixtures = assembly.GetCustomAttributes<AssemblyFixtureAttribute>().Select(a => a.AssemblyFixtureType).ToList();
        fixtures.Should().Contain(typeof(LeftoversSweepFixture), "the start-of-run sweep is an assembly fixture of THIS suite");
        fixtures.Should().Contain(typeof(RunTempFixture), "the run folder's fallback refusal");
        assembly.GetCustomAttributes<StartOfRunOrderAttribute>().Should().ContainSingle("the order proof runs on every test of THIS suite");
    }

    /// <summary>The re-review's finding e1: the sweep ENDED before the first test of any collection started. The attribute proves it on every test; this one writes the margin down.</summary>
    [Fact]
    public void TheSweep_EndedBeforeTheFirstTestStarted()
    {
        StartOfRunSweep.EndedAt.Should().NotBe(0, "the sweep returned (a result or an error) and stamped its end");
        StartOfRunSweep.FirstTestStart.Should().NotBe(0, "this test's own Before recorded a start");
        StartOfRunSweep.FirstTestStart.Should().BeGreaterThan(StartOfRunSweep.EndedAt, "no test started before the sweep ended");
        StartOfRunSweep.LastRoot.Should().Be(RunTemp.Real, "the sweep was handed the machine's temp root, never the run folder");
        var margin = System.Diagnostics.Stopwatch.GetElapsedTime(StartOfRunSweep.EndedAt, StartOfRunSweep.FirstTestStart);
        TestContext.Current.AddWarning($"E5 S2-B order: the start-of-run sweep ended {margin.TotalMilliseconds:F1} ms before the first test started");
    }

    [Fact]
    public void TheEntryPoint_IsTheGatedOne() =>
        Assembly.GetEntryAssembly()!.EntryPoint!.DeclaringType!.FullName.Should().Be("CoaiMcp.Tests.TestEntryPoint", "xUnit's generated Main is behind RunTemp.Gate");

    [Fact]
    public void AClassFixtureBuiltBeforeAnyTest_AlreadySawTheRunFolder() =>
        Trimmed(_fixture.Temp).Should().StartWith(RunTemp.Folder, "a fixture is built before the first test of its class");

    [Theory]
    [MemberData(nameof(TempSeenAtDiscovery))]
    public void AMemberDataReadAtDiscovery_AlreadySawTheRunFolder(string seen) =>
        Trimmed(seen).Should().StartWith(RunTemp.Folder, "discovery runs before any fixture is built");

    [Fact]
    public void AStaticConstructorOfATestClass_AlreadySawTheRunFolder() =>
        Trimmed(TempSeenByTheStaticConstructor).Should().StartWith(RunTemp.Folder, "a type initialiser runs before the first test of the class");

    /// <summary>The three variables a child's temp path can be read from, printed by a shell: <c>TEMP;TMP;TMPDIR</c>.</summary>
    private static ProcessRequest PrintTheThreeVariables() =>
        OperatingSystem.IsWindows()
            ? new ProcessRequest("cmd.exe", ["/c", "echo %TEMP%;%TMP%;%TMPDIR%"], AppContext.BaseDirectory) { InheritsEnvironment = false }
            : new ProcessRequest("sh", ["-c", "printf '%s;%s;%s' \"$TMPDIR\" \"$TMP\" \"$TEMP\""], AppContext.BaseDirectory) { InheritsEnvironment = false };

    [Fact]
    public async Task AConfinedChild_IsGivenTheRunFolderAsItsTemp_OnAllThreeVariables()
    {
        var result = await new ProcessLauncher().RunAsync(PrintTheThreeVariables(), TestContext.Current.CancellationToken);

        var printed = result.StdOut.Trim().Split(';').Select(Trimmed).ToList();
        printed.Should().HaveCount(3).And.OnlyContain(
            p => p.StartsWith(RunTemp.Folder, StringComparison.Ordinal),
            $"a child on the launcher's allowlist copies TEMP, TMP and TMPDIR from this process: {result.StdOut} / {result.StdErr}");
    }

    [Fact]
    public void AProductRound_MakesItsAnswersFolderInsideTheRunFolder()
    {
        using var scratch = TempDir.For("coai-runtemp-round-");
        var service = new PanelService(
            new PanelSettings
            {
                DataDir = scratch.At("data"),
                CodeWorkspace = "none",
                Providers = [new ProviderSettings("alpha") { Enabled = true, Runtime = "local", Model = "m" }],
            },
            VaultKeys.None("no vault"), default, new ProcessLauncher(), Serilog.Core.Logger.None, Noticing.None);
        Directory.CreateDirectory(scratch.At("wt"));

        var reviewers = service.Roster.BuildWork(
            [RoleCatalog.ConventionsRole], scratch.At("wt"), "ctx", round: 1, stage: Stage.CodeReview, readsCheckout: true,
            seed: 1, codexTiers: CodexTiers.None).Reviewers;

        reviewers.Should().NotBeEmpty();
        reviewers.Select(r => r.Invocation.OutputFile).Where(f => f.Length > 0).Should().OnlyContain(
            f => f.StartsWith(RunTemp.Folder, StringComparison.Ordinal) && f.Contains("coai-answers-", StringComparison.Ordinal),
            "the round made its answers folder through the process's temp variables, so it is inside the run folder");
    }

    /// <summary>A stand-in for the process environment, and the order everything happened in.</summary>
    private sealed class FakeEnvironment(Dictionary<string, string?> values, string root)
    {
        public Dictionary<string, string?> Values { get; } = values;

        public List<string> Events { get; } = [];

        public RunTempSeams Seams() => new(Set, name => Values.GetValueOrDefault(name))
        {
            AfterClaim = claim => Events.Add($"after-claim claim={File.Exists(claim)} folder={FolderExists()}"),
            RegisterRestore = _ => Events.Add($"registered folder={FolderExists()} claim={ClaimExists()} sets={Sets()}"),
            Warn = message => Events.Add("warn " + message),
        };

        private int Sets() => Events.Count(e => e.StartsWith("set", StringComparison.Ordinal));

        private void Set(string name, string? value)
        {
            Events.Add($"set {name}");
            if (value is null)
            {
                Values.Remove(name);
            }
            else
            {
                Values[name] = value;
            }
        }

        private bool FolderExists() => Directory.GetDirectories(root).Length > 0;

        private bool ClaimExists() => Directory.GetFiles(root, "*.owner").Length > 0;
    }

    private static Dictionary<string, string?> SomeoneElsesTemp() =>
        new(StringComparer.Ordinal) { ["TEMP"] = "old-temp", ["TMPDIR"] = "old-tmpdir" };

    [Fact]
    public void TheRunFolder_IsOwnerForm_AndClaimedFirst()
    {
        var root = _owned.Dir("s3-order");
        var environment = new FakeEnvironment(SomeoneElsesTemp(), root);

        var run = RunTempFolder.Create(root, environment.Seams());

        environment.Events.Should().Equal(
            "after-claim claim=True folder=False",
            "registered folder=True claim=True sets=0",
            "set TEMP",
            "set TMP",
            "set TMPDIR");
        var leaf = Path.GetFileName(run.Folder);
        leaf.Should().MatchRegex(RunLeaf.ToString());
        Owned.Classify(leaf).Should().Be(new NameForm.Owner(OwnerToken.This, "coai"));
        var claim = File.ReadAllText(run.ClaimFile);
        Owner.Parse(claim).Should().Be(Owner.RunRecord(), "the claim is this process's owner record, in the contract's grammar");
        Owner.Parse(claim)!.Host.Should().MatchRegex("@(linux|macos|windows)\\z");
        run.Restore();
    }

    [Fact]
    public void RestoreIsIdempotent_AndPutsTheOldValuesBack()
    {
        var root = _owned.Dir("s3-restore");
        var environment = new FakeEnvironment(SomeoneElsesTemp(), root);
        var run = RunTempFolder.Create(root, environment.Seams());
        File.WriteAllText(Path.Combine(run.Folder, "made-by-a-test.txt"), "x");
        environment.Values.Should().OnlyContain(pair => pair.Value == run.Folder, "all three variables point at the run folder");

        run.Restore();

        environment.Values.Should().Equal(new Dictionary<string, string?> { ["TEMP"] = "old-temp", ["TMPDIR"] = "old-tmpdir" }, "TMP was absent before: it is removed, not set to empty");
        Directory.Exists(run.Folder).Should().BeFalse("the folder goes, with what a test left in it");
        File.Exists(run.ClaimFile).Should().BeFalse();

        environment.Values["TEMP"] = "changed-later";
        var again = () => run.Restore();
        again.Should().NotThrow("a second call does nothing");
        environment.Values["TEMP"].Should().Be("changed-later", "a second restore must not touch the variables again");
    }

    [Fact]
    public void AFolderThatCannotBeDeleted_IsWarnedAbout_AndRestoreNeverThrows()
    {
        var root = _owned.Dir("s3-undeletable");
        var environment = new FakeEnvironment(SomeoneElsesTemp(), root);
        var run = RunTempFolder.Create(root, environment.Seams() with { Delete = _ => throw new IOException("held by a scanner") });

        var act = () => run.Restore();

        act.Should().NotThrow("a leftover is the sweep's, never a reason to fail the end of a run");
        environment.Values["TEMP"].Should().Be("old-temp", "the variables are restored even when the delete fails");
    }

    private string TheThreeVariablesAreAFolder(out string root)
    {
        root = _owned.Dir("s3-child");

        return root;
    }

    [Fact]
    public void ARunThatCannotMakeItsFolder_FailsBeforeAnyTest()
    {
        TheThreeVariablesAreAFolder(out var root);
        var missing = Path.Combine(root, "missing");

        var result = RefusedChild(root, missing, null, out var sentinel);

        TheOneLineRefusal(result, root, missing, sentinel);
    }

    /// <summary>
    /// A forced-failure child: the three variables at <paramref name="at"/>, started on <c>DiscoverySentinel</c> with the sentinel in <paramref name="root"/>, so a run that
    /// got as far as discovery or a test body would have written it.
    /// </summary>
    private static SuiteChild.Result RefusedChild(string root, string at, string? selfTest, out string sentinel)
    {
        sentinel = Path.Combine(root, "sentinel");
        var env = AllThreeAt(at);
        env[RunTempChildren.SentinelVariable] = sentinel;
        if (selfTest is not null)
        {
            env[RunTemp.SelfTestVariable] = selfTest;
        }

        return SuiteChild.Run(["--filter-class", "*DiscoverySentinel"], env, ChildTimeout);
    }

    /// <summary>Exit 78, stdout without a result line, ONE stderr line starting with the prefix and holding <paramref name="mentions"/>, the sentinel untouched, nothing left in the root.</summary>
    private static void TheOneLineRefusal(SuiteChild.Result result, string root, string mentions, string sentinel)
    {
        result.TimedOut.Should().BeFalse("the child must end by itself: " + result.StdErr);
        result.ExitCode.Should().Be(RunTemp.RefusedExitCode, $"stdout: {result.StdOut} / stderr: {result.StdErr}");
        LinesOf(result.StdErr).Should().ContainSingle().Which.Should().StartWith(RunTemp.RefusalPrefix).And.Contain(mentions);
        result.StdOut.Should().NotContain("total:", "no test ran");
        File.Exists(sentinel).Should().BeFalse("discovery never ran: the refusal is before it");
        Directory.GetFileSystemEntries(root).Where(entry => !entry.EndsWith("missing", StringComparison.Ordinal)).Should().BeEmpty("the refused child left nothing in its root");
    }

    [Fact]
    public void ARunThatCannotMakeItsFolder_InAReadOnlyRoot_FailsBeforeAnyTest()
    {
        TheThreeVariablesAreAFolder(out var root);
        var readOnly = Path.Combine(root, "readonly");
        Directory.CreateDirectory(readOnly);

        using (OperatingSystem.IsWindows() ? DenyWriting(readOnly) : MakeReadOnlyPosix(readOnly))
        {
            var result = RefusedChild(root, readOnly, null, out var sentinel);

            File.Exists(sentinel).Should().BeFalse("discovery never ran");
            result.TimedOut.Should().BeFalse(result.StdErr);
            result.ExitCode.Should().Be(RunTemp.RefusedExitCode, $"stdout: {result.StdOut} / stderr: {result.StdErr}");
            LinesOf(result.StdErr).Should().ContainSingle().Which.Should().StartWith(RunTemp.RefusalPrefix).And.Contain(readOnly);
            Directory.GetFileSystemEntries(readOnly).Should().BeEmpty("nothing was made in a root nobody can write");
        }
    }

    [UnsupportedOSPlatform("windows")]
    private static Releaser MakeReadOnlyPosix(string folder)
    {
        if (Native.GetUid() == 0)
        {
            Assert.Skip("uid 0 writes into a mode 0555 folder, so the read-only root cannot be provoked");
        }

        var before = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            File.WriteAllText(Path.Combine(folder, "probe"), "x");
        }
        catch (UnauthorizedAccessException)
        {
            return new Releaser(() => File.SetUnixFileMode(folder, before));
        }

        File.SetUnixFileMode(folder, before);
        Assert.Skip("the premise failed: a write into a mode 0555 folder succeeded");

        return new Releaser(() => { });
    }

    [SupportedOSPlatform("windows")]
    private static Releaser DenyWriting(string folder)
    {
        var caller = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(
            caller, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.WriteData | FileSystemRights.AppendData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny);
        var info = new DirectoryInfo(folder);
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        try
        {
            File.WriteAllText(Path.Combine(folder, "probe"), "x");
        }
        catch (UnauthorizedAccessException)
        {
            return new Releaser(() =>
            {
                var restored = info.GetAccessControl();
                restored.RemoveAccessRule(rule);
                info.SetAccessControl(restored);
            });
        }

        var undone = info.GetAccessControl();
        undone.RemoveAccessRule(rule);
        info.SetAccessControl(undone);
        Assert.Skip("the premise failed: a write into a folder with a deny ACE for the caller succeeded");

        return new Releaser(() => { });
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    [Fact]
    public void AClaimThatCannotBeWritten_FailsTheRun()
    {
        var root = _owned.Dir("s3-claim");

        var result = RefusedChild(root, root, "fail-claim", out var sentinel);

        TheOneLineRefusal(result, root, "could not claim", sentinel);
    }

    /// <summary>Each of the three variables, changed ALONE after the folder was made, is refused on its own: one combined case would pass for a check of only one of them.</summary>
    [Theory]
    [InlineData("mismatch-temp", "TEMP")]
    [InlineData("mismatch-tmp", "TMP")]
    [InlineData("mismatch-tmpdir", "TMPDIR")]
    public void ARunWhoseVariableNoLongerNamesItsFolder_IsRefused_ForEachVariableAlone(string selfTest, string variable)
    {
        var root = _owned.Dir("s3-lost");

        var result = RefusedChild(root, root, selfTest, out var sentinel);

        TheOneLineRefusal(result, root, "TEMP, TMP and TMPDIR must all name this run's folder", sentinel);
        LinesOf(result.StdErr).Single().Should().Contain($"{variable}='").And.Contain("-mismatch'", $"only {variable} was changed");
    }

    /// <summary>
    /// D9, merge condition (o) (E5 S6): the end-of-run audit reads the run folder BEFORE the run ends. A child plants an orphan claim (a claim whose folder is missing) inside
    /// its own run folder, absent from the registry and with no inherited <c>TESTSTODELETE_RUN</c>; only <c>Discover</c> sees it, and <c>Discover</c> answers nothing once the run folder is
    /// gone. So the audit must run first, and the run must fail with its message.
    /// </summary>
    [Fact]
    public void AnOrphanClaim_IsReportedByTheAudit_BeforeTheRunFolderEnds()
    {
        var root = _owned.Dir("s6-orphan");
        var env = AllThreeAt(root);
        env["COAI_S6_ORPHAN_CLAIM"] = "1";
        env["TESTSTODELETE_RUN"] = null; // SuiteChild removes a variable given as null: the child's own record is what the audit reads

        var result = SuiteChild.Run(["--filter-class", "*OrphanClaimChild"], env, ChildTimeout);

        result.TimedOut.Should().BeFalse(result.StdErr);
        result.ExitCode.Should().NotBe(0, "the audit fails the run");
        (result.StdOut + result.StdErr).Should().Contain("coai tests: this run left 1 folder(s) it claimed").And.Contain("1 claimed in the temp root");
        Directory.GetFileSystemEntries(root).Should().BeEmpty("the audit removes what it names, and the run's end removes the rest");
    }

    private static string TestsExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "CoaiMcp.Tests.exe" : "CoaiMcp.Tests");

    [Theory]
    [InlineData("--list-tests")]
    [InlineData("--help")]
    [InlineData("--filter-method")]
    public async Task ARunThatBuildsNoFixture_LeavesNothing(string flag)
    {
        using var theMachinesTemp = TempDir.For("e2-realtemp-");
        string[] arguments = flag == "--filter-method" ? [flag, "NoSuchTest_7f3a"] : [flag];

        var result = await new ProcessLauncher().RunAsync(
            new ProcessRequest(TestsExe, arguments, AppContext.BaseDirectory)
            {
                Environment = new Dictionary<string, string?>
                {
                    ["TEMP"] = theMachinesTemp.Path,
                    ["TMP"] = theMachinesTemp.Path,
                    ["TMPDIR"] = theMachinesTemp.Path,
                },
                Timeout = TimeSpan.FromMinutes(3),
            },
            TestContext.Current.CancellationToken);

        result.TimedOut.Should().BeFalse("the child must have ended by itself: " + result.StdErr);
        (result.StdOut + result.StdErr).Should().NotBeEmpty("a child that printed nothing may not have started at all");
        Directory.GetFileSystemEntries(theMachinesTemp.Path).Should().BeEmpty(
            "a run that never builds a fixture — listing, help, a filter matching nothing — still removes its folder and its claim at process exit");
    }
}
