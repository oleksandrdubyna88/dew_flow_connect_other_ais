using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The envelope of <c>result.json</c> for the scout and the judge (epoch 3 E3 s2; spec A, "The envelope"): built by coai from the model's answer and from coai's own checks, never copied from the model. It holds the answer, with
/// <c>verified</c> and <c>why</c> on every evidence item, <c>exists</c> on every file, the counts, the repair turns the answer took and the commit the checkout was at when the job began.
/// </summary>
/// <remarks>
/// <para><b>Every text the model wrote passes the redaction rule</b> (<see cref="Redaction.SafeText"/>) on its way into the envelope: a line of a file that held a credential, quoted back as evidence, is stored as <c>[redacted]</c>. The
/// <c>verified</c> flag was decided on the model's text BEFORE this, so redaction never turns a false into a true.</para>
/// <para>A model's own <c>verified</c> is never read: the readers refuse an unknown key. An item that was not verified is in the envelope with <c>verified: false</c> and the reason; it is never shown as verified and never dropped.</para>
/// </remarks>
internal static class ReadOnlyEnvelope
{
    public static string Scout(ScoutAnswer answer, IReadOnlyList<IReadOnlyList<CheckedEvidence>> evidence, IReadOnlyList<(string Path, bool Exists)> files, int repairTurns, string commit) =>
        Build(json =>
        {
            json.WriteString("status", "ok");
            json.WriteStartArray("facts");
            for (var i = 0; i < answer.Facts.Count; i++)
            {
                json.WriteStartObject();
                json.WriteString("claim", Safe(answer.Facts[i].Claim));
                WriteEvidence(json, evidence[i]);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            WriteFiles(json, files);
            WriteTexts(json, "unknown", answer.Unknown);
            json.WriteStartObject("counts");
            json.WriteNumber("facts", answer.Facts.Count);
            WriteEvidenceCounts(json, evidence.SelectMany(items => items).ToList());
            json.WriteNumber("filesMissing", files.Count(file => !file.Exists));
            json.WriteEndObject();
            Close(json, repairTurns, commit);
        });

    public static string Judge(JudgeAnswer answer, IReadOnlyList<IReadOnlyList<CheckedEvidence>> evidence, int repairTurns, string commit) =>
        Build(json =>
        {
            json.WriteString("status", "ok");
            json.WriteString("verdict", answer.Verdict);
            json.WriteString("summary", Safe(answer.Summary));
            json.WriteStartArray("findings");
            for (var i = 0; i < answer.Findings.Count; i++)
            {
                WriteFinding(json, answer.Findings[i], evidence[i]);
            }

            json.WriteEndArray();
            WriteDecision(json, answer.Decision);
            json.WriteStartObject("counts");
            json.WriteNumber("findings", answer.Findings.Count);
            WriteEvidenceCounts(json, evidence.SelectMany(items => items).ToList());
            json.WriteEndObject();
            Close(json, repairTurns, commit);
        });

    private static void WriteFinding(Utf8JsonWriter json, JudgeFinding finding, IReadOnlyList<CheckedEvidence> evidence)
    {
        json.WriteStartObject();
        json.WriteString("id", finding.Id);
        json.WriteString("severity", finding.Severity);
        json.WriteString("title", Safe(finding.Title));
        json.WriteString("detail", Safe(finding.Detail));
        WriteEvidence(json, evidence);
        json.WriteString("fix", Safe(finding.Fix));
        json.WriteEndObject();
    }

    private static void WriteDecision(Utf8JsonWriter json, JudgeDecision decision)
    {
        if (!decision.Made)
        {
            return;
        }

        json.WriteStartObject("decision");
        json.WriteString("chosen", Safe(decision.Chosen));
        json.WriteString("why", Safe(decision.Why));
        json.WriteStartArray("rejected");
        foreach (var option in decision.Rejected)
        {
            json.WriteStartObject();
            json.WriteString("option", Safe(option.Option));
            json.WriteString("why", Safe(option.Why));
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteEvidence(Utf8JsonWriter json, IReadOnlyList<CheckedEvidence> evidence)
    {
        json.WriteStartArray("evidence");
        foreach (var checkedItem in evidence)
        {
            json.WriteStartObject();
            json.WriteString("file", Safe(checkedItem.Item.File));
            json.WriteNumber("line", checkedItem.Item.Line);
            json.WriteString("text", Safe(checkedItem.Item.Text));
            json.WriteBoolean("verified", checkedItem.Verified);
            json.WriteString("why", checkedItem.Why);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteFiles(Utf8JsonWriter json, IReadOnlyList<(string Path, bool Exists)> files)
    {
        json.WriteStartArray("files");
        foreach (var (path, exists) in files)
        {
            json.WriteStartObject();
            json.WriteString("path", Safe(path));
            json.WriteBoolean("exists", exists);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteTexts(Utf8JsonWriter json, string name, IReadOnlyList<string> texts)
    {
        json.WriteStartArray(name);
        foreach (var text in texts)
        {
            json.WriteStringValue(Safe(text));
        }

        json.WriteEndArray();
    }

    private static void WriteEvidenceCounts(Utf8JsonWriter json, IReadOnlyList<CheckedEvidence> all)
    {
        json.WriteNumber("evidence", all.Count);
        json.WriteNumber("verified", all.Count(item => item.Verified));
    }

    private static void Close(Utf8JsonWriter json, int repairTurns, string commit)
    {
        json.WriteNumber("repairTurns", repairTurns);
        json.WriteString("commit", commit);
    }

    private static string Safe(string text) => Redaction.SafeText(text, Redaction.DetailLimit);

    private static string Build(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            write(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
