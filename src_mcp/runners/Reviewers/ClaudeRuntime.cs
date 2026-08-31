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
/// exits, `--output-format text` keeps the answer plain, and `--permission-mode plan` is the
/// read-only mode. `--disallowedTools` names the write tools anyway: a reviewer that can edit the
/// tree it is reviewing is a different program.</para>
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
                "--output-format", "text",
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
        return new ReviewerInvocation(Provider, role, request);
    }
}
