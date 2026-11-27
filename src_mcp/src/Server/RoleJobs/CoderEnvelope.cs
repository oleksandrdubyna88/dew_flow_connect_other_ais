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

/// <summary>
/// The CLI release a job's coder ran with, beside the one measured last (owner decision of 2026-11-26): the vendor, the release the CLI's own <c>--version</c> named (empty when none was read), and the last measured release of that vendor (empty when none was). A record for a person to see how far apart
/// the two are; no release ever refuses or stops a job.
/// </summary>
internal sealed record CoderCliRecord(string Vendor, string Installed, string LastMeasured)
{
    /// <summary>What the preflight rows of this vendor do not prove (ids of class <c>release</c>: measured on the release named beside the installed one, never probed before the job); empty for a vendor that says nothing.</summary>
    public string NotProved { get; init; } = string.Empty;

    /// <summary>No CLI was launched (a job that ended before any model).</summary>
    public static CoderCliRecord None { get; } = new(string.Empty, string.Empty, string.Empty);

    public void Write(Utf8JsonWriter json)
    {
        if (Installed.Length == 0 && LastMeasured.Length == 0)
        {
            return;
        }

        json.WriteStartObject("cli");
        json.WriteString("vendor", Vendor);
        json.WriteString("installed", Installed);
        json.WriteString("lastMeasured", LastMeasured);
        if (NotProved.Length > 0)
        {
            json.WriteString("notProvedByPreflight", NotProved);
        }

        json.WriteEndObject();
    }
}

/// <summary>
/// The model a codex coder job launched with (epoch 3 E3 s2 t5; SPEC_CHANGES Q1 item 7 and Q3 item 2): the name as asked and as resolved (a family becomes the newest model the installed CLI lists when the job reaches the entry; a name given in full is the same twice), and the known tools the model does not have
/// (coai's own strings, so they can be shown). Written as <c>model</c>; nothing for another vendor.
/// </summary>
internal sealed record CoderModelRecord(string Vendor, string Asked, string Resolved, IReadOnlyList<string> ToolsAbsent)
{
    /// <summary>No model was recorded (a job that ended before any launch, or a vendor with nothing to say).</summary>
    public static CoderModelRecord None { get; } = new(string.Empty, string.Empty, string.Empty, []);

    public void Write(Utf8JsonWriter json)
    {
        if (Vendor != "codex")
        {
            return;
        }

        json.WriteStartObject("model");
        json.WriteString("asked", Asked);
        json.WriteString("resolved", Resolved);
        json.WriteStartArray("toolsAbsent");
        foreach (var tool in ToolsAbsent)
        {
            json.WriteStringValue(tool);
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}

/// <summary>The real sign-in file of a vendor that signs in with a file, before and after the job (s7 t6 d4): a sha256 and a modification time each — a hash, never text — and whether they differ. coai opens that file for reading only, so a difference is something else's doing.</summary>
internal sealed record CoderSignInRecord(string Vendor, string BeforeSha256, string BeforeModifiedUtc, string AfterSha256, string AfterModifiedUtc, bool Changed)
{
    /// <summary>How many refresh units the job ran for this vendor: each puts the owner's refresh token for the seconds of one run into a unit of coai's own (risk A12) and is one more refresh on coai's copy (A5).</summary>
    public int Refreshes { get; init; }

    public static CoderSignInRecord Of(string vendor, RealSignInState before, RealSignInState after) =>
        new(vendor, before.Sha256, Stamp(before.ModifiedUtc), after.Sha256, Stamp(after.ModifiedUtc), before.Sha256 != after.Sha256 || before.ModifiedUtc != after.ModifiedUtc);

    private static string Stamp(DateTime? time) => time is { } at ? at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    public void Write(Utf8JsonWriter json)
    {
        json.WriteStartObject();
        json.WriteString("vendor", Vendor);
        json.WriteStartObject("before");
        json.WriteString("sha256", BeforeSha256);
        json.WriteString("modifiedUtc", BeforeModifiedUtc);
        json.WriteEndObject();
        json.WriteStartObject("after");
        json.WriteString("sha256", AfterSha256);
        json.WriteString("modifiedUtc", AfterModifiedUtc);
        json.WriteEndObject();
        json.WriteBoolean("changed", Changed);
        json.WriteNumber("refreshes", Refreshes);
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

    /// <summary>The CLI release of the model that produced the result, beside the last measured one. Written as <c>cli</c> when there is one.</summary>
    public CoderCliRecord Cli { get; init; } = CoderCliRecord.None;

    /// <summary>The model a codex coder launched with, as asked and as resolved, and the known tools it does not have. Written as <c>model</c> for codex.</summary>
    public CoderModelRecord Model { get; init; } = CoderModelRecord.None;

    /// <summary>The real sign-in files of the vendors that were given a copy, before and after the job. Written as <c>signIn</c> when there is one.</summary>
    public IReadOnlyList<CoderSignInRecord> SignIn { get; init; } = [];

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
            Cli.Write(json);
            Model.Write(json);
            if (SignIn.Count > 0)
            {
                json.WriteStartArray("signIn");
                foreach (var record in SignIn)
                {
                    record.Write(json);
                }

                json.WriteEndArray();
            }
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
