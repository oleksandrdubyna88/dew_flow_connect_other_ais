using System.Text;
using CoaiMcp.Core.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s5 t3 (S1 to S7; SPEC E, level two): a selected run exists only where the FILE names <c>testFor</c>: the files the job added or changed under a <c>testFiles</c> prefix; a red selected run ends the round red and the full command does not run; a green one is followed by the full command on the SAME commit,
/// whose word is the only green; "no test ran" is nothing selected and the full command runs; after a full run has failed once the later rounds run the full command only. Pure half and the real coder rig.
/// </summary>
[Collection("fakecli-env")]
public sealed class ASelectedThenFullTests
{
    // ---- pure ----

    [Fact]
    public void S1_TheSelectedFiles_AreWhatTheJobChanged_UnderATestFilesPrefix_OfTheProject()
    {
        var changed = new[] { "svc/tests/AlphaTests.cs", "svc/src/Alpha.cs", "other/tests/BetaTests.cs", "svc/tests/sub/GammaTests.cs" };

        TestSelection.Select(changed, "svc", ["tests"]).Should().BeEquivalentTo(new SelectedFiles("files", ["svc/tests/AlphaTests.cs", "svc/tests/sub/GammaTests.cs"]));
        TestSelection.Select(changed, "svc", ["lib"]).State.Should().Be("none");
        TestSelection.Select(["tests/A.cs"], string.Empty, ["tests"]).Files.Should().Equal("tests/A.cs");
    }

    [Theory]
    [InlineData("tests/My-Test.cs")]
    [InlineData("tests/a b.cs")]
    [InlineData("tests/-flag.cs")]
    [InlineData("tests/we;ird.cs")]
    [InlineData("tests/Dollar$.cs")]
    public void S6_AStemThatIsNotLettersDigitsAndUnderscores_OrAnUnsafeName_IsNoSelectedRun(string path)
    {
        TestSelection.Select([path], string.Empty, ["tests"]).State.Should().Be("unsafe");
    }

    [Fact]
    public void S6_MoreThanFiftyFiles_IsNoSelectedRun()
    {
        var many = Enumerable.Range(0, TestSelection.MaxFiles + 1).Select(n => $"tests/F{n}Tests.cs");

        TestSelection.Select(many, string.Empty, ["tests"]).State.Should().Be("too-many");
        TestSelection.Select(many.Take(TestSelection.MaxFiles), string.Empty, ["tests"]).State.Should().Be("files");
    }

    [Fact]
    public void TheSelectedArgv_ReplacesFileAndStemInPlace_OneItemPerFile()
    {
        TestSelection.Argv(["run", "--filter-class", "CoaiMcp.Tests.{stem}", "{file}", "-j", "{jobs}"], ["tests/AlphaTests.cs", "tests/BetaTests.cs"], ".", 2)
            .Should().Equal("run", "--filter-class", "CoaiMcp.Tests.{stem}", "./tests/AlphaTests.cs", "./tests/BetaTests.cs", "-j", "2");
        TestSelection.Argv(["x", "{stem}"], ["tests/AlphaTests.cs", "tests/BetaTests.cs"], ".", 1).Should().Equal("x", "AlphaTests", "BetaTests");
    }

    // ---- the real rig ----

    private const string Ignore = ".flaky/\n";

    /// <summary>A project of the file: the build says <c>built</c>; the selected command (<c>testFor</c>) goes red when a file it is given holds SELECTED_RED, and exits 8 (the no-test-ran code of the MTP runner) when it holds NONE_RAN; the full command goes red when any file under tests holds FULL_RED.</summary>
    private static string Toolchain()
    {
        const string selected = "for f in \"$@\"; do echo \"selected $f\"; if grep -q NONE_RAN \"$f\"; then echo 'Zero tests ran'; exit 8; fi; if grep -q SELECTED_RED \"$f\"; then echo \"FAILED selected $f\"; exit 1; fi; done; echo selected-ok";
        const string full = "echo full-run; if grep -rq FULL_RED tests; then echo 'FAILED the full run'; exit 1; fi; echo full-ok";
        var text = new StringBuilder("version: 1\nprojects:\n  - path: \".\"\n    adapter: dotnet\n");
        text.Append("    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n");
        text.Append($"    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(full)}\n");
        text.Append($"    testFor:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(selected)}\n      - \"sel\"\n      - \"{{file}}\"\n    testFiles:\n      - \"tests\"\n");

        return text.ToString();
    }

    private static IEnumerable<(string Scope, int Files, int Exit)> Steps(System.Text.Json.JsonElement envelope) =>
        envelope.GetProperty("checks").EnumerateArray().Where(step => step.GetProperty("step").GetString() == "test").Select(step => (step.GetProperty("scope").GetString()!, step.GetProperty("files").GetInt32(), step.GetProperty("exitCode").GetInt32()));

    private static CoderRig.Launch Writes(params (string Path, string Text)[] files) => new(files.ToDictionary(file => file.Path, file => file.Text), CoderRig.Report());

    private static Dictionary<string, string> Base(params (string Path, string Text)[] more) =>
        new(more.Select(file => KeyValuePair.Create(file.Path, file.Text))) { [".gitignore"] = Ignore, ["tests/KeepTests.cs"] = "class KeepTests {}\n" };

    [Fact]
    public async Task S3_AGreenSelectedRun_IsFollowedByTheFullRunOnTheSameCommit_AndOnlyTheFullRunSaysGreen()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: Base());
        rig.Says("sonnet", Writes(("tests/AlphaTests.cs", "class AlphaTests {}\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        Steps(envelope).Should().Equal(("selected", 1, 0), ("full", 0, 0));
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task S3_ARedSelectedRun_EndsTheRoundRed_WithItsDigest_AndTheFullCommandDoesNotRun()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: Base());
        rig.Says("sonnet", Writes(("tests/AlphaTests.cs", "SELECTED_RED\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        var envelope = rig.Envelope(job);
        Steps(envelope).Should().Equal(("selected", 1, 1));
        envelope.GetProperty("lastDigest").GetRawText().Should().Contain("FAILED selected");
    }

    [Fact]
    public async Task S4_ASelectedGreenAndAFullRed_IsRed_WithTheDigestOfTheFullRun()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: Base(("tests/OtherTests.cs", "FULL_RED\n")));
        rig.Says("sonnet", Writes(("tests/AlphaTests.cs", "class AlphaTests {}\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        var envelope = rig.Envelope(job);
        Steps(envelope).Should().Equal(("selected", 1, 0), ("full", 0, 1));
        envelope.GetProperty("lastDigest").GetRawText().Should().Contain("FAILED the full run");
    }

    [Fact]
    public async Task S5_NoTestRan_IsNothingSelected_NotRedNotGreen_AndTheFullCommandRuns()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: Base());
        rig.Says("sonnet", Writes(("tests/AlphaTests.cs", "NONE_RAN\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Steps(rig.Envelope(job)).Should().Equal(("selected", 1, 8), ("full", 0, 0));
    }

    [Fact]
    public async Task S6_AnUnsafeFileName_IsNoSelectedRun_TheFullRunAlone()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: Base());
        rig.Says("sonnet", Writes(("tests/My-Test.cs", "class X {}\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Steps(rig.Envelope(job)).Should().Equal(("full", 0, 0));
    }

    [Fact]
    public async Task S7_AfterAFullRunFailedOnce_TheLaterRoundsRunTheFullCommandOnly_AndTheFactIsAFileOfTheJob()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: Base(("tests/OtherTests.cs", "FULL_RED\n")));
        rig.Says("sonnet",
            Writes(("tests/AlphaTests.cs", "class AlphaTests {}\n"), (CoderRig.StateFile, CoderRig.Green + "\n")),
            Writes(("tests/OtherTests.cs", "fixed\n"), ("tests/BetaTests.cs", "class BetaTests {}\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "1");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        Steps(rig.Envelope(job)).Should().Equal([("full", 0, 0)], "the second round ran the full command only: a full run failed in the first");
        File.Exists(Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "full-failed")).Should().BeTrue();
    }
}
