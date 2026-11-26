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
