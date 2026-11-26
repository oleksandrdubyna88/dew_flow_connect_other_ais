using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Epoch 3 E4 s4 t2 (R1, R3): the readers of the error lines of <c>tsc</c> and of the error block of a failed <c>npm ci</c>, on the recordings of story s1. Pure, bounded as the readers of t1, redacted before any cut.</summary>
public sealed class ANodeBuildDigestTests
{
    [Theory]
    [InlineData("node-tsc-two-errors.txt", CheckStep.Typecheck)]
    [InlineData("node-npm-run-typecheck-two-errors.txt", CheckStep.Typecheck)]
    [InlineData("node-npm-run-build-two-errors.txt", CheckStep.Build)]
    public void R1_TheRecordedTscOutput_GivesTwoItems_WithFileLineCodeAndMessage(string recording, CheckStep step)
    {
        var digest = DigestFixtures.Digest(recording, step, "node");

        digest.Kind.Should().Be(DigestKind.Tsc);
        digest.Items.Select(item => (item.File, item.Line, item.Code)).Should().Equal(("src/index.ts", 1, "TS2322"), ("src/index.ts", 3, "TS2345"));
        digest.Items[0].Message.Should().Be("Type 'string' is not assignable to type 'number'.");
        digest.Items.Should().OnlyContain(item => item.Step == step.Wire());
    }

    [Fact]
    public void R1_TheRecordedFailedNpmCi_GivesOneItem_WithNpmsCodeAndTheFirstLinesOfItsMessage_NotTheUsageText()
    {
        var digest = DigestFixtures.Digest("node-npm-ci-error.txt", CheckStep.Restore, "node");

        digest.Kind.Should().Be(DigestKind.Npm);
        var item = digest.Items.Should().ContainSingle().Subject;
        item.Code.Should().Be("EUSAGE");
        item.Message.Should().Contain("Missing: is-odd@3.0.1 from lock file").And.Contain("Missing: is-number@6.0.0 from lock file").And.NotContain("Usage").And.NotContain("--install-strategy");
    }

    [Fact]
    public void TheRecordedNpmCiWithNoNetwork_IsOneItemWithItsCodeAndTheReason()
    {
        var item = DigestFixtures.Digest("node-npm-ci-no-network.txt", CheckStep.Restore, "node").Items.Should().ContainSingle().Subject;

        item.Code.Should().Be("EAI_AGAIN");
        item.Message.Should().Contain("getaddrinfo EAI_AGAIN registry.npmjs.org");
    }

    [Fact]
    public void ATscLineIsNotAnMsBuildLine_AndAnMsBuildLineIsNotATscLine()
    {
        var tsc = DigestFixtures.Read("node-tsc-two-errors.txt");
        var msbuild = "src/File.cs(3,5): error CS0103: The name 'x' does not exist [/work/App.csproj]\n";

        FailureDigest.Of(CheckStep.Build, "dotnet", tsc).Kind.Should().Be(DigestKind.RawTail, "a dotnet build reads no TS code");
        FailureDigest.Of(CheckStep.Build, "node", msbuild).Kind.Should().Be(DigestKind.RawTail, "a node build reads no CS code");
    }

    [Fact]
    public void TheRedactionPass_RunsBeforeAnyCut_ForTheTscAndNpmReaders()
    {
        var secret = "ghp_0123456789abcdef0123456789abcdef0123";
        var tsc = FailureDigest.Of(CheckStep.Build, "node", $"src/a.ts(1,1): error TS2322: the value token={secret} is wrong\n");
        var npm = FailureDigest.Of(CheckStep.Restore, "node", $"npm error code E401\nnpm error auth token={secret} was refused\n");

        foreach (var digest in new[] { tsc, npm })
        {
            FailureDigest.Json(digest.Items).Should().NotContain(secret).And.Contain("[redacted]");
        }
    }

    [Fact]
    public void R3_AMebibyteOfRandomLines_NeverThrows_AndGivesNoItem()
    {
        var random = new Random(20261127);
        var text = string.Join('\n', Enumerable.Range(0, 20000).Select(_ => new string([.. Enumerable.Range(0, random.Next(0, 80)).Select(_ => (char)random.Next(32, 0x2FFF))])));

        foreach (var step in new[] { CheckStep.Build, CheckStep.Typecheck, CheckStep.Restore })
        {
            FailureDigest.Of(step, "node", text).Kind.Should().Be(DigestKind.RawTail);
        }
    }
}
