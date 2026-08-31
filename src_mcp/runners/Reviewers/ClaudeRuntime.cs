using System.Text.Json;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// A SECOND Claude, reviewing the first one's work through the Claude Code CLI.
/// </summary>
/// <remarks>
/// <para>The product is about other vendors, and this is not one — so it is worth saying why it
/// belongs. A reviewer's value here is that it cannot see the author's reasoning, and a separate
/// `claude -p` process cannot: it gets the plan and the diff, nothing of the conversation that
/// produced them. A cheaper model reviewing a stronger one's work is also the ordinary case, not
/// an odd one — and the CLI is already installed and signed in on the machines this ships to.</para>
/// <para>Flags verified against the installed CLI before being written here: `-p` prints and
/// exits, `--output-format json` wraps the answer in an envelope, and `--permission-mode plan` is
/// the read-only mode. `--disallowedTools` names the write tools anyway: a reviewer that can edit
/// the tree it is reviewing is a different program.</para>
/// <para>`json` rather than `text` because this is the ONE vendor that prices its own run: the
/// envelope carries `usage` and `total_cost_usd`, measured against the installed CLI. The answer
/// then lives in `result`, which is what <see cref="ReadAnswer"/> is for.</para>
/// </remarks>
public sealed class ClaudeRuntime : IReviewerRuntime
{
    public string Provider => "claude";

    public ReviewerInvocation Build(
        ReviewRole role,
        string prompt,
        string worktreePath,
        string schemaFilePath,
        string outputDir,
        ReviewerSettings settings)
    {
        var request = new ProcessRequest(
            settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : "claude",
            [
                "-p",
                "--output-format", "json",
                "--permission-mode", "plan",
                "--disallowedTools", "Edit", "Write", "NotebookEdit",
                "--add-dir", worktreePath,
                .. settings.Model.Length > 0 ? (string[])["--model", settings.Model] : [],
            ],
            worktreePath)
        {
            // The prompt rides stdin, like every other vendor here: on Windows these CLIs are
            // shims, and a multi-line argument is truncated at its first newline.
            StdIn = prompt,
            Environment = settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = settings.ApiKey }
                : new Dictionary<string, string?>(),
            Timeout = settings.Timeout,
        };
        return new ReviewerInvocation(Provider, role, request, OutputFile: string.Empty, this);
    }

    /// <summary>The review is the envelope's <c>result</c> string; the rest is metadata.</summary>
    public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.StdOut);
            return document.RootElement.TryGetProperty("result", out var answer) && answer.ValueKind == JsonValueKind.String
                ? answer.GetString()
                : null;
        }
        catch (JsonException)
        {
            // Not the envelope at all — hand the raw text on, so a CLI that ignored the flag still
            // produces a review rather than a silent failure.
            return result.StdOut;
        }
    }
}
