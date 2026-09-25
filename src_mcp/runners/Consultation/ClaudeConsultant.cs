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
/// <para><b>What it may do, and the line between the two.</b> A consultant is asked here to READ the
/// tree, so <c>Read</c>, <c>Glob</c> and <c>Grep</c> stay allowed — denying them as a CONFINED
/// reviewer does would leave it judging the prompt alone, which is the one thing it exists not to do.
/// Everything that can WRITE is denied, and that list includes <c>Bash</c>: `--permission-mode plan`
/// is the CLI's promise and a shell is a way around it, since `rm`, `mv` and `sed -i` change a tree
/// no edit tool was ever asked for. The gate's security reviewer named exactly that on this story's
/// plan round. <c>WebFetch</c> and <c>WebSearch</c> go with them — not because they write, but
/// because a consultant reading an unreviewed tree has no reason to reach the network, and the
/// cheapest way to not exfiltrate a secret is to have no tool that could.</para>
/// <para>The filesystem invariant remains the check behind all of this: a flag is the vendor's
/// promise, and the invariant is ours.</para>
/// </remarks>
public sealed class ClaudeConsultant(IReviewerRuntime inner, string vendor = "claude") : IConsultantRuntime
{
    /// <summary>
    /// Denied to a consultant: everything that can change the tree, and everything that reaches the
    /// network. <c>Read</c>, <c>Glob</c> and <c>Grep</c> are deliberately absent from this list.
    /// </summary>
    /// <remarks>
    /// <c>Task</c> and <c>Agent</c> are both named because the sub-agent tool has carried both names
    /// across CLI versions and an unknown name here is inert — listing the one the CLI does not use
    /// costs nothing, and omitting the one it does would leave a way to run a shell through a child.
    /// </remarks>
    private static readonly string[] Denied =
        ["Edit", "Write", "NotebookEdit", "Bash", "WebFetch", "WebSearch", "Task", "Agent"];

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
                "--disallowedTools", .. Denied,
                // No MCP server at all, like a reviewer (issue #514).
                NoMcpServers.ClaudeFlag,
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

    /// <summary>
    /// The CLI's own refusal to resume — read from STDERR and a failed exit only.
    /// </summary>
    /// <remarks>
    /// Never from stdout, which carries the model's ANSWER: a consultant advising about an error
    /// message would have its perfectly good turn discarded and its handle reset for quoting the
    /// words "conversation not found". Stderr plus a non-zero exit is the CLI speaking; stdout is the
    /// model speaking. (codex, code round.)
    /// </remarks>
    public bool DroppedTheConversation(ProcessResult result) =>
        result.ExitCode != 0
        && (result.StdErr.Contains("No conversation found", StringComparison.OrdinalIgnoreCase)
            || result.StdErr.Contains("session not found", StringComparison.OrdinalIgnoreCase)
            || result.StdErr.Contains("No session found", StringComparison.OrdinalIgnoreCase));
}
