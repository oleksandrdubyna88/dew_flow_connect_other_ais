using System.Text.Json;
using CoaiMcp.Core.Toolchain;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// What the checks of a coder job came to, for the envelope (<see cref="CoderEnvelope"/>): every round that ran, with the LAST one as the verdict — <c>green</c> is the last round's word, never the coder's. Written into <c>result.json</c> of a succeeded job and of a
/// job that failed after its commit (the evidence stays beside the failure).
/// </summary>
/// <param name="Rounds">Every round of checks, in the order they ran; never empty.</param>
/// <param name="FixRoundsCap">How many fix turns the job was allowed (<c>COAI_FIX_ROUNDS</c>, <see cref="RoleJobSettings"/>).</param>
/// <param name="FixRoundsNote">Empty, or the sentence that says the setting was not a whole number from 0 to 10 and the default was used.</param>
internal sealed record CoderChecksReport(IReadOnlyList<CoderChecksRound> Rounds, int FixRoundsCap, string FixRoundsNote)
{
    /// <summary>The round that decides.</summary>
    public CoderChecksRound Last => Rounds[^1];

    /// <summary>
    /// <c>toolchain</c>: where the checks came from (<c>file</c>, <c>file+defaults</c> or <c>detected</c>), each project with its adapter, the facts the detector read and the steps planned (name, working folder, network where the table fixes it), the notes, and
    /// <c>changedBetweenBaseAndHead</c>: what differs between the two commits for a detected or defaulted project. No value of the head tree is written: every sentence here was spelled by <see cref="PathSpelling"/> or states a count. Null when the round never reached the resolving.
    /// </summary>
    private static void WriteToolchain(Utf8JsonWriter json, ResolvedToolchain? toolchain, AffectedSelection? selection)
    {
        if (toolchain is null)
        {
            json.WriteNull("toolchain");

            return;
        }

        var plan = StepPlan.Of(toolchain, ToolchainDefaults.Current, 1);
        json.WriteStartObject("toolchain");
        json.WriteString("source", toolchain.Source switch { ToolchainSource.File => "file", ToolchainSource.FileAndDefaults => "file+defaults", _ => "detected" });
        json.WriteStartArray("projects");
        foreach (var project in toolchain.Projects)
        {
            json.WriteStartObject();
            json.WriteString("path", project.Id);
            json.WriteString("adapter", project.FileProject?.Adapter ?? project.Detected?.Stack ?? string.Empty);
            json.WriteString("origin", project.Origin);
            json.WriteBoolean("definedByHead", project.DefinedByHead);
            json.WriteStartObject("facts");
            foreach (var (name, value) in project.Detected?.Facts ?? [])
            {
                json.WriteString(name, name is "projects" or "testProjects" ? PathSpelling.SpellFact(value) : value);
            }

            json.WriteEndObject();
            json.WriteStartArray("steps");
            foreach (var step in plan.Where(step => step.Project == project.Id || (project.Detected is not null && step.Project == project.Detected.Id)))
            {
                json.WriteStartObject();
                json.WriteString("name", step.Step.Wire());
                json.WriteString("workingFolder", step.WorkingFolder);
                json.WriteString("network", step.Network is { } network ? (network.Mode == DefaultNetwork.Host ? "host" : "none") : "job");
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("notes");
        foreach (var note in toolchain.Notes)
        {
            json.WriteStringValue(note);
        }

        json.WriteEndArray();
        json.WriteStartArray("changedBetweenBaseAndHead");
        foreach (var difference in toolchain.Differences)
        {
            json.WriteStringValue(difference);
        }

        json.WriteEndArray();
        json.WriteStartObject("selection");
        json.WriteBoolean("applied", false);
        json.WriteString("note", "information only in this version: every detected unit ran, whatever the selection would have left out");
        json.WriteBoolean("everything", selection is null || selection.Everything);
        json.WriteString("why", selection?.Why ?? string.Empty);
        json.WriteStartArray("leftOut");
        foreach (var left in selection?.Left ?? [])
        {
            json.WriteStartObject();
            json.WriteString("project", PathSpelling.Bare(left.Project));
            json.WriteString("reason", left.Reason);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
        json.WriteEndObject();
    }

    /// <summary>Writes the fields of the envelope's checks half into the object being written.</summary>
    public void Write(Utf8JsonWriter json)
    {
        var last = Last;
        json.WriteBoolean("green", last.Green);
        json.WriteString("checksState", last.State);
        json.WriteString("checksNote", last.Note);
        json.WriteStartObject("format");
        json.WriteString("state", "not_configured");
        json.WriteString("note", CoderChecks.FormatNote);
        json.WriteEndObject();
        json.WriteStartArray("checks");
        foreach (var step in last.Steps)
        {
            json.WriteStartObject();
            json.WriteString("project", step.Project);
            json.WriteString("step", step.Step);
            json.WriteString("network", step.Network);
            json.WriteString("workingFolder", step.WorkingFolder);
            json.WriteStartArray("argv");
            foreach (var argument in step.Argv)
            {
                json.WriteStringValue(argument);
            }

            json.WriteEndArray();
            json.WriteNumber("exitCode", step.ExitCode);
            json.WriteNumber("durationMs", step.DurationMs);
            json.WriteString("logRef", step.LogRef);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        WriteToolchain(json, last.Toolchain, last.Selection);
        json.WriteStartArray("rounds");
        foreach (var round in Rounds)
        {
            json.WriteStartObject();
            json.WriteNumber("turn", round.Turn);
            json.WriteString("headSha", round.HeadSha);
            json.WriteString("state", round.State);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartObject("fixRounds");
        json.WriteNumber("cap", FixRoundsCap);
        json.WriteString("note", FixRoundsNote);
        json.WriteEndObject();
        json.WritePropertyName("lastDigest");
        json.WriteRawValue(FailureDigest.Json(last.Digest));
    }
}
