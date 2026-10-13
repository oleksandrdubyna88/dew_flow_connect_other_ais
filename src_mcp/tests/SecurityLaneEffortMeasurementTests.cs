using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// One ARM of the security-lane effort measurement through real code rounds — explicit hardware, never unattended CI
/// (epic 2 of research/PLAN_local_effort_reaches_every_local_launch.md; results in
/// research/RESULTS_security_lane_effort_2026-10-13.md).
/// </summary>
/// <remarks>
/// <para>The arm comes from the environment (<see cref="SecurityLaneEffortArm.From"/>): <c>COAI_SECURITY_CALIBRATION_EFFORT</c>,
/// <c>…_MAX_TOKENS</c>, <c>…_REPEATS</c>, <c>…_FIXTURE</c> (<c>unsafe</c>: the planted tenant-guard removal and interpolated
/// SQL; <c>safe</c>: the same method with <c>ORDER BY Id</c> added, whose correct answer is empty), beside the calibration
/// harness's own <c>…_MODEL</c>, <c>…_ENDPOINT</c> and <c>…_OUT</c>.</para>
/// <para>Unlike the qualification test beside it, a reviewer cut at its ceiling does not fail the run — that cut is an
/// observation. What still fails: an error reply, a prompt that never reached the shim, and any cell whose recorded argv
/// did not carry the arm's effort and ceiling, so a cell that did not differ can never be counted in the arm.</para>
/// </remarks>
public sealed class SecurityLaneEffortMeasurementTests
{
    [Fact(Explicit = true)]
    public async Task One_effort_arm_records_every_cell_and_every_cell_carried_the_arm()
    {
        var root = Environment.GetEnvironmentVariable("COAI_SECURITY_CALIBRATION_OUT");
        root.Should().NotBeNullOrWhiteSpace("set an explicit output directory to retain each measured cell");
        var arm = SecurityLaneEffortArm.From(Environment.GetEnvironmentVariable);
        // One directory per arm and run, so an interrupted arm leaves one identifiable partial directory (plan round, finding 2).
        var output = Path.Combine(root!, $"{arm.Name}-{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}");
        Directory.CreateDirectory(output);
        var real = new ProcessLauncher();
        await using var repo = await TempGitRepo.InitAsync(real, "coai-security-effort-");
        await repo.WriteAsync("AGENTS.md", "Review this isolated invoice fixture. Preserve tenant isolation and SQL parameterization.\n");
        await repo.WriteAsync("Invoices.cs", SecurityLaneCalibrationTests.Source(unsafeChange: false));
        await repo.CommitAsync("safe calibration baseline");
        var baseline = await repo.HeadAsync();
        var head = arm.Safe ? SecurityLaneCalibrationTests.SafeHead() : SecurityLaneCalibrationTests.Source(unsafeChange: true);
        await repo.WriteAsync("Invoices.cs", head);
        await repo.CommitAsync(arm.Safe ? "order the invoices by id" : "synthetic tenant and query regression");
        var revision = await repo.HeadAsync();
        await File.WriteAllTextAsync(Path.Combine(output, "fixture.cs.txt"), head);
        await File.WriteAllTextAsync(Path.Combine(output, "manifest.json"), Manifest(arm, baseline, revision, head));

        for (var repeat = 1; repeat <= arm.Repeats; repeat++)
        {
            await SecurityLaneCalibrationTests.RunCell(repo.Path, baseline, revision, Path.Combine(output, $"repeat-{repeat}"), real,
                SecurityLaneCalibrationTests.Scope, SecurityLaneCalibrationTests.CalibrationPrompts,
                tune: panel => panel with { LocalReasoningEffort = arm.Effort, LocalMaxTokens = arm.MaxTokens },
                strict: false);
        }

        arm.Mismatches(RecordedArgv(output)).Should().BeEmpty("every cell of an arm must have been launched with the arm's effort and ceiling");
    }

    private static IReadOnlyList<IReadOnlyList<string>> RecordedArgv(string output) =>
        [.. Directory.EnumerateFiles(output, "turn-*.arguments.json", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file)) ?? []))];

    private static string Manifest(SecurityLaneEffortArm arm, string baseline, string revision, string head) =>
        JsonSerializer.Serialize(new
        {
            arm = arm.Name,
            effort = arm.Effort,
            maxOutputTokens = arm.MaxTokens,
            repeats = arm.Repeats,
            fixture = arm.Safe ? "safe" : "unsafe",
            model = SecurityLaneCalibrationTests.Model,
            endpoint = SecurityLaneCalibrationTests.Endpoint,
            contextTokens = 131072,
            localConcurrency = 1,
            baseline,
            head = revision,
            sourceHash = SecurityLaneCalibrationTests.Hash(head),
            utc = DateTimeOffset.UtcNow,
            prompts = SecurityLaneCalibrationTests.CalibrationPrompts.Select(id => new { id, sha256 = SecurityLaneCalibrationTests.Hash(RolePrompts.ShippedDefaultFor(id)) }),
            sampling = "the product's: temperature 0, a seed derived from the prompt bytes, frequency penalty 0.2",
            ordinaryReviewer = "FakeCli clean answer; only local security findings are measured",
            gpu = "the family GPU lease daemon was not listening; the product's cross-process EngineLease serialised local calls, the owner's live reviews included",
            limitation = "Token usage cannot prove local input coverage. Reproductions are not executed. Wall time includes queueing.",
        }, new JsonSerializerOptions { WriteIndented = true });
}
