using CoaiMcp.FakeCli;
using CoaiMcp.Runners.Processes;
using FluentAssertions;
using FluentAssertions.Execution;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The fake CLI reads its minimal-environment steering file from BESIDE ITS BINARY, never from the user's temp folder
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, C1/D1, scenarios S1 and S2).
/// </summary>
/// <remarks>
/// <para>The temp folder is one folder per Windows user, so a second run from another worktree wrote and deleted the file the first
/// run's child was reading, and one run's fake answered as another run's agy. The child is started here with <c>TEMP</c>, <c>TMP</c>
/// and <c>TMPDIR</c> pointed at a PRIVATE folder through the request, never by moving the test process's own variables, and the
/// test never writes into the machine's real temp folder.</para>
/// <para>In the <c>fakecli-env</c> collection, as every class that writes the steering file must be
/// (<c>EveryWriterOfTheSteeringFileTests</c>).</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class AFakeCliSteeringFileIsBesideItsBinaryTests : IDisposable
{
    private static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    private readonly ProcessLauncher _launcher = new();
    private readonly TempDir _usersTemp = TempDir.For("testsToDelete-steering-temp-");
    private IDisposable _steering = FakeCliSteering.Unwritten;

    public AFakeCliSteeringFileIsBesideItsBinaryTests() => FakeCliSteering.Reset();

    public void Dispose()
    {
        _steering.Dispose();
        _usersTemp.Dispose();
    }

    /// <summary>A vendor-shaped launch (<c>exec …</c>), no mode in its environment, its temp folder the one given.</summary>
    private Task<ProcessResult> VendorLaunchAsync() =>
        _launcher.RunAsync(
            new ProcessRequest(FakeCliExe, ["exec", "hello"], _usersTemp.Path)
            {
                Environment = new Dictionary<string, string?>
                {
                    ["FAKECLI_MODE"] = null,
                    ["TEMP"] = _usersTemp.Path,
                    ["TMP"] = _usersTemp.Path,
                    ["TMPDIR"] = _usersTemp.Path,
                },
                Timeout = TimeSpan.FromSeconds(60),
            },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ASteeringFileAnotherRunLeftInTheUsersTemp_NeverSteersThisRunsFake()
    {
        // What another run's agy scenario writes where the fake used to look: a decoy that says "exit 7, print decoy".
        await File.WriteAllTextAsync(
            Path.Combine(_usersTemp.Path, MinimalSteeringFile.Name),
            """{"FAKECLI_STDOUT":"decoy","FAKECLI_EXIT":"7"}""",
            TestContext.Current.CancellationToken);

        var result = await VendorLaunchAsync();
        using var together = new AssertionScope();
        result.StdOut.Should().NotContain("decoy", "the fake reads its steering from beside its binary; the user's temp folder is every run's, so another run's file steered this one");
        result.ExitCode.Should().Be(64, "with nothing beside the binary the fake falls through to its verb mode and refuses 'exec' as an unknown verb: " + result.StdErr);
    }

    [Fact]
    public async Task ASteeringFileBesideTheBinary_SteersTheFake()
    {
        _steering = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_STDOUT"] = "steered", ["FAKECLI_EXIT"] = "5" });

        var result = await VendorLaunchAsync();

        result.StdOut.Should().Contain("steered", "the file beside the binary is what a child on a minimal environment is steered by");
        result.ExitCode.Should().Be(5);
    }

    [Fact]
    public void TheSteeringFile_SitsInTheFolderOfTheFakesBinary()
    {
        // The premise of the placement: the child's AppContext.BaseDirectory is the folder its binary is in, and that is the
        // folder the test's own AppContext.BaseDirectory names, because the build copies the fake beside the tests.
        Path.GetDirectoryName(FakeCliSteering.MinimalFile).Should().Be(Path.GetDirectoryName(Path.GetFullPath(FakeCliExe)));
        File.Exists(Path.Combine(AppContext.BaseDirectory, "FakeCli.dll")).Should().BeTrue();
    }

    [Fact]
    public void ASteeringFileAKilledRunLeftBesideTheBinary_IsDeletedAtTheNextRunsStart()
    {
        // A run killed before its DisposeAsync leaves the file: the lease is dropped without being disposed.
        _ = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_STDOUT"] = "left behind" });
        File.Exists(FakeCliSteering.MinimalFile).Should().BeTrue("the killed run wrote it");

        FakeCliSteering.DeleteALeftover(_ => { }).Should().BeTrue("there was one to delete");

        File.Exists(FakeCliSteering.MinimalFile).Should().BeFalse();
        FakeCliSteering.DeleteALeftover(_ => { }).Should().BeFalse("a second start finds none, and that is not a fault");
    }

    [Fact]
    public void ASteeringFileThatCannotBeDeleted_IsWarnedAbout_AndDoesNotBlockTheRun()
    {
        // A handle another process holds (a scanner, a killed run's orphan) makes the delete throw on Windows. The leftover is the NEXT
        // run's problem to report, not a reason for the whole assembly to fail: the fixture that calls this runs before every test.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "deleting a file someone has open succeeds on Unix; the held handle is Windows'");
        _ = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_STDOUT"] = "left behind" });
        var warnings = new List<string>();
        using (new FileStream(FakeCliSteering.MinimalFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = () => FakeCliSteering.DeleteALeftover(warnings.Add);

            act.Should().NotThrow("a leftover that cannot be deleted is reported, never fatal");
            File.Exists(FakeCliSteering.MinimalFile).Should().BeTrue("the held file is still there");
        }

        warnings.Should().ContainSingle().Which.Should().Contain(FakeCliSteering.MinimalFile, "the warning names the file a person has to delete");
        FakeCliSteering.DeleteALeftover(_ => { }).Should().BeTrue("once the handle is gone the next start removes it");
    }

    [Fact]
    public void AWritersLeaseThatCannotDeleteTheFile_DoesNotFailItsOwnTest()
    {
        // The four classes this helper replaced each deleted best-effort in Dispose; the helper must not be stricter than they were —
        // a failing Dispose fails a test that passed, over a file the next start sweeps anyway.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "deleting a file someone has open succeeds on Unix; the held handle is Windows'");
        var lease = FakeCliSteering.Write(new Dictionary<string, string> { ["FAKECLI_STDOUT"] = "held" });
        using (new FileStream(FakeCliSteering.MinimalFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = lease.Dispose;

            act.Should().NotThrow("a lease's cleanup is best-effort");
        }

        FakeCliSteering.DeleteALeftover(_ => { }).Should().BeTrue("the file the lease could not delete is the next start's to remove");
    }
}
