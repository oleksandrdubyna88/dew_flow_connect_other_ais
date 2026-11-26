using CoaiMcp.Core.Toolchain;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Epoch 3 E4 s4 t3 (R1, R2, R3): the readers of the consoles of jest, vitest and <c>node --test</c>, on the recordings of story s1.</summary>
public sealed class ANodeTestDigestTests
{
    [Fact]
    public void R1_TheRecordedJestConsole_GivesTheFailedTests_WithTheNameTheFileTheFirstFrameAndTheAssertion()
    {
        var digest = DigestFixtures.Digest("node-jest-three-tests.txt", CheckStep.Test, "node", "jest");

        digest.Kind.Should().Be(DigestKind.Jest);
        var tests = digest.Items.Where(item => item.Code != "Test suite failed to run").ToList();
        tests.Select(item => (item.File, item.Line, item.Code)).Should().Equal(("sum.test.js", 2, "fails on the value"), ("sum.test.js", 3, "fails by throwing"));
        tests[0].Message.Should().Be("expect(received).toBe(expected) // Object.is equality\nExpected: 3\nReceived: 2");
        tests[1].Message.Should().Be("boom from the scratch test");
        digest.Items.Should().ContainSingle(item => item.Code == "Test suite failed to run" && item.File == "empty.test.js", "a suite that could not run is a failure too, and the coder is told");
    }

    [Fact]
    public void R1_TheRecordedVitestConsole_GivesTheFailedTests_WithTheNameTheFileTheLocationAndTheAssertion()
    {
        var digest = DigestFixtures.Digest("node-vitest-three-tests.txt", CheckStep.Test, "node", "vitest");

        digest.Kind.Should().Be(DigestKind.Vitest);
        var tests = digest.Items.Where(item => item.Code != "Test suite failed to run").ToList();
        tests.Select(item => (item.File, item.Code)).Should().Equal(("sum.test.js", "fails on the value"), ("sum.test.js", "fails by throwing"));
        tests[0].Line.Should().Be(4);
        tests[0].Message.Should().StartWith("AssertionError: expected 2 to be 3");
        tests[1].Message.Should().StartWith("Error: boom from the scratch test");
        digest.Items.Should().ContainSingle(item => item.Code == "Test suite failed to run");
    }

    [Fact]
    public void R1_TheRecordedNodeTestOutput_GivesTheFailedTests_WithTheNameTheLocationAndTheMessage()
    {
        var digest = DigestFixtures.Digest("node-node-test-three-tests.txt", CheckStep.Test, "node", "node-test");

        digest.Kind.Should().Be(DigestKind.NodeTest);
        digest.Items.Select(item => (item.File, item.Line, item.Code)).Should().Equal(("sum.test.js", 4, "fails on the value"), ("sum.test.js", 5, "fails by throwing"));
        digest.Items[0].Message.Should().Contain("Expected values to be strictly equal").And.Contain("2 !== 3");
        digest.Items[1].Message.Should().Be("Error: boom from the scratch test");
    }

    [Theory]
    [InlineData("node-jest-verbose.txt", "jest")]
    [InlineData("node-vitest-verbose.txt", "vitest")]
    [InlineData("node-node-test-verbose.txt", "node-test")]
    public void TheVerboseConsoles_GiveTheSameFailures(string recording, string runner)
    {
        var digest = DigestFixtures.Digest(recording, CheckStep.Test, "node", runner);

        digest.Items.Where(item => item.Code != "Test suite failed to run").Select(item => item.Code).Should().Contain(["fails on the value", "fails by throwing"]);
    }

    [Fact]
    public void ForAStepOfTheFile_TheNodeReadersAreTriedInOneFixedOrder_TheFirstThatGivesAnItemWins()
    {
        foreach (var (recording, kind) in new[] { ("node-jest-three-tests.txt", DigestKind.Jest), ("node-vitest-three-tests.txt", DigestKind.Vitest), ("node-node-test-three-tests.txt", DigestKind.NodeTest) })
        {
            DigestFixtures.Digest(recording, CheckStep.Test, "node").Kind.Should().Be(kind, "no runner fact: every reader is tried, and only the tool's own marker lets one read");
        }
    }

    [Fact]
    public void R2_EveryRecording_GoesThroughEveryReader_AndOnlyTheOwnToolsReaderGivesAnItem()
    {
        var owners = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["jest"] = [.. DigestFixtures.All().Select(recording => recording.Name).Where(name => name.StartsWith("node-jest", StringComparison.Ordinal))],
            ["vitest"] = [.. DigestFixtures.All().Select(recording => recording.Name).Where(name => name.StartsWith("node-vitest", StringComparison.Ordinal))],
            ["node-test"] = [.. DigestFixtures.All().Select(recording => recording.Name).Where(name => name.StartsWith("node-node-test", StringComparison.Ordinal))],
        };

        foreach (var (recording, text) in DigestFixtures.All())
        {
            foreach (var reader in owners.Keys)
            {
                var items = DigestFixtures.Readers[reader]("test", text);
                if (!owners[reader].Contains(recording))
                {
                    items.Should().BeEmpty($"{reader} has no marker line in {recording}");
                }
            }
        }
    }

    [Fact]
    public void R3_AMebibyteOfRandomLines_NeverThrows_ForEachReader_AndGivesNoItem()
    {
        var random = new Random(20261128);
        var text = string.Join('\n', Enumerable.Range(0, 20000).Select(_ => new string([.. Enumerable.Range(0, random.Next(0, 80)).Select(_ => (char)random.Next(32, 0x2FFF))])));

        foreach (var (name, reader) in DigestFixtures.Readers)
        {
            reader("test", text).Should().BeEmpty(name);
        }

        FailureDigest.Of(CheckStep.Test, "node", text).Kind.Should().Be(DigestKind.RawTail);
    }

    [Fact]
    public void TheMarkerLinesAlone_WithNoFailure_GiveNoItem()
    {
        DigestFixtures.Readers["jest"]("test", "Tests:       0 failed, 3 passed, 3 total\n").Should().BeEmpty();
        DigestFixtures.Readers["node-test"]("test", "ℹ tests 3\nℹ pass 3\nℹ fail 0\n").Should().BeEmpty();
    }
}
