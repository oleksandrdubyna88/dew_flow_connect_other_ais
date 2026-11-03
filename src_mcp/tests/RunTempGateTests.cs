using CoaiMcp.Tests.Leftovers;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The gate's other scenarios (E5 S3): the refusal table, the pure information flags, <c>--list-tests</c>, the fallback behind the gate, and a killed run's
/// collection by the sweep. The three forced-failure child runs the baseline's proof reads are in <c>RunTempTests</c>, the suite's proof file.
/// </summary>
public sealed class RunTempGateTests : IDisposable
{
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromMinutes(3);

    private const string Exe = "/the/test/exe";

    private readonly OwnedScope _owned = DarwinLayout.FreshOwnedScope();

    public void Dispose() => _owned.Dispose();

    private static Dictionary<string, string?> AllThreeAt(string path) => RunTempFolder.Variables.ToDictionary(name => name, _ => (string?)path);

    private static string[] LinesOf(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static string Said(string? refusal) => refusal ?? "<no refusal>";

    [Fact]
    public void AClaimThatCannotBeWritten_IsOneInProcessFailure_AndTheRefusalNamesIt()
    {
        var root = _owned.Dir("s3-claim-inproc");
        var variables = new Dictionary<string, string?>();
        var registered = false;
        var seams = new RunTempSeams((name, value) => variables[name] = value, name => variables.GetValueOrDefault(name))
        {
            BeforeClaimWrite = _ => throw new IOException("disk full"),
            RegisterRestore = _ => registered = true,
            Warn = _ => { },
        };

        var act = () => RunTempFolder.Create(root, seams);

        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Contain("could not claim");
        Directory.GetFileSystemEntries(root).Should().BeEmpty("the claim made and the folder not made: nothing is left");
        variables.Should().BeEmpty("no variable was set");
        registered.Should().BeFalse("no restore was registered for a folder that was never made");

        var line = RunTemp.Refusal(new RunTemp.RunState(root, string.Empty, thrown, true), _ => null, Exe);
        line.Should().NotBeNull().And.StartWith(RunTemp.RefusalPrefix).And.Contain("could not claim").And.Contain("disk full", "the cause is in the line").And.Contain(Exe);
        LinesOf(line!).Should().ContainSingle();
    }

    [Fact]
    public void ARunThatLostItsFolder_IsRefused_ByTheTable()
    {
        var root = _owned.Dir("s3-table");
        using var run = OwnedDir.Create("run", root);
        var folder = run.Path;
        string? Environment(string name) => folder;

        RunTemp.Refusal(new RunTemp.RunState(root, folder, null, true), Environment, Exe).Should().BeNull("every check holds");
        foreach (var different in RunTempFolder.Variables)
        {
            var line = RunTemp.Refusal(new RunTemp.RunState(root, folder, null, true), name => name == different ? folder + "-other" : folder, Exe);

            Said(line).Should().StartWith(RunTemp.RefusalPrefix).And.Contain("TEMP, TMP and TMPDIR must all name this run's folder").And.Contain($"{different}='{folder}-other'", $"{different} differs");
        }

        Said(RunTemp.Refusal(new RunTemp.RunState(root, folder, null, true), name => name == "TMP" ? null : folder, Exe)).Should().Contain("TMP=''", "an unset variable is not the folder");
        Said(RunTemp.Refusal(new RunTemp.RunState(root, string.Empty, null, false), Environment, Exe)).Should().Contain("made no run folder");

        var legacyLeaf = Owned.NameBase.Replace(OwnerToken.This + "-", string.Empty, StringComparison.Ordinal) + "run-abcdef";
        var legacy = Directory.CreateDirectory(Path.Combine(root, legacyLeaf)).FullName;
        Said(RunTemp.Refusal(new RunTemp.RunState(root, legacy, null, true), _ => legacy, Exe)).Should().Contain("is not an owner-form run folder", "a legacy leaf has no owner segment");

        var gone = Path.Combine(root, Owned.NameBase + "run-gone00");
        Said(RunTemp.Refusal(new RunTemp.RunState(root, gone, null, true), _ => gone, Exe)).Should().Contain("is gone");

        var withNewlines = RunTemp.Refusal(new RunTemp.RunState(root, string.Empty, new IOException("two\r\nlines"), true), _ => null, Exe);
        LinesOf(withNewlines!).Should().ContainSingle("each line break of a message becomes a space, so the refusal stays ONE line");
    }

    [Fact]
    public void ARunWhoseVariablesAreDropped_AfterTheFolderWasMade_IsRefusedAndLeavesNothing()
    {
        var root = _owned.Dir("s3-drop");
        var env = AllThreeAt(root);
        env[RunTemp.SelfTestVariable] = "drop-variables";

        var result = SuiteChild.Run(SuiteChild.MatchesNothing, env, ChildTimeout);

        result.ExitCode.Should().Be(RunTemp.RefusedExitCode, result.StdErr);
        LinesOf(result.StdErr).Should().ContainSingle().Which.Should().Contain("TEMP, TMP and TMPDIR must all name this run's folder");
        Directory.GetFileSystemEntries(root).Should().BeEmpty("the folder the child made was removed at its exit, though the run was refused");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("--info")]
    public void AnInformationFlag_PassesTheGate_AndRunsInsideTheFoldersOfItsOwn(string flag)
    {
        var root = _owned.Dir("s3-info");
        var result = SuiteChild.Run([flag], AllThreeAt(root), ChildTimeout);
        TestContext.Current.SendDiagnosticMessage("{0}: exit {1}, stdout {2} bytes, stderr {3} bytes", flag, result.ExitCode, result.StdOut.Length, result.StdErr.Length);

        result.TimedOut.Should().BeFalse(result.StdErr);
        result.ExitCode.Should().NotBe(RunTemp.RefusedExitCode, $"an information flag is not refused: {result.StdErr}");
        result.StdErr.Should().NotContain(RunTemp.RefusalPrefix);
        Directory.GetFileSystemEntries(root).Should().BeEmpty("the folder was made inside the root and removed at exit");

        var missing = Path.Combine(root, "missing");
        var without = SuiteChild.Run([flag], AllThreeAt(missing), ChildTimeout);
        TestContext.Current.SendDiagnosticMessage("{0} with an unwritable root: exit {1}", flag, without.ExitCode);

        without.ExitCode.Should().NotBe(RunTemp.RefusedExitCode, $"still not refused with no folder: {without.StdErr}");
        Directory.GetFileSystemEntries(root).Should().BeEmpty("nothing was created");
    }

    [Fact]
    public void ListTests_IsGated_AndItsDiscoveryNeverRuns()
    {
        var root = _owned.Dir("s3-list");
        var sentinel = Path.Combine(root, "sentinel");
        var env = AllThreeAt(Path.Combine(root, "missing"));
        env[RunTempChildren.SentinelVariable] = sentinel;

        var result = SuiteChild.Run(["--list-tests"], env, ChildTimeout);

        result.TimedOut.Should().BeFalse(result.StdErr);
        result.ExitCode.Should().Be(RunTemp.RefusedExitCode, $"stdout: {result.StdOut.Length} bytes / stderr: {result.StdErr}");
        LinesOf(result.StdErr).Should().ContainSingle().Which.Should().StartWith(RunTemp.RefusalPrefix);
        File.Exists(sentinel).Should().BeFalse("discovery runs every MemberData, and the gate is before it");
    }

    [Fact]
    public void TheFallback_RefusesWhenTheGateDidNotRun_InProcess()
    {
        var notRun = () => new RunTempFixture(isActive: () => false, refusal: () => null, Exe);
        notRun.Should().Throw<InvalidOperationException>().Which.Message.Should().StartWith(RunTemp.RefusalPrefix).And.Contain("gated entry point did not run").And.Contain(Exe);

        var line = RunTemp.RefusalPrefix + "a refusal line";
        var refused = () => new RunTempFixture(isActive: () => false, refusal: () => line, Exe);
        refused.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(line, "the fixture says what the gate would have said");

        var passed = () => new RunTempFixture(isActive: () => true, refusal: () => null, Exe);
        passed.Should().NotThrow();
    }

    [Fact]
    public void TheFallback_RefusesAHostThatBypassedTheGate_AndNoTestBodyRuns()
    {
        var root = _owned.Dir("s3-bypass");
        var sentinel = Path.Combine(root, "sentinel");
        var env = AllThreeAt(root);
        env[RunTemp.SelfTestVariable] = "bypass-gate";
        env[RunTempChildren.SentinelVariable] = sentinel;

        var result = SuiteChild.Run(["--filter-class", "*GateBypassedSentinel"], env, ChildTimeout);
        TestContext.Current.SendDiagnosticMessage("bypass-gate: exit {0}", result.ExitCode);

        result.TimedOut.Should().BeFalse(result.StdErr);
        result.ExitCode.Should().NotBe(0, "the failed fixture fails the run");
        (result.StdOut + result.StdErr).Should().Contain(RunTemp.RefusalPrefix + "the gated entry point did not run");
        // Discovery DOES run (the gate was bypassed), so the discovery line may be there; a test body must not have run.
        (File.Exists(sentinel) ? File.ReadAllText(sentinel) : string.Empty).Should().NotContain("test body", "no test body ran: the fixture failed first");
    }

    [Fact]
    public void AKilledRun_IsCollected_ByTheSweep()
    {
        var root = _owned.Dir("s3-killed");
        var env = AllThreeAt(root);
        env[RunTempChildren.KilledVariable] = "1";

        var result = SuiteChild.Run(["--filter-class", "*KilledRunChild"], env, ChildTimeout);

        result.TimedOut.Should().BeFalse(result.StdErr);
        result.ExitCode.Should().NotBe(0, "the child was killed on purpose");
        var entries = Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        entries.Should().HaveCount(2, "a killed run leaves exactly its folder and its claim");
        var leaf = entries.Single(e => !e!.EndsWith(".owner", StringComparison.Ordinal))!;
        entries.Should().Equal(leaf, leaf + ".owner");
        var claim = Path.Combine(root, leaf + ".owner");
        var owner = Owner.Parse(File.ReadAllText(claim));
        owner.Should().NotBeNull("the claim is in the owner form");
        owner!.Pid.Should().Be(result.Pid);
        if (OperatingSystem.IsWindows())
        {
            using var windows = new WindowsOwnership();
            windows.OwnedByCaller(Path.Combine(root, leaf));
            windows.OwnedByCaller(claim);
        }

        var later = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(11);
        var alive = LeftoversSweep.Run(new SweepOptions(root, later, LeftoversSweep.MinAge, _ => new Probe(ProbeState.Live, owner.Start)));
        alive.Removed.Should().BeEmpty("a live process keeps what it made");
        alive.Preserved.Should().Contain(p => p.Reason == "live");
        Directory.GetFileSystemEntries(root).Should().HaveCount(2);

        var dead = LeftoversSweep.Run(new SweepOptions(root, later, LeftoversSweep.MinAge, pid => pid == result.Pid ? new Probe(ProbeState.Dead, null) : Liveness.ProbeProcess(pid)));
        dead.Removed.Should().ContainSingle().Which.Should().EndWith(leaf, "the sweep reports the folder; its claim goes with it");
        Directory.GetFileSystemEntries(root).Should().BeEmpty();
    }
}
