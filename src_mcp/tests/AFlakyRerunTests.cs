using System.Text;
using CoaiMcp.Core.Notices;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s5 t5 (K1, K2, K3, K7, K8, K11, K13): the re-run itself — one more run of the failed tests in a new unit, the <c>flaky</c> list with its proof, ONE notice, the setting. The real coder rig with a project of the file whose full test prints the Microsoft Testing Platform summary: it fails the first time the step
/// runs in a round and passes the second (a marker in an ignored folder of the worktree, which the clean of the next round removes).
/// </summary>
[Collection("fakecli-env")]
public sealed class AFlakyRerunTests
{
    private const string Ignore = ".flaky/\n";

    private const string Failing = "echo 'failed Flaky.Tests.Sometimes (3ms)'; echo '  boom'; echo '    at Flaky.Tests.Sometimes() in tests/FlakyTests.cs:3'; echo; echo 'Test run summary: Failed!'; echo '  total: 5'; echo '  failed: 1'; echo '  succeeded: 4'; echo '  skipped: 0'; exit 2";

    private const string Passing = "echo 'Test run summary: Passed!'; echo '  total: 5'; echo '  failed: 0'; echo '  succeeded: 5'; echo '  skipped: 0'; exit 0";

    private static string Toolchain(string secondRun, string? narrowed = null, string firstRun = Failing)
    {
        var full = $"mkdir -p .flaky; if [ -f .flaky/seen ]; then {secondRun}; else touch .flaky/seen; {firstRun}; fi";
        var text = new StringBuilder("version: 1\nprojects:\n  - path: \".\"\n    adapter: dotnet\n");
        text.Append("    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n");
        text.Append($"    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(full)}\n");
        if (narrowed is not null)
        {
            text.Append($"    testFor:\n      - \"/bin/sh\"\n      - \"-c\"\n      - {ToolchainFixture.Quote(narrowed)}\n      - \"sel\"\n      - \"{{file}}\"\n    testFiles:\n      - \"tests\"\n");
        }

        return text.ToString();
    }

    private static Dictionary<string, string> Base() => new() { [".gitignore"] = Ignore, ["tests/FlakyTests.cs"] = "class FlakyTests {}\n" };

    private static CoderRig.Launch Writes(params (string Path, string Text)[] files) => new(files.ToDictionary(file => file.Path, file => file.Text), CoderRig.Report());

    private static IEnumerable<(string Scope, int Exit, bool Excused)> TestSteps(System.Text.Json.JsonElement envelope) =>
        envelope.GetProperty("checks").EnumerateArray().Where(step => step.GetProperty("step").GetString() == "test").Select(step => (step.GetProperty("scope").GetString()!, step.GetProperty("exitCode").GetInt32(), step.GetProperty("excused").GetBoolean()));

    [Fact]
    public async Task K1_ATestThatFailsOnceAndPassesWithProof_IsFlaky_TheRoundIsGreen_AndOneNoticeIsRaised()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Passing), extra: Base());
        rig.Says("sonnet", Writes((CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("green").GetBoolean().Should().BeTrue();
        var flaky = envelope.GetProperty("flaky").EnumerateArray().Should().ContainSingle().Subject;
        (flaky.GetProperty("test").GetString(), flaky.GetProperty("file").GetString(), flaky.GetProperty("rerun").GetString(), flaky.GetProperty("proof").GetString()).Should().Be(("Flaky.Tests.Sometimes", "tests/FlakyTests.cs", "whole", "totals"));
        TestSteps(envelope).Should().Equal(("full", 2, true), ("rerun", 0, false));
        rig.Rig.NoticesOf(ServerNoticeCodes.CoderTestFlaky).Should().ContainSingle();
    }

    [Fact]
    public async Task K2_ATestThatFailsTwice_StaysRed_WithTheDigestOfTheFirstRun_AndNoNotice()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Failing), extra: Base());
        rig.Says("sonnet", Writes((CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("flaky").GetArrayLength().Should().Be(0);
        envelope.GetProperty("lastDigest").GetRawText().Should().Contain("Flaky.Tests.Sometimes");
        rig.Rig.NoticesOf(ServerNoticeCodes.CoderTestFlaky).Should().BeEmpty();
    }

    [Fact]
    public async Task K3_ATestWhoseOwnFileTheJobChanged_IsNeverReRun()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Passing), extra: Base());
        rig.Says("sonnet", Writes(("tests/FlakyTests.cs", "class FlakyTests { int changed; }\n"), (CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        TestSteps(rig.Envelope(job)).Should().Equal(("full", 2, false));
    }

    [Fact]
    public async Task K8_WithTheSettingAtZero_NoReRunStarts()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Passing), extra: Base());
        rig.Says("sonnet", Writes((CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        rig.Rig.Setting("COAI_FLAKY_RERUNS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed, ended.Reason);
        TestSteps(rig.Envelope(job)).Should().Equal(("full", 2, false));
    }

    [Fact]
    public async Task K1_ANarrowedReRun_WithTheFilesTestFor_IsProvedByTheNameAmongThePassedTests()
    {
        const string narrowed = "echo \"passed Flaky.Tests.Sometimes (1ms)\"; echo 'Test run summary: Passed!'; echo '  total: 1'; echo '  failed: 0'; echo '  succeeded: 1'; echo '  skipped: 0'";
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Failing, narrowed), extra: Base());
        rig.Says("sonnet", Writes((CoderRig.StateFile, CoderRig.Green + "\n")));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var flaky = rig.Envelope(job).GetProperty("flaky").EnumerateArray().Should().ContainSingle().Subject;
        (flaky.GetProperty("rerun").GetString(), flaky.GetProperty("proof").GetString()).Should().Be(("narrowed", "named"));
    }

    [Fact]
    public async Task K11_ATestForWhoseFilterMatchesNoTest_GivesAReRunInWhichNoTestRan_NothingIsExcused_TheRoundIsRed()
    {
        const string narrowed = "echo 'Zero tests ran'; echo 'Test run summary: Failed!'; echo '  total: 0'; echo '  failed: 0'; echo '  succeeded: 0'; exit 8";
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(Passing, narrowed), extra: Base());
        rig.Says("sonnet", Writes((CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Failed, ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("flaky").GetArrayLength().Should().Be(0);
        envelope.GetProperty("lastDigest").GetRawText().Should().Contain("Flaky.Tests.Sometimes", "the coder gets the digest of the first run");
    }

    [Fact]
    public async Task K13_AReRunThatPassesTheNamedTest_ButExitsNonZero_ExcusesNothing_TheRoundStaysRed()
    {
        const string wholeRerun = "echo 'Test run summary: Passed!'; echo '  total: 5'; echo '  failed: 0'; echo '  succeeded: 5'; echo '  skipped: 0'; exit 3";
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(wholeRerun), extra: Base());
        rig.Says("sonnet", Writes((CoderRig.StateFile, CoderRig.Green + "\n")));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "0");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, "not-green"), ended.Reason);
        var envelope = rig.Envelope(job);
        envelope.GetProperty("flaky").GetArrayLength().Should().Be(0);
        TestSteps(envelope).Should().Equal(("full", 2, false), ("rerun", 3, false));
        rig.Rig.NoticesOf(ServerNoticeCodes.CoderTestFlaky).Should().BeEmpty();
    }
}
