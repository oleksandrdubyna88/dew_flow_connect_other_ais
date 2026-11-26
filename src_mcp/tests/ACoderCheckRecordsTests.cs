using System.Text.Json;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Epoch 3 E2 follow-up, the code checkpoint's findings 1 to 6: the record of each check unit is written the moment its step has ended (a cancel in a later step keeps it), carries the step number the runner gave the step, and a record that cannot be written
/// is one warning line in the runner's log and nothing else. The real runner, worktree and units, FakeCli as the coder; skipped, with the reason, where there is no user manager.
/// </summary>
[Collection("fakecli-env")]
public sealed class ACoderCheckRecordsTests
{
    private static CoderRig.Launch Green() => new(new Dictionary<string, string> { [CoderRig.StateFile] = CoderRig.Green + "\n" }, CoderRig.Report());

    private static string Round(CoderRig rig, RoleJobRecord job) => Path.Combine(rig.World.Store.FolderOf(job.JobId)!.Location, "checks", "round-0");

    [Fact]
    public async Task ACancelDuringStepTwo_KeepsTheRecordOfStepOne()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the checks run in a systemd user unit");
        await using var rig = await CoderRig.CreateAsync(toolchain: CoderRig.DefaultToolchain(testTail: "; sleep 60"));
        rig.Says("sonnet", Green());
        var job = rig.NewJob();
        var running = Task.Run(() => rig.RunAsync(job), TestContext.Current.CancellationToken);
        RoleJobWorld.WaitFor(() => rig.UnitsOfAsync(job).GetAwaiter().GetResult().Any(unit => unit.Contains("-s2", StringComparison.Ordinal)), "the unit of step 2 to be running");

        rig.World.Store.FolderOf(job.JobId)!.RequestCancel();
        var (_, ended) = await running;

        ended.State.Should().Be(RoleJobStates.Cancelled);
        var one = Path.Combine(Round(rig, job), "unit-s1.json");
        File.Exists(one).Should().BeTrue("step 1 had ended before the cancel, and its record was written then, not after the round");
        JsonDocument.Parse(File.ReadAllText(one)).RootElement.GetProperty("unit").GetString().Should().Be($"coai-check-{job.JobId}-s1");
    }

    [Fact]
    public async Task TheRecordOfAStep_CarriesTheNumberTheRunnerGaveIt_NotOneReadFromTheLogName()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Green());
        var job = rig.NewJob();

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        var records = new[] { 1, 2 }.Select(number => JsonDocument.Parse(File.ReadAllText(Path.Combine(Round(rig, job), $"unit-s{number}.json"))).RootElement).ToArray();
        records.Select(record => record.GetProperty("part").GetString()).Should().Equal("s1", "s2");
        records.Select(record => record.GetProperty("unit").GetString()).Should().Equal($"coai-check-{job.JobId}-s1", $"coai-check-{job.JobId}-s2");
    }

    [Fact]
    public async Task AUnitRecordThatCannotBeWritten_IsOneWarningLine_AndTheChecksAreStillGreen()
    {
        await using var rig = await CoderRig.CreateAsync();
        rig.Says("sonnet", Green());
        var job = rig.NewJob();
        // A folder at the name of the record: the move of the finished file onto it is refused.
        Directory.CreateDirectory(Path.Combine(Round(rig, job), "unit-s1.json"));

        var (_, ended) = await rig.RunAsync(job);

        ended.State.Should().Be(RoleJobStates.Succeeded, ended.Reason);
        rig.Envelope(job).GetProperty("checksState").GetString().Should().Be("green", "a record that was not kept is not a fault of the checks");
        var said = rig.World.Said.Where(line => line.Level == Serilog.Events.LogEventLevel.Warning).Select(line => line.RenderMessage()).Where(text => text.Contains("could not be written", StringComparison.Ordinal)).ToList();
        said.Should().ContainSingle(text => text.Contains("\"s1\"", StringComparison.Ordinal), "one line, naming the part and nothing of the record's content");
        File.Exists(Path.Combine(Round(rig, job), "unit-s2.json")).Should().BeTrue("the next step's record is still kept");
    }

    [Fact]
    public async Task APreflightRecordThatCannotBeWritten_IsOneWarningLine_AndTheRefusalStands()
    {
        await using var rig = await CoderRig.CreateAsync();
        var main = await rig.GitAsync(rig.Repo, "rev-parse", "--show-toplevel");
        rig.Degrade = arguments => arguments.Any(argument => argument.StartsWith("coai-check-", StringComparison.Ordinal))
            ? [.. arguments.Select(argument => argument == $"BindReadOnlyPaths={main}" ? $"BindPaths={main}" : argument)]
            : arguments;
        rig.Says("sonnet", Green());
        var job = rig.NewJob();
        Directory.CreateDirectory(Path.Combine(Round(rig, job), "unit-preflight.json"));

        var (_, ended) = await rig.RunAsync(job);

        (ended.State, ended.Stop).Should().Be((RoleJobStates.Failed, RoleStopWords.CoderBoundary), ended.Reason);
        rig.World.Said.Where(line => line.Level == Serilog.Events.LogEventLevel.Warning).Select(line => line.RenderMessage())
            .Should().ContainSingle(text => text.Contains("could not be written", StringComparison.Ordinal) && text.Contains("\"preflight\"", StringComparison.Ordinal));
    }
}
