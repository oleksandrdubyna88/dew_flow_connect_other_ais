using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Coder;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>One path of the coder's change, as <c>git diff --name-status</c> says it: <c>A</c> added, <c>M</c> modified, <c>D</c> deleted, <c>T</c> type changed.</summary>
internal sealed record CoderFileChange(string Status, string Path);

/// <summary>
/// The result of a coder job (<c>result.json</c>, and what <c>role_status</c> returns): the branch coai made, the commit it made on it, whether the coder's report was valid, and the files that changed — the facts of the
/// repository, with the coder's own report beside them as a claim.
/// </summary>
/// <remarks>
/// <para><b>What is here and what is not.</b> <c>filesChanged</c> comes from <c>git diff --name-status &lt;baseSha&gt; &lt;headSha&gt;</c> and from nothing else: a report that names another file changes nothing in it. <c>report</c> is
/// the coder's report as the reader parsed it, or null when it was not valid after its one repair turn (<c>schemaValid</c> false). There is no <c>green</c>: a report with every <c>met</c> true is a claim, and green comes from
/// the checks of story s4.</para>
/// <para><b><c>fixTurns</c></b> counts the turns the CHECKS sent the coder back for (story s4); this turn makes none, so it is 0. The one repair turn of an invalid report is the job's <c>repairTurns</c>.</para>
/// </remarks>
internal sealed record CoderEnvelope(string JobId, string BaseSha, string HeadSha, string Branch, bool SchemaValid, int FixTurns, IReadOnlyList<CoderFileChange> FilesChanged, CoderReport? Report)
{
    public string ToJson()
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("jobId", JobId);
            json.WriteString("baseSha", BaseSha);
            json.WriteString("headSha", HeadSha);
            json.WriteString("branch", Branch);
            json.WriteBoolean("schemaValid", SchemaValid);
            json.WriteNumber("fixTurns", FixTurns);
            json.WriteStartArray("filesChanged");
            foreach (var change in FilesChanged)
            {
                json.WriteStartObject();
                json.WriteString("status", change.Status);
                json.WriteString("path", change.Path);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            WriteReport(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void WriteReport(Utf8JsonWriter json)
    {
        if (Report is null)
        {
            json.WriteNull("report");

            return;
        }

        json.WriteStartObject("report");
        json.WriteString("status", "ok");
        json.WriteStartArray("dod");
        foreach (var item in Report.Dod)
        {
            json.WriteStartObject();
            json.WriteString("id", item.Id);
            json.WriteBoolean("met", item.Met);
            json.WriteString("evidence", item.Evidence);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteString("notes", Report.Notes);
        json.WriteEndObject();
    }

    /// <summary>
    /// The entries of <c>git diff --name-status -z --no-renames</c>: <c>STATUS NUL PATH NUL</c>, repeated. A trailing piece that is not a whole entry is dropped rather than guessed; the order is git's.
    /// </summary>
    public static IReadOnlyList<CoderFileChange> ParseNameStatus(string output)
    {
        var pieces = output.Split('\0');
        var changes = new List<CoderFileChange>();
        for (var at = 0; at + 1 < pieces.Length; at += 2)
        {
            if (pieces[at].Length > 0 && pieces[at + 1].Length > 0)
            {
                changes.Add(new CoderFileChange(pieces[at], pieces[at + 1]));
            }
        }

        return changes;
    }
}
