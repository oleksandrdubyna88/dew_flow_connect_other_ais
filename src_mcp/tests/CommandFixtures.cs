using Xunit;
using FluentAssertions;
using CoaiMcp.Core.Commands;

namespace CoaiMcp.Tests;

/// <summary>
/// Writes the commands the gate would produce for every plan in this repository, as files a
/// measurement harness can read.
/// </summary>
/// <remarks>
/// <para><b>Why a test and not a script:</b> the text must be the PRODUCT's, not a copy of it. A
/// harness that re-typed the wording would measure the harness. This calls the same
/// <see cref="GateCommands"/> the server calls and writes what it returns.</para>
/// <para>It does nothing unless <c>COAI_WRITE_FIXTURES</c> names a directory, so a normal run — and
/// CI — is unaffected.</para>
/// </remarks>
public sealed class CommandFixtures
{
    [Fact]
    public void WriteThemWhenAsked()
    {
        var outDir = Environment.GetEnvironmentVariable("COAI_WRITE_FIXTURES");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            return;
        }

        var repo = Environment.GetEnvironmentVariable("COAI_REPO_DIR") ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);
        var written = 0;

        foreach (var folder in new[] { "research", "todo" })
        {
            var path = Path.Combine(repo, folder);
            if (!Directory.Exists(path))
            {
                continue;
            }
            foreach (var file in Directory.EnumerateFiles(path, "PLAN_*.md"))
            {
                var text = File.ReadAllText(file);
                var shape = PlanShapeReader.Of(text);
                var context = new CommandContext(
                    Autonomous: true,
                    SplitPlan: true,
                    SplitWithFable: true,
                    FableAvailable: true,
                    PlanText: text,
                    PlanStage: true);
                var commands = GateCommands.For(context);
                // The SECOND arm the campaign needs: what an epic is told when it comes back for
                // its own plan review. The order not to split again is only worth anything if a
                // model actually obeys it, and that is a measurement, not an assertion.
                var epicCommands = GateCommands.For(context with { FirstPlanRound = false });
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    plan = Path.GetFileName(file),
                    folder,
                    lines = shape.Lines,
                    steps = shape.Steps,
                    files = shape.Files,
                    areas = shape.Areas,
                    verdict = shape.Verdict.ToString(),
                    numbers = shape.Numbers,
                    preamble = GateCommands.Preamble,
                    commands,
                    epicCommands,
                });
                File.WriteAllText(Path.Combine(outDir, Path.GetFileNameWithoutExtension(file) + ".json"), json);
                written += 1;
            }
        }

        written.Should().BeGreaterThan(0, "the harness has nothing to measure without them");
    }
}
