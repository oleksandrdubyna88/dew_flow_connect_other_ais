using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Claude as a consultant: one process per turn, the conversation resumed by the session id the CLI
/// reports in its own JSON envelope.
/// </summary>
/// <remarks>
/// <para>Measured 2026-09-12 with the full review flag set and <c>--add-dir</c> pointed at a real
/// checkout: turn 1 in 5.4 s reporting its <c>session_id</c> unasked, <c>--resume</c> in 4.4 s
/// returning the number planted in turn 1.</para>
/// <para><b>The id is READ, not chosen.</b> <c>--session-id</c> exists and would let the server mint
/// one, which is tempting — and would make <c>Build</c> impure, since a fresh GUID per call is a
/// launch no test can assert. The CLI reports the id either way, so every consultant here has the
/// same shape: run, read the handle off what the process said, resume with it.</para>
/// <para>The write tools stay denied, as they are for a reviewer; the read tools stay ALLOWED,
/// because reading the tree is what a consultant was asked here to do. <c>ReviewerSettings.Confined</c>
/// is therefore never set on this path — that flag is the Team server's case, where the prompt is
/// the whole world.</para>
/// </remarks>
public sealed class ClaudeConsultant(IReviewerRuntime inner, string vendor = "claude") : IConsultantRuntime
{
    public string Vendor => vendor;

    public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);
        var request = new ProcessRequest(
            launch.Settings.ExecutablePath.Length > 0 ? launch.Settings.ExecutablePath : "claude",
            [
                "-p",
                "--output-format", "json",
                "--permission-mode", "plan",
                "--disallowedTools", "Edit", "Write", "NotebookEdit",
                "--add-dir", launch.RepoPath,
                .. launch.Handle.Length > 0 ? (string[])["--resume", launch.Handle] : [],
                .. launch.Settings.Model.Length > 0 ? (string[])["--model", launch.Settings.Model] : [],
            ],
            launch.RepoPath)
        {
            Environment = launch.Settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = launch.Settings.ApiKey }
                : [],
            StdIn = launch.Prompt,
            Timeout = launch.Settings.Timeout,
        };
        ConsultantLaunches.MustCarryNoLineBreak(request.Arguments);

        return new ReviewerInvocation(vendor, ConsultantRoles.Consult, request, string.Empty, inner, Model: launch.Settings.Model);
    }

    /// <summary>The <c>session_id</c> of the envelope, which the CLI reports whether or not it was given one.</summary>
    public string ReadHandle(ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.StdOut);

            return document.RootElement.TryGetProperty("session_id", out var id)
                   && id.ValueKind == JsonValueKind.String
                   && ConsultantHandle.IsWellFormed(id.GetString())
                ? id.GetString()!
                : string.Empty;
        }
        catch (JsonException)
        {
            // A killed turn leaves a truncated envelope, which is not an error here — it is simply
            // a turn whose handle nobody can read, and the record keeps whatever it already had.
            return string.Empty;
        }
    }

    public bool DroppedTheConversation(ProcessResult result) =>
        ConsultantLaunches.Mentions(result, "No conversation found")
        || ConsultantLaunches.Mentions(result, "session not found")
        || ConsultantLaunches.Mentions(result, "No session found");
}
