using System.Text;
using System.Text.Json;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The JSON the three role tools answer — written by hand over <see cref="Utf8JsonWriter"/> like the other tools of this build, so the shapes are exactly the ones the spec names and the
/// result document is embedded as JSON, not as a string.
/// </summary>
internal static class RoleJobAnswers
{
    private static readonly JsonWriterOptions Indented = new() { Indented = false };

    /// <summary>The shape every refusal here has: <c>{"error": "..."}</c>, with the job id when a job exists (a start whose launch failed).</summary>
    public static string Refused(string sentence, string jobId = "") => Write(json =>
    {
        json.WriteString("error", sentence);
        if (jobId.Length > 0)
        {
            json.WriteString("jobId", jobId);
        }
    });

    /// <summary><c>start_role</c>: <c>{jobId, state, route[], launcher}</c>. The route is the router's (story s3); until it runs it is empty.</summary>
    public static string Started(RoleJobRecord job) => Write(json =>
    {
        json.WriteString("jobId", job.JobId);
        json.WriteString("state", job.State);
        WriteStrings(json, "route", job.Route);
        json.WriteString("launcher", job.Launcher);
    });

    /// <summary><c>role_status</c>: <c>{state, generation, attempts[{n, model, vendor, stop, reason, evidenceRef}], stalled, result}</c> and the facts a reader needs beside them.</summary>
    public static string Status(RoleJobObservation seen, string resultJson) => Write(json =>
    {
        var job = seen.Job;
        json.WriteString("jobId", job.JobId);
        json.WriteString("role", job.Role);
        json.WriteString("state", job.State);
        json.WriteNumber("generation", job.Generation);
        json.WriteBoolean("stalled", seen.Stalled);
        json.WriteString("launcher", job.Launcher);
        json.WriteString("model", job.Model);
        WriteStrings(json, "route", job.Route);
        json.WriteStartArray("attempts");
        foreach (var attempt in job.Attempts)
        {
            json.WriteStartObject();
            json.WriteNumber("n", attempt.N);
            json.WriteString("model", attempt.Model);
            json.WriteString("vendor", attempt.Vendor);
            json.WriteString("stop", attempt.Stop);
            json.WriteString("reason", attempt.Reason);
            json.WriteString("evidenceRef", attempt.EvidenceRef);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteString("stop", job.Stop);
        json.WriteString("reason", job.Reason);
        WriteTime(json, "createdUtc", job.CreatedUtc);
        WriteTime(json, "startedUtc", job.StartedUtc);
        WriteTime(json, "endedUtc", job.EndedUtc);
        json.WritePropertyName("result");
        WriteResult(json, resultJson);
    });

    /// <summary><c>role_cancel</c>: where the job ended up.</summary>
    public static string Cancelled(RoleJobRecord job, bool wasOver) => Write(json =>
    {
        json.WriteString("jobId", job.JobId);
        json.WriteString("state", job.State);
        json.WriteString("stop", job.Stop);
        json.WriteString("reason", job.Reason);
        json.WriteBoolean("alreadyOver", wasOver);
    });

    private static void WriteResult(Utf8JsonWriter json, string resultJson)
    {
        if (resultJson.Length == 0)
        {
            json.WriteNullValue();

            return;
        }

        try
        {
            using var document = JsonDocument.Parse(resultJson);
            document.RootElement.WriteTo(json);
        }
        catch (JsonException)
        {
            // A result that is not JSON is still the role's answer: it is handed over as the text it is.
            json.WriteStringValue(resultJson);
        }
    }

    private static void WriteStrings(Utf8JsonWriter json, string name, IReadOnlyList<string> values)
    {
        json.WriteStartArray(name);
        foreach (var value in values)
        {
            json.WriteStringValue(value);
        }

        json.WriteEndArray();
    }

    private static void WriteTime(Utf8JsonWriter json, string name, DateTime? utc)
    {
        if (utc is { } at)
        {
            json.WriteString(name, CoaiMcp.Core.Notices.ServerNotice.Iso(new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc))));
        }
        else
        {
            json.WriteNull(name);
        }
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, Indented))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
