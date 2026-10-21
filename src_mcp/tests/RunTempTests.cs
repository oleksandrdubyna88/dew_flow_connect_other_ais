using System.Text.RegularExpressions;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// A test run keeps everything it makes in "temp" inside ONE folder of its own (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>,
/// D2, scenarios S5a-S5i).
/// </summary>
/// <remarks>
/// <para>The folder is made, and <c>TEMP</c>, <c>TMP</c> and <c>TMPDIR</c> are pointed at it, by <see cref="RunTemp"/>'s module initializer — before
/// any type of this assembly is touched. The three arms below that record <c>Path.GetTempPath()</c> from a class fixture, a <c>MemberData</c> and a
/// static constructor are the order proof: each runs at a moment an assembly FIXTURE would be too late for.</para>
/// <para>This class writes no environment variable of the process: the folder logic is tested through <see cref="RunTempFolder"/> against a private
/// root and a dictionary standing in for the environment, and a child is started on its own variables through the request.</para>
/// </remarks>
public sealed class RunTempTests : IClassFixture<RunTempTests.TempSeenByAClassFixture>
{
    private static readonly string TempSeenByTheStaticConstructor;

    private static readonly Regex Claim = new(@"^[^@\s]+@(win32|linux|darwin):\d+(:\d+)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly TempSeenByAClassFixture _fixture;

    static RunTempTests() => TempSeenByTheStaticConstructor = Path.GetTempPath();

    public RunTempTests(TempSeenByAClassFixture fixture) => _fixture = fixture;

    /// <summary>What a class fixture saw when it was built: before the first test of the class.</summary>
    public sealed class TempSeenByAClassFixture
    {
        public string Temp { get; } = Path.GetTempPath();
    }

    /// <summary>A <c>MemberData</c> is read when the tests are DISCOVERED — before any fixture is built.</summary>
    public static TheoryData<string> TempSeenAtDiscovery() => new() { Path.GetTempPath() };

    private static string Trimmed(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    [Fact]
    public void ATestRun_KeepsItsTempInsideItsOwnFolder()
    {
        Trimmed(Path.GetTempPath()).Should().StartWith(RunTemp.Folder, "the run's temp folder is where every test's temp path points");
        RunTemp.Folder.Should().StartWith(RunTemp.Real).And.NotBe(RunTemp.Real, "the run folder is INSIDE the machine's temp folder, never it");
        Path.GetFileName(RunTemp.Folder).Should().MatchRegex("^testsToDelete-coai-run-[0-9a-f]{6}$");
    }

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
            "the round made its answers folder through the process's temp variables, so it is inside the run's folder");
    }

    [Fact]
    public void TheRunFoldersClaim_NamesThisProcess_InE5sFormat()
    {
        var text = File.ReadAllText(RunTemp.Folder + ".owner");

        OwnerClaim.IsWellFormed(text).Should().BeTrue("<host>@<os>:<pid>[:<start>], read STRICTLY (whole-text anchors): the family's owner claim");
        text.Should().NotEndWith("\n", "no trailing newline: .NET's $ matches before a final line feed, so a lenient regex would let one through").And.NotContain("\r");
        var fields = text.Split('@')[1].Split(':');
        fields[1].Should().Be(Environment.ProcessId.ToString(), "the claim names THIS process");
        fields.Length.Should().Be(OperatingSystem.IsLinux() ? 3 : 2, "only Linux gives a start time without spawning a process");
        if (OperatingSystem.IsLinux())
        {
            long.TryParse(fields[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var recorded).Should().BeTrue("digits only, no whitespace");
            Math.Abs(recorded - new DateTimeOffset(System.Diagnostics.Process.GetCurrentProcess().StartTime).ToUnixTimeSeconds())
                .Should().BeLessThanOrEqualTo(2, "the start field is this process's own start");
        }
    }

    [Fact]
    public void TheClaimText_IsOneLine_AndCarriesTheStartOnlyWhereItIsGiven()
    {
        OwnerClaim.Format("host", "linux", 42, 1_700_000_000).Should().Be("host@linux:42:1700000000");
        OwnerClaim.Format("host", "win32", 42, null).Should().Be("host@win32:42");
        OwnerClaim.Format("host", "darwin", 42, null).Should().MatchRegex(Claim.ToString());
    }

    /// <summary>A stand-in for the process environment, and the order everything happened in.</summary>
    private sealed class FakeEnvironment(Dictionary<string, string?> values)
    {
        public Dictionary<string, string?> Values { get; } = values;

        public List<string> Events { get; } = [];

        public RunTempSeams Seams(string root) => new(Set, name => Values.GetValueOrDefault(name))
        {
            RegisterRestore = _ => Events.Add($"registered folder={FolderExists(root)} claim={ClaimExists(root)} sets={Sets()}"),
            WriteClaim = folder => WriteClaim(root, folder),
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

        private void WriteClaim(string root, string folder)
        {
            Events.Add($"claim folder={FolderExists(root)} sets={Sets()} registered={Events.Any(e => e.StartsWith("registered", StringComparison.Ordinal))}");
            File.WriteAllText(folder + ".owner", "claim");
        }

        private static bool FolderExists(string root) => Directory.GetDirectories(root, "testsToDelete-coai-run-*").Length > 0;

        private static bool ClaimExists(string root) => Directory.GetFiles(root, "testsToDelete-coai-run-*.owner").Length > 0;
    }

    private static Dictionary<string, string?> SomeoneElsesTemp() =>
        new(StringComparer.Ordinal) { ["TEMP"] = "old-temp", ["TMPDIR"] = "old-tmpdir" };

    [Fact]
    public void TheClaimIsWrittenAfterTheFolderExists_AndTheRestoreIsRegistered()
    {
        using var root = TempDir.For("testsToDelete-e2-root-");
        var environment = new FakeEnvironment(SomeoneElsesTemp());

        var run = RunTempFolder.Create(root.Path, environment.Seams(root.Path));

        environment.Events.Should().Equal(
            "registered folder=True claim=False sets=0",
            "set TEMP",
            "set TMP",
            "set TMPDIR",
            "claim folder=True sets=3 registered=True");
        File.Exists(run.ClaimFile).Should().BeTrue();
        run.Restore();
    }

    [Fact]
    public void RestoreIsIdempotent_AndPutsTheOldValuesBack()
    {
        using var root = TempDir.For("testsToDelete-e2-root-");
        var environment = new FakeEnvironment(SomeoneElsesTemp());
        var run = RunTempFolder.Create(root.Path, environment.Seams(root.Path));
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
        using var root = TempDir.For("testsToDelete-e2-root-");
        var environment = new FakeEnvironment(SomeoneElsesTemp());
        var run = RunTempFolder.Create(root.Path, environment.Seams(root.Path) with { Delete = _ => throw new IOException("held by a scanner") });

        var act = () => run.Restore();

        act.Should().NotThrow("a leftover is the janitor's, never a reason to fail the end of a run");
        environment.Events.Should().Contain(e => e.StartsWith("warn", StringComparison.Ordinal) && e.Contains(run.Folder, StringComparison.Ordinal),
            "the warning names the folder a person has to delete");
        environment.Values["TEMP"].Should().Be("old-temp", "the variables are restored even when the delete fails");
    }

    /// <summary>The verdict table of <see cref="OwnerClaim.Verdict"/>: what the sweeper may take, and what it must keep.</summary>
    public static TheoryData<string, bool, ClaimVerdict> Verdicts() => new()
    {
        { "host@linux:4242:1000", true, ClaimVerdict.Keep },
        { "host@win32:4242", true, ClaimVerdict.Keep },
        { "host@win32:4242", false, ClaimVerdict.Leftover },
        { "host@darwin:4242", false, ClaimVerdict.Leftover },
        { "host@linux:4242:1000", false, ClaimVerdict.Leftover },
        { "not a claim at all", false, ClaimVerdict.Keep },
        { string.Empty, false, ClaimVerdict.Keep },
        { "host@plan9:4242", false, ClaimVerdict.Keep },
        { "host@win32:4242\nsecond line", false, ClaimVerdict.Keep },
    };

    [Theory]
    [MemberData(nameof(Verdicts))]
    public void TheSweepersVerdict_KeepsWhatItCannotProveDead(string claim, bool pidAlive, ClaimVerdict expected) =>
        OwnerClaim.Verdict(claim, pidAlive).Should().Be(expected, "a live pid — whatever its start, a reused pid included — and a claim that does not parse are LEAKS, never deletions");

    private static string TestsExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "CoaiMcp.Tests.exe" : "CoaiMcp.Tests");

    [Theory]
    [InlineData("--list-tests")]
    [InlineData("--help")]
    [InlineData("--filter-method")]
    public async Task ARunThatBuildsNoFixture_LeavesNothing(string flag)
    {
        using var theMachinesTemp = TempDir.For("testsToDelete-e2-realtemp-");
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
