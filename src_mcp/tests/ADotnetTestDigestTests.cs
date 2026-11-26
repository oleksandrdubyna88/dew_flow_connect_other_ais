using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s4 t1 (R1, R3): the readers of the console of <c>dotnet test</c> (VSTest, and the Microsoft Testing Platform form through the reader E2 has) and of the errors of a failed <c>dotnet restore</c>, on the recordings of story s1. The recordings are real output of the real tools.
/// </summary>
public sealed class ADotnetTestDigestTests
{
    private const string Tree = "/work/c/coai-coder-m1";

    [Fact]
    public void R1_TheRecordedVsTestConsole_GivesOneItemPerFailure_WithTheNameTheLocationAndTheMessage()
    {
        var digest = FailureDigest.Of(CheckStep.Test, "dotnet", DigestFixtures.Read("dotnet-vstest-failed.txt"), Tree, "vstest");

        digest.Kind.Should().Be(DigestKind.VsTest);
        digest.Items.Select(item => (item.Step, item.File, item.Line, item.Code)).Should().Equal(
            ("test", "vstest/SampleTests.cs", 11, "Scratch.SampleTests.FailsOnTheValue"),
            ("test", "vstest/SampleTests.cs", 14, "Scratch.SampleTests.FailsByThrowing"));
        digest.Items[0].Message.Should().Be("Assert.Equal() Failure: Values differ\nExpected: 3\nActual:   2");
        digest.Items[1].Message.Should().Be("System.InvalidOperationException : boom from the scratch test");
        digest.Cut.Should().BeFalse();
    }

    [Fact]
    public void TheDetailedVsTestConsole_StillGivesTheFailures_NotThePassedTestsOrTheAdaptersLines()
    {
        var digest = DigestFixtures.Digest("dotnet-vstest-detailed.txt", CheckStep.Test, "dotnet", "vstest");

        digest.Items.Select(item => item.Code).Should().Equal("Scratch.SampleTests.FailsOnTheValue", "Scratch.SampleTests.FailsByThrowing");
    }

    [Fact]
    public void ATheoryCaseOfTheVsTestConsole_IsOneItemWithTheArgumentsInItsName()
    {
        var text = "VSTest version 18.0.2 (x64)\n\nStarting test execution, please wait...\n  Failed Scratch.T.Sum(a: 1, b: \"x\") [2 ms]\n  Error Message:\n   boom\n  Stack Trace:\n     at Scratch.T.Sum(Int32 a, String b) in /work/T.cs:line 7\n\nFailed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 3 ms - Scratch.dll (net10.0)\n";

        var item = FailureDigest.Of(CheckStep.Test, "dotnet", text).Items.Single();

        (item.Code, item.File, item.Line, item.Message).Should().Be(("Scratch.T.Sum(a: 1, b: \"x\")", "/work/T.cs", 7, "boom"));
    }

    [Theory]
    [InlineData("dotnet-mtp-global-failed.txt")]
    public void TheRecordedMtpConsoleOfDotnetTest_GivesTheSameThroughTheReaderOfE2_WithoutItsFromLine(string recording)
    {
        var digest = DigestFixtures.Digest(recording, CheckStep.Test, "dotnet", "mtp");

        digest.Kind.Should().Be(DigestKind.TestExecutable);
        digest.Items.Select(item => item.Code).Should().BeEquivalentTo("Scratch.SampleTests.FailsOnTheValue", "Scratch.SampleTests.FailsByThrowing");
        digest.Items.Should().OnlyContain(item => !item.Message.StartsWith("from ", StringComparison.Ordinal));
        digest.Items.Single(item => item.Code.EndsWith("FailsOnTheValue", StringComparison.Ordinal)).Message.Should().Contain("Expected: 3");
    }

    [Fact]
    public void TheRecordedFailedRestore_GivesOneItemPerError_WithItsCode()
    {
        var digest = FailureDigest.Of(CheckStep.Restore, "dotnet", DigestFixtures.Read("dotnet-restore-error.txt"), Tree);

        digest.Kind.Should().Be(DigestKind.MsBuild);
        var item = digest.Items.Should().ContainSingle().Subject;
        (item.Step, item.Code, item.File).Should().Be(("restore", "NU1101", "badrestore/Scratch.Bad.csproj"));
        item.Message.Should().StartWith("Unable to find package Coai.Scratch.NoSuchPackage.Anywhere");
    }

    [Fact]
    public void R3_AMebibyteOfRandomLines_NeverThrows_AndGivesNoItem()
    {
        var random = new Random(20261126);
        var text = string.Join('\n', Enumerable.Range(0, 20000).Select(_ => new string([.. Enumerable.Range(0, random.Next(0, 80)).Select(_ => (char)random.Next(32, 0x2FFF))])));

        foreach (var (step, adapter) in new[] { (CheckStep.Test, "dotnet"), (CheckStep.Restore, "dotnet"), (CheckStep.Build, "dotnet") })
        {
            var digest = FailureDigest.Of(step, adapter, text);

            digest.Kind.Should().Be(DigestKind.RawTail, "random lines are no tool's output: the raw tail of E2 is the digest");
        }
    }

    [Fact]
    public void ALineOver4096Characters_IsSkipped_NeverRead()
    {
        var longLine = "  Failed Scratch.Long.Name " + new string('x', 5000) + " [3 ms]";
        var text = "VSTest version 18.0.2 (x64)\n" + longLine + "\n";

        FailureDigest.Of(CheckStep.Test, "dotnet", text).Kind.Should().Be(DigestKind.RawTail);
    }
}
