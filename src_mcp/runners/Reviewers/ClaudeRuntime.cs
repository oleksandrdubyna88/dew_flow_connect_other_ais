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

    /// <summary>
    /// Claude reports the same run TWICE and the two disagree — this reads the right one.
    /// </summary>
    /// <remarks>
    /// <para>Measured on a real call: <c>usage</c> said 10 input / 44 output while
    /// <c>modelUsage</c> said 532 / 57 for the same run. <c>usage</c> is the LAST message's
    /// usage; <c>modelUsage</c> is the aggregate across every turn, which is what a multi-turn
    /// review actually consumed — the generic scan read the wrong one and under-reported every
    /// reviewer.</para>
    /// <para>Cache tokens are ADDED here, unlike codex: claude reports
    /// <c>cacheCreationInputTokens</c> and <c>cacheReadInputTokens</c> BESIDE the input count
    /// rather than inside it, and both are billed. Codex's <c>cached_input_tokens</c> is a subset
    /// of its <c>input_tokens</c> and must NOT be added. Getting that backwards is a silent factor
    /// of two in either direction, which is exactly why each vendor reads its own numbers.</para>
    /// </remarks>
    public Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.StdOut.Trim());
            if (document.RootElement.TryGetProperty("modelUsage", out var models) &&
                models.ValueKind == JsonValueKind.Object)
            {
                return Aggregate(models);
            }
        }
        catch (JsonException)
        {
            // Not the envelope we know; the generic scan is a better answer than none.
        }

        return UsageParser.Parse(result.StdOut);
    }

    private static Usage Aggregate(JsonElement models)
    {
        var usage = Usage.None;
        foreach (var model in models.EnumerateObject())
        {
            usage = usage.Add(new Usage(
                Number(model.Value, "inputTokens")
                + Number(model.Value, "cacheCreationInputTokens")
                + Number(model.Value, "cacheReadInputTokens"),
                Number(model.Value, "outputTokens"),
                Cost(model.Value)));
        }

        return usage;
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static double? Cost(JsonElement element) =>
        element.TryGetProperty("costUSD", out var value) && value.TryGetDouble(out var usd) ? usd : null;
}
