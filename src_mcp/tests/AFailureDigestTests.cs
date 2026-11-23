using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Runners.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The failure digest of a failed step (epoch 3 E2 s1 t3): MSBuild error lines and the test executable's failed tests, each as <c>{step, file, line, code, message}</c>, at most 40 items and 16 KB, with the last
/// lines of the raw output when no reader knows it, and credential-like text redacted by the product's own redaction.
/// </summary>
/// <remarks>
/// The two main fixtures are REAL recorded output of this repository on 2026-11-23 (<c>fixtures/toolchain/</c>): <c>dotnet build -m:4</c> of the core project with two deliberate compile errors in a scratch file, and the
/// <c>CoaiMcp.Tests</c> executable run on a scratch class of one passing test, two failing ones and a theory with one failing case. Two edits were made to the recordings, for what a repository should not carry:
/// the checkout path <c>/home/jinx/git/coai-v2-ep3e2</c> became <c>/home/dev/coai</c> and the line naming the run's temporary folder was dropped. The scratch files were never committed.
/// </remarks>
public sealed class AFailureDigestTests
{
    private const string Tree = "/home/dev/coai";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "toolchain", name));

    private static FailureDigestResult Build(string output, string adapter = "dotnet") => FailureDigest.Of(CheckStep.Build, adapter, output, Tree);

    private static FailureDigestResult Test(string output, string adapter = "dotnet") => FailureDigest.Of(CheckStep.Test, adapter, output, Tree);

    private static int JsonBytes(FailureDigestResult digest) => Encoding.UTF8.GetByteCount(FailureDigest.Json(digest.Items));

    private static string Error(int number, string message = "something is wrong") => $"{Tree}/src/File{number}.cs({number},5): error CS{1000 + number}: {message} [{Tree}/src/App.csproj]";

    [Fact]
    public void TheRecordedMsBuildOutputWithTwoErrors_GivesTwoItems_NotFour()
    {
        var digest = Build(Fixture("msbuild-two-errors.txt"));

        digest.Kind.Should().Be(DigestKind.MsBuild);
        digest.Items.Should().HaveCount(2, "MSBuild prints each error twice, inline and in the summary, and the digest names each failure once");
        digest.Items[0].Should().Be(new DigestItem("build", "src_mcp/core/Toolchain/ZzBroken.cs", 5, "CS0029", "Cannot implicitly convert type 'string' to 'int'"));
        digest.Items[1].Should().Be(new DigestItem("build", "src_mcp/core/Toolchain/ZzBroken.cs", 7, "CS0103", "The name 'MissingType' does not exist in the current context"));
        digest.Found.Should().Be(2);
        digest.Cut.Should().BeFalse();
    }

    [Fact]
    public void AnErrorWithNoFile_IsAnItemWithNoFileAndNoLine()
    {
        var digest = Build(Fixture("msbuild-no-project.txt"));

        digest.Kind.Should().Be(DigestKind.MsBuild);
        digest.Items.Should().ContainSingle().Which.Should().Be(new DigestItem("build", string.Empty, 0, "MSB1009", "Project file does not exist."));
    }

    [Fact]
    public void AProjectLevelError_NamesTheProjectFileItCameFrom()
    {
        var digest = Build($"{Tree}/src/App.csproj : error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org");

        digest.Items.Should().ContainSingle().Which.Should().Be(
            new DigestItem("build", "src/App.csproj", 0, "NU1101", "Unable to find package Foo. No packages exist with this id in source(s): nuget.org"));
    }

    [Fact]
    public void TheRecordedFailedTestRun_GivesOneItemPerFailedTest_WithTheTestsNameLocationAndMessage()
    {
        var digest = Test(Fixture("mtp-three-failed.txt"));

        digest.Kind.Should().Be(DigestKind.TestExecutable);
        digest.Items.Should().HaveCount(3, "one item per failed test: the theory's failing case counts, its passing case and the passing fact do not");
        digest.Items.Select(item => item.Code).Should().Equal(
            "CoaiMcp.Tests.ZzFailingRecording.ATheoryWithOneBadCase(value: 2)",
            "CoaiMcp.Tests.ZzFailingRecording.TheSecondFailure",
            "CoaiMcp.Tests.ZzFailingRecording.TheFirstFailure");
        digest.Items.Should().OnlyContain(item => item.Step == "test" && item.File == "src_mcp/tests/ZzFailingRecording.cs");
        digest.Items.Select(item => item.Line).Should().Equal(24, 18, 12);
        digest.Items[0].Message.Should().Be("Expected value to be 1, but found 2.");
        digest.Items[1].Message.Should().Be("Expected list to be equal to {1, 2, 4}, but {1, 2, 3} differs at index 2.");
        digest.Items[2].Message.Should().StartWith("Expected string to be \"beta\"").And.Contain("because the recording needs a reason");
        digest.Items.Should().OnlyContain(item => !item.Message.Contains("   at ", StringComparison.Ordinal), "the stack is not the message");
    }

    [Fact]
    public void ARunWithWindowsLineEndings_GivesTheSameItems()
    {
        Build(Fixture("msbuild-two-errors.txt").ReplaceLineEndings("\r\n")).Items.Should().BeEquivalentTo(Build(Fixture("msbuild-two-errors.txt")).Items);
        Test(Fixture("mtp-three-failed.txt").ReplaceLineEndings("\r\n")).Items.Should().BeEquivalentTo(Test(Fixture("mtp-three-failed.txt")).Items);
    }

    [Fact]
    public void AnOutputNoReaderKnows_GivesOneItemWithTheLastLinesOfTheRawOutput()
    {
        var output = string.Join('\n', Enumerable.Range(1, 200).Select(number => $"noise line {number}"));

        var digest = Build(output);

        digest.Kind.Should().Be(DigestKind.RawTail);
        var item = digest.Items.Should().ContainSingle().Subject;
        item.Should().BeEquivalentTo(item with { Step = "build", File = string.Empty, Line = 0, Code = string.Empty });
        item.Message.Should().EndWith("noise line 200").And.Contain("noise line 171").And.NotContain("noise line 170\n", "30 lines are kept, the last 30");
    }

    [Theory]
    [InlineData("node")]
    [InlineData("rust")]
    public void AnotherAdapter_DoesNotUseTheMsBuildReader_ItsOutputIsRawEvenWhenItLooksLikeOne(string adapter)
    {
        var digest = Build(Error(1), adapter);

        digest.Kind.Should().Be(DigestKind.RawTail);
        digest.Items.Should().ContainSingle().Which.Message.Should().Contain("CS1001");
    }

    [Fact]
    public void ABuildWhoseOutputHasNoErrorLine_FallsBackToTheRawTail_NeverToAnEmptyDigest()
    {
        var digest = Build("Build started\nsomething broke but not in MSBuild words\nBuild FAILED.\n");

        digest.Kind.Should().Be(DigestKind.RawTail);
        digest.Items.Should().ContainSingle().Which.Message.Should().Contain("something broke").And.Contain("Build FAILED.");
    }

    [Fact]
    public void AStepThatWroteNothing_IsOneItemSayingSo()
    {
        var item = Test(string.Empty).Items.Should().ContainSingle().Subject;

        item.Message.Should().Contain("no output");
        item.Step.Should().Be("test");
    }

    [Fact]
    public void AHundredErrors_GiveFortyItems_AndSayHowManyWereFoundAndThatItWasCut()
    {
        var digest = Build(string.Join('\n', Enumerable.Range(1, 100).Select(number => Error(number))));

        digest.Items.Should().HaveCount(FailureDigest.MaxItems);
        digest.Found.Should().Be(100);
        digest.Cut.Should().BeTrue();
        digest.Items[0].Code.Should().Be("CS1001", "the first failures are kept, in the order the output had them");
        digest.Items[^1].Code.Should().Be($"CS{1000 + FailureDigest.MaxItems}");
    }

    [Fact]
    public void FortyLongErrors_AreCutToSixteenKilobytes_AsJson()
    {
        var digest = Build(string.Join('\n', Enumerable.Range(1, 40).Select(number => Error(number, new string('m', 3000)))));

        JsonBytes(digest).Should().BeLessThanOrEqualTo(FailureDigest.MaxBytes);
        digest.Items.Should().NotBeEmpty().And.HaveCountLessThan(40, "the budget, not the item count, ended it");
        digest.Cut.Should().BeTrue();
        digest.Found.Should().Be(40);
        digest.Items.Should().OnlyContain(item => item.Message.Length <= FailureDigest.MessageChars + 1, "one message is cut to its own limit before the budget is counted");
    }

    [Fact]
    public void ARawTailOfHugeLines_IsWithinSixteenKilobytes_Too()
    {
        var digest = Build(string.Join('\n', Enumerable.Range(1, 60).Select(number => new string('z', 4000) + number)));

        digest.Kind.Should().Be(DigestKind.RawTail);
        JsonBytes(digest).Should().BeLessThanOrEqualTo(FailureDigest.MaxBytes);
        digest.Items.Single().Message.Should().EndWith("60", "the END of the output is what is kept");
    }

    [Theory]
    [InlineData("ghp_0123456789abcdef0123456789abcdef0123")]
    [InlineData("sk-0123456789abcdefghijklmnop")]
    public void CredentialLikeText_IsRedactedByTheProductsOwnRule_InEveryKindOfItem(string secret)
    {
        var msbuild = Build(Error(1, $"the value token={secret} is not allowed"));
        var tests = Test($"failed CoaiMcp.Tests.X.Y (3ms)\n  Expected the key {secret} to be absent\n    at CoaiMcp.Tests.X.Y() in {Tree}/src/X.cs:line 9\n");
        var raw = Build($"first\nthe password is password={secret}\nlast\n", "node");

        foreach (var digest in new[] { msbuild, tests, raw })
        {
            FailureDigest.Json(digest.Items).Should().NotContain(secret).And.Contain("[redacted]");
        }
    }

    [Fact]
    public void TheJsonIsTheArrayTheCoderSessionIsSent_WithExactlyTheFiveKeys()
    {
        var digest = Build(Fixture("msbuild-two-errors.txt"));

        using var document = JsonDocument.Parse(FailureDigest.Json(digest.Items));

        document.RootElement.GetArrayLength().Should().Be(2);
        var first = document.RootElement[0];
        first.EnumerateObject().Select(property => property.Name).Should().Equal("step", "file", "line", "code", "message");
        first.GetProperty("line").GetInt32().Should().Be(5);
        first.GetProperty("code").GetString().Should().Be("CS0029");
    }

    [Fact]
    public void WithNoTreeGiven_AFilePathIsKeptAsTheOutputWroteIt()
    {
        FailureDigest.Of(CheckStep.Build, "dotnet", Error(1)).Items.Single().File.Should().Be($"{Tree}/src/File1.cs");
    }

    [Fact]
    public async Task TheLogOfAFailedBuildStepOfTheRunner_IsWhatTheDigestReads()
    {
        using var dir = TempDir.For("coai-digest-run-");
        Directory.CreateDirectory(Path.Combine(dir.Path, "tree"));
        var text = ToolchainFixture.File(["emit-exit", Error(7, "the runner wrote this to its log"), "1"], ["emit", "never"]);

        var result = await new CheckRunner(new CoaiMcp.Runners.Processes.ProcessLauncher()).RunAsync(ToolchainFixture.Read(text), new CheckRequest(dir.At("tree"), dir.At("logs"), 4));

        var failed = result.FailedStep!;
        var digest = FailureDigest.Of(failed.Step, "dotnet", await File.ReadAllTextAsync(failed.LogPath), Tree);
        digest.Items.Should().ContainSingle().Which.Should().Be(new DigestItem("build", "src/File7.cs", 7, "CS1007", "the runner wrote this to its log"));
    }
}
