using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s4 t5 (R6, R7, R8): the run summary of a failed test step, apart from the bounded digest: the tool's counts, the name of EVERY failure, the passed names when the tool prints them, and whether the list is complete. Section F (the re-run) reads this, never the digest.
/// </summary>
public sealed class ARunSummaryIsCompleteOrSaysSoTests
{
    private static RunSummary Summary(string recording, string adapter, string runner, bool logCut = false)
    {
        var digest = FailureDigest.Of(CheckStep.Test, adapter, DigestFixtures.Read(recording), runner: runner, logCut: logCut);

        return digest.Summary.Should().NotBeNull($"{recording} has a reader with a summary").And.Subject.As<RunSummary>();
    }

    [Theory]
    [InlineData("dotnet-vstest-failed.txt", "dotnet", "vstest", "vstest", 4, 2, 2)]
    [InlineData("dotnet-mtp-global-failed.txt", "dotnet", "mtp", "mtp", 4, 2, 2)]
    [InlineData("mtp-three-failed.txt", "dotnet", "mtp", "mtp", -1, -1, -1)]
    [InlineData("node-node-test-three-tests.txt", "node", "node-test", "node-test", 4, 2, 2)]
    public void R6_EveryRecording_GivesTheCountsAndTheNameOfEveryFailure_AndIsComplete(string recording, string adapter, string runner, string tool, int total, int failed, int passed)
    {
        var summary = Summary(recording, adapter, runner);

        summary.Tool.Should().Be(tool);
        summary.SummaryFound.Should().BeTrue();
        summary.Complete.Should().BeTrue(summary.ToString());
        summary.Failures.Count.Should().Be(summary.Failed);
        if (total >= 0)
        {
            (summary.Total, summary.Failed, summary.Passed).Should().Be((total, failed, passed));
        }
    }

    [Fact]
    public void R6_TheVsTestRecording_NamesTheFailuresAndThePassedTestsOfADetailedRun()
    {
        var summary = Summary("dotnet-vstest-detailed.txt", "dotnet", "vstest");

        summary.Failures.Should().Equal("Scratch.SampleTests.FailsOnTheValue", "Scratch.SampleTests.FailsByThrowing");
        summary.PassedNames.Should().BeEquivalentTo("Scratch.OtherTests.AlsoPasses", "Scratch.SampleTests.PassesAlways");
        summary.Complete.Should().BeTrue();
    }

    [Fact]
    public void R6_JestAndVitest_CountTheTests_ButAreNotCompleteWhileASuiteFailedToRun()
    {
        var jest = Summary("node-jest-three-tests.txt", "node", "jest");
        var vitest = Summary("node-vitest-three-tests.txt", "node", "vitest");

        (jest.Total, jest.Failed, jest.Passed).Should().Be((3, 2, 1));
        jest.Failures.Should().Equal("fails on the value", "fails by throwing");
        jest.Complete.Should().BeFalse("'Test suite failed to run' is a failure the summary cannot name: no re-run may be decided from it");
        (vitest.Total, vitest.Failed, vitest.Passed).Should().Be((3, 2, 1));
        vitest.Failures.Should().Equal("sum.test.js > fails on the value", "sum.test.js > fails by throwing");
        vitest.Complete.Should().BeFalse();
    }

    [Fact]
    public void R6_TheVerboseNodeTestAndVitestRecordings_NameThePassedTests()
    {
        Summary("node-node-test-verbose.txt", "node", "node-test").PassedNames.Should().Contain("passes always");
        Summary("node-vitest-verbose.txt", "node", "vitest").PassedNames.Should().Contain("sum.test.js > passes always");
        Summary("node-jest-verbose.txt", "node", "jest").PassedNames.Should().Contain("passes always");
    }

    [Fact]
    public void R7_NotComplete_TwoNamedFailuresBesideOneTheReaderCannotName()
    {
        var text = "VSTest version 18.0.2 (x64)\n  Failed A.One [1 ms]\n  Error Message:\n   x\n  Failed A.Two [1 ms]\n  Error Message:\n   y\n\nFailed!  - Failed:     3, Passed:     0, Skipped:     0, Total:     3, Duration: 1 ms - A.dll (net10.0)\n";

        var summary = FailureDigest.Of(CheckStep.Test, "dotnet", text, runner: "vstest").Summary!;

        (summary.Failed, summary.Failures.Count, summary.Complete).Should().Be((3, 2, false));
    }

    [Fact]
    public void R7_NotComplete_NoSummaryLine()
    {
        var text = "failed A.One (3ms)\n  boom\n    at A.One() in /work/A.cs:3\n";

        var summary = FailureDigest.Of(CheckStep.Test, "dotnet", text, runner: "mtp").Summary!;

        (summary.SummaryFound, summary.Complete).Should().Be((false, false));
    }

    [Fact]
    public void R7_NotComplete_AStepLogCutAtItsLimit()
    {
        Summary("dotnet-vstest-failed.txt", "dotnet", "vstest", logCut: true).Complete.Should().BeFalse();
        Summary("node-node-test-three-tests.txt", "node", "node-test", logCut: true).Complete.Should().BeFalse();
    }

    [Fact]
    public void R8_FortyFiveFailures_GiveADigestOfFortyThatSaysItWasCut_AndASummaryOfFortyFiveNamesThatIsComplete()
    {
        var failures = string.Concat(Enumerable.Range(1, 45).Select(n => $"failed A.Test{n} ({n}ms)\n  boom {n}\n    at A.Test{n}() in /work/A.cs:{n}\n"));
        var text = failures + "\nTest run summary: Failed!\n  total: 50\n  failed: 45\n  succeeded: 5\n  skipped: 0\n  duration: 1s\n";

        var digest = FailureDigest.Of(CheckStep.Test, "dotnet", text, runner: "mtp");

        digest.Items.Should().HaveCount(40);
        digest.Cut.Should().BeTrue();
        digest.Found.Should().Be(45);
        digest.Summary!.Failures.Should().HaveCount(45);
        digest.Summary.Complete.Should().BeTrue();
    }

    [Fact]
    public void AboveTheHardCapOfNames_TheSummaryIsNotComplete_AndNeverThrows()
    {
        var failures = string.Concat(Enumerable.Range(1, RunSummary.MaxNames + 5).Select(n => $"failed A.Test{n} (1ms)\n  x\n"));
        var text = failures + "\nTest run summary: Failed!\n  total: 9000\n  failed: " + (RunSummary.MaxNames + 5) + "\n  succeeded: 0\n";

        var summary = FailureDigest.Of(CheckStep.Test, "dotnet", text, runner: "mtp").Summary!;

        summary.Failures.Should().HaveCount(RunSummary.MaxNames);
        summary.Complete.Should().BeFalse();
    }

    [Fact]
    public void ARecordingOfANonTestStepOrNoKnownTool_HasNoSummary()
    {
        FailureDigest.Of(CheckStep.Build, "dotnet", DigestFixtures.Read("msbuild-two-errors.txt")).Summary.Should().BeNull();
        FailureDigest.Of(CheckStep.Test, "dotnet", "nothing a tool printed\n").Summary.Should().BeNull();
    }
}
