using System.Text;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E4 s5 t2 (F1): the format commit is <c>style: format (coai)</c>, made by coai through the one commit road after the turn's commit; it holds only files the formatter was given; no change means no commit; a second run on that commit changes nothing; nothing else moves HEAD.
/// </summary>
[Collection("fakecli-env")]
public sealed class AFormatCommitTests
{
    private static string Toolchain() =>
        new StringBuilder("version: 1\nprojects:\n  - path: \".\"\n    adapter: node\n    format:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"sed -i 's/BAD/GOOD/g' \\\"$@\\\"\"\n      - \"fmt\"\n      - \"{file}\"\n")
            .Append("    build:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"echo built\"\n    test:\n      - \"/bin/sh\"\n      - \"-c\"\n      - \"if grep -rq 'state: red' src 2>/dev/null; then echo FAILED the state is red; exit 1; fi; echo tested\"\n").ToString();

    [Fact]
    public async Task F1_TheFormatCommit_HoldsOnlyTheFilesGiven_AndATurnWhoseFilesAreAlreadyFormatted_MakesNoCommit()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: new Dictionary<string, string> { [".gitignore"] = "node_modules/\n" });
        rig.Says("sonnet", new CoderRig.Launch(new Dictionary<string, string> { ["a.js"] = "const x = GOOD;\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("format").GetProperty("state").GetString().Should().Be("unchanged");
        (await rig.GitAsync(rig.Repo, "rev-list", "--count", $"{rig.Base}..refs/heads/coder/{job.JobId}")).Should().Be("1", "no change means no commit");
    }

    [Fact]
    public async Task TheFormatCommit_IsCoaisOwn_TheHeadIsTheNewCommit_AndASecondFormatChangesNothing()
    {
        await using var rig = await CoderRig.CreateAsync(toolchain: Toolchain(), extra: new Dictionary<string, string> { [".gitignore"] = "node_modules/\n" });
        rig.Says("sonnet",
            new CoderRig.Launch(new Dictionary<string, string> { ["a.js"] = "const x = BAD;\n", [CoderRig.StateFile] = CoderRig.Red + "\n" }, CoderRig.Report()),
            new CoderRig.Launch(new Dictionary<string, string> { ["b.js"] = "const y = BAD;\n", [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report()));
        rig.Rig.Setting("COAI_FIX_ROUNDS", "1");
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var branch = $"refs/heads/coder/{job.JobId}";
        (await rig.GitAsync(rig.Repo, "log", "--format=%s", "--reverse", $"{rig.Base}..{branch}")).Split('\n').Should().Equal($"coder {job.JobId}: {CoderRig.TaskId}", "style: format (coai)", $"coder {job.JobId}: {CoderRig.TaskId} fix 1", "style: format (coai)");
        (await rig.GitAsync(rig.Repo, "show", $"{branch}:a.js")).Should().Be("const x = GOOD;", "the first format commit stays formatted; the second one gave only b.js");
        rig.Envelope(job).GetProperty("headSha").GetString().Should().Be(await rig.GitAsync(rig.Repo, "rev-parse", branch));
        (await rig.GitAsync(rig.Repo, "diff", "--name-only", $"{branch}~1", branch)).Should().Be("b.js");
    }
}
