using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Coder;
using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>Which coder prompt a job's model was sent on turn 0, and its hash: the id of the vendor's prompt (<c>coder</c> is claude's) and the sha256 of the text as rendered — the person's override, the task and the instruction block included. Never the text.</summary>
internal sealed record CoderPromptRecord(string Id, string Sha256)
{
    /// <summary>No turn 0 was sent (a job that ended before any model was launched).</summary>
    public static CoderPromptRecord None { get; } = new(string.Empty, string.Empty);

    public void Write(Utf8JsonWriter json)
    {
        json.WriteStartObject("prompt");
        json.WriteString("id", Id);
        json.WriteString("sha256", Sha256);
        json.WriteEndObject();
    }
}

/// <summary>One path of the coder's change, as <c>git diff --name-status</c> says it: <c>A</c> added, <c>M</c> modified, <c>D</c> deleted, <c>T</c> type changed.</summary>
internal sealed record CoderFileChange(string Status, string Path);

/// <summary>
/// The result of a coder job (<c>result.json</c>, and what <c>role_status</c> returns): the branch coai made, the commit it made on it, whether the coder's report was valid, and the files that changed — the facts of the
/// repository, with the coder's own report beside them as a claim.
/// </summary>
/// <remarks>
/// <para><b>What is here and what is not.</b> <c>filesChanged</c> comes from <c>git diff --name-status &lt;baseSha&gt; &lt;headSha&gt;</c> and from nothing else: a report that names another file changes nothing in it. <c>report</c> is
/// the coder's report as the reader parsed it, or null when it was not valid after its one repair turn (<c>schemaValid</c> false). <c>green</c> is the CHECKS' word (<see cref="CoderChecksReport"/>), never the report's: a report with every
/// <c>met</c> true is a claim.</para>
/// <para><b><c>fixTurns</c></b> counts the turns the CHECKS sent the coder back for (story s4). The one repair turn of an invalid report is the job's <c>repairTurns</c>.</para>
/// <para><b>The checks fields</b> (when the job reached its checks): <c>green</c>, <c>checksState</c> (<c>green</c>, <c>failed</c>, <c>not_configured</c>, <c>toolchain_refused</c>, <c>boundary_refused</c>, <c>tree_changed</c>), <c>checksNote</c>, <c>format</c>
/// (the slot of the order a formatter has: <c>not_configured</c> when the toolchain file names none), <c>checks</c> (the steps of the LAST round: <c>{project, step, argv, exitCode, durationMs, logRef}</c>), <c>rounds</c> (every round: its turn, the commit it
/// checked and its state), <c>fixRounds</c> (the cap and, when the setting was not a whole number from 0 to 10, the note saying so) and <c>lastDigest</c> (what the last failed step went back to the coder as).</para>
/// <para><b><c>mainCheckout</c></b> is EVIDENCE about the repository the job was started in, never a verdict (decision 8, <c>server.md</c> § 16): what two snapshots of it, before and after each launch of the coder, showed. The coder cannot write there (the OS
/// layer holds it read-only), so a path listed is somebody else's edit during the job — a person's. It never makes a job fail; a reader looks at it before the branch is merged.</para>
/// </remarks>
internal sealed record CoderEnvelope(string JobId, string BaseSha, string HeadSha, string Branch, bool SchemaValid, int FixTurns, IReadOnlyList<CoderFileChange> FilesChanged, CoderReport? Report, CoderMainEvidence? MainCheckout = null, CoderChecksReport? Checks = null)
{
    /// <summary>What the instruction step of the job came to (SPEC C9 item 8); <see cref="CoderInstructionsRecord.NotDeclared"/> when the base declares none. Written as <c>instructions</c>: its size and hash, never its text.</summary>
    public CoderInstructionsRecord Instructions { get; init; } = CoderInstructionsRecord.NotDeclared;

    /// <summary>The prompt id and the sha256 of the rendered turn-0 prompt of the model that answered (SPEC C7 item 7): never its text.</summary>
    public CoderPromptRecord Prompt { get; init; } = CoderPromptRecord.None;

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
            WriteMainCheckout(json);
            Checks?.Write(json);
            Instructions.Write(json);
            Prompt.Write(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private void WriteMainCheckout(Utf8JsonWriter json)
    {
        if (MainCheckout is null)
        {
            return;
        }

        json.WriteStartObject("mainCheckout");
        json.WriteBoolean("taken", MainCheckout.Taken);
        json.WriteBoolean("changed", MainCheckout.Changed);
        json.WriteStartArray("changes");
        foreach (var change in MainCheckout.Changes)
        {
            json.WriteStartObject();
            json.WriteString("path", change.Path);
            json.WriteString("what", change.What);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteString("note", MainCheckout.Note);
        json.WriteEndObject();
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
