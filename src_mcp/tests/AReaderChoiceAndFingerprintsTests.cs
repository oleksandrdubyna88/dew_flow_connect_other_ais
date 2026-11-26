using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s4 t4 (R4, and the choice of a reader): the reader is chosen by adapter, step name and runner fact; a step of the file (no runner fact) tries the adapter's readers in one fixed order and the first that gives an item wins; no item from any reader gives the raw tail, so a digest is never empty;
/// every item has a fingerprint that does not hold the line number.
/// </summary>
public sealed class AReaderChoiceAndFingerprintsTests
{
    [Theory]
    [InlineData("dotnet-vstest-failed.txt", "dotnet", CheckStep.Test, "vstest", DigestKind.VsTest)]
    [InlineData("dotnet-vstest-failed.txt", "dotnet", CheckStep.Test, "", DigestKind.VsTest)]
    [InlineData("dotnet-mtp-global-failed.txt", "dotnet", CheckStep.Test, "mtp", DigestKind.TestExecutable)]
    [InlineData("dotnet-mtp-global-failed.txt", "dotnet", CheckStep.Test, "", DigestKind.TestExecutable)]
    [InlineData("msbuild-two-errors.txt", "dotnet", CheckStep.Build, "", DigestKind.MsBuild)]
    [InlineData("dotnet-restore-error.txt", "dotnet", CheckStep.Restore, "", DigestKind.MsBuild)]
    [InlineData("node-tsc-two-errors.txt", "node", CheckStep.Typecheck, "", DigestKind.Tsc)]
    [InlineData("node-npm-run-build-two-errors.txt", "node", CheckStep.Build, "", DigestKind.Tsc)]
    [InlineData("node-npm-ci-error.txt", "node", CheckStep.Restore, "", DigestKind.Npm)]
    [InlineData("node-jest-three-tests.txt", "node", CheckStep.Test, "jest", DigestKind.Jest)]
    [InlineData("node-vitest-three-tests.txt", "node", CheckStep.Test, "", DigestKind.Vitest)]
    [InlineData("node-node-test-three-tests.txt", "node", CheckStep.Test, "node-test", DigestKind.NodeTest)]
    public void TheReader_IsChosenByTheAdapterTheStepAndTheRunnerFact(string recording, string adapter, CheckStep step, string runner, DigestKind expected)
    {
        DigestFixtures.Digest(recording, step, adapter, runner).Kind.Should().Be(expected);
    }

    [Fact]
    public void AReaderOfAnotherAdapterOrStep_ReadsNothing_TheRawTailIsTheDigest_NeverEmpty()
    {
        foreach (var (recording, step, adapter) in new[]
        {
            ("node-jest-three-tests.txt", CheckStep.Test, "dotnet"), ("dotnet-vstest-failed.txt", CheckStep.Test, "node"), ("node-tsc-two-errors.txt", CheckStep.Restore, "node"),
            ("msbuild-two-errors.txt", CheckStep.Typecheck, "node"), ("node-npm-ci-error.txt", CheckStep.Build, "dotnet"), ("node-jest-three-tests.txt", CheckStep.Test, "other"),
        })
        {
            var digest = DigestFixtures.Digest(recording, step, adapter);

            digest.Kind.Should().Be(DigestKind.RawTail, $"{recording} as a {adapter} {step} step");
            digest.Items.Should().ContainSingle().Which.Message.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void EveryRecording_GoesThroughEveryReader_AndNoRecordingIsReadByAToolThatIsNotItsOwn()
    {
        // R2 for the whole family: a recording that a reader reads must be one of the tool's own.
        var own = new Dictionary<string, Func<string, bool>>(StringComparer.Ordinal)
        {
            ["vstest"] = name => name.StartsWith("dotnet-vstest", StringComparison.Ordinal),
            ["tsc"] = name => name.Contains("tsc", StringComparison.Ordinal) || name.Contains("typecheck", StringComparison.Ordinal) || name.Contains("run-build", StringComparison.Ordinal),
            ["npm"] = name => name.Contains("npm-ci", StringComparison.Ordinal),
            ["jest"] = name => name.StartsWith("node-jest", StringComparison.Ordinal),
            ["vitest"] = name => name.StartsWith("node-vitest", StringComparison.Ordinal),
            ["node-test"] = name => name.StartsWith("node-node-test", StringComparison.Ordinal),
        };

        foreach (var (recording, text) in DigestFixtures.All())
        {
            foreach (var (reader, isOwn) in own)
            {
                var items = DigestFixtures.Readers[reader]("test", text);
                if (items.Count > 0)
                {
                    isOwn(recording).Should().BeTrue($"{reader} read {recording}, which is another tool's output");
                }
            }
        }
    }

    [Fact]
    public void ThePre_Existing_LimitsHold_ForEveryReader_FortyItemsAndSixteenKilobytes()
    {
        var text = string.Concat(Enumerable.Range(1, 60).Select(n => $"src/a.ts({n},1): error TS2322: the message number {n} {new string('x', 700)}\n"));

        var digest = FailureDigest.Of(CheckStep.Build, "node", text);

        digest.Kind.Should().Be(DigestKind.Tsc);
        digest.Items.Count.Should().BeLessThanOrEqualTo(FailureDigest.MaxItems);
        digest.Cut.Should().BeTrue();
        System.Text.Encoding.UTF8.GetByteCount(FailureDigest.Json(digest.Items)).Should().BeLessThanOrEqualTo(FailureDigest.MaxBytes);
    }

    [Fact]
    public void R4_EveryItemHasAFingerprint_OfSixtyFourHexCharacters_AndItIsStable()
    {
        var digest = DigestFixtures.Digest("dotnet-vstest-failed.txt", CheckStep.Test, "dotnet", "vstest");

        digest.Items.Select(item => item.Fingerprint).Should().OnlyContain(print => print.Length == 64 && print.All(c => char.IsAsciiHexDigitLower(c))).And.OnlyHaveUniqueItems();
        digest.Items[0].Fingerprint.Should().Be(DigestFixtures.Digest("dotnet-vstest-failed.txt", CheckStep.Test, "dotnet", "vstest").Items[0].Fingerprint);
    }

    [Fact]
    public void R4_TheSameFailureWithALineAddedAboveIt_HasTheSameFingerprint_ForEveryReader()
    {
        string Shift(string text) => text.Replace(":line 11", ":line 12", StringComparison.Ordinal).Replace(":line 14", ":line 16", StringComparison.Ordinal);
        var before = DigestFixtures.Read("dotnet-vstest-failed.txt");
        var after = "A new first line the tool printed\n" + Shift(before);

        FailureDigest.Of(CheckStep.Test, "dotnet", after, runner: "vstest").Items.Select(item => (item.Line, item.Fingerprint)).Should().NotEqual(FailureDigest.Of(CheckStep.Test, "dotnet", before, runner: "vstest").Items.Select(item => (item.Line, item.Fingerprint)), "the lines moved");
        FailureDigest.Of(CheckStep.Test, "dotnet", after, runner: "vstest").Items.Select(item => item.Fingerprint).Should().Equal(FailureDigest.Of(CheckStep.Test, "dotnet", before, runner: "vstest").Items.Select(item => item.Fingerprint));

        var msbuildBefore = "src/File.cs(5,3): error CS0103: The name 'x' does not exist in the current context [/w/App.csproj]\n";
        var msbuildAfter = "src/File.cs(9,3): error CS0103: The name 'x' does not exist in the current context [/w/App.csproj]\n";
        FailureDigest.Of(CheckStep.Build, "dotnet", msbuildAfter).Items[0].Fingerprint.Should().Be(FailureDigest.Of(CheckStep.Build, "dotnet", msbuildBefore).Items[0].Fingerprint);
        FailureDigest.Of(CheckStep.Build, "node", "src/a.ts(4,1): error TS2322: Type 'string' is not assignable to type 'number'.\n").Items[0].Fingerprint
            .Should().Be(FailureDigest.Of(CheckStep.Build, "node", "src/a.ts(40,1): error TS2322: Type 'string' is not assignable to type 'number'.\n").Items[0].Fingerprint);
    }

    [Fact]
    public void ADigitRunAndWhiteSpaceInTheMessage_AreMadeEqual_ButAnotherFileCodeOrStepIsAnotherFailure()
    {
        var a = new DigestItem("test", "a.cs", 3, "T.One", "expected 12  but found\t13");
        var b = new DigestItem("test", "a.cs", 99, "T.One", "expected 7 but found 8");

        a.Fingerprint.Should().Be(b.Fingerprint);
        (a with { File = "b.cs" }).Fingerprint.Should().NotBe(a.Fingerprint);
        (a with { Code = "T.Two" }).Fingerprint.Should().NotBe(a.Fingerprint);
        (a with { Step = "build" }).Fingerprint.Should().NotBe(a.Fingerprint);
        (a with { Message = "expected 12 but not found" }).Fingerprint.Should().NotBe(a.Fingerprint);
    }
}
