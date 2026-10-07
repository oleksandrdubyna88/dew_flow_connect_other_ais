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
/// the tree it is reviewing is a different program. For a CONFINED reviewer
/// (<see cref="ReviewerSettings.Confined"/>) the same flag also names every tool that reaches past
/// the prompt — see <see cref="ReachTools"/> for what was verified and what was not.</para>
/// <para>`json` rather than `text` because this is the ONE vendor that prices its own run: the
/// envelope carries `usage` and `total_cost_usd`, measured against the installed CLI. The answer
/// then lives in `result`, which is what <see cref="ReadAnswer"/> is for.</para>
/// </remarks>
public sealed class ClaudeRuntime(string id = "claude") : IReviewerRuntime
{
    /// <summary>The claude CLI — what a launch starts when no path is configured, and what the probe asks.</summary>
    /// <remarks>
    /// Not the row id, which the interface's default would have been: a second claude row (`claude-2`) made the probe start
    /// a program of that name and report a working reviewer as missing (todo/PLAN_one_model_catalog.md, epic 2, story 1).
    /// </remarks>
    public string DefaultExecutable => "claude";

    /// <summary>The tools no reviewer ever gets: the ones that change the tree it is reviewing.</summary>
    private static readonly string[] WriteTools = ["Edit", "Write", "NotebookEdit"];

    /// <summary>
    /// What a confined reviewer loses on top of <see cref="WriteTools"/>: everything that reaches
    /// past its prompt — the filesystem, a shell, the web, and a sub-agent that would have all three.
    /// </summary>
    /// <remarks>
    /// <para>The flag was verified against the installed CLI's <c>--help</c> before these names were
    /// written: <c>--disallowedTools, --disallowed-tools &lt;tools...&gt;</c>, read on
    /// <b>claude CLI 2.1.258</b>, 2026-09-10. (An earlier draft of this comment also named 2.1.34;
    /// that number came from a brief written from memory rather than from a <c>--version</c>, and it
    /// is removed rather than kept beside the measured one — a version nobody read is not evidence.)
    /// The tool names are the ones the CLI's own permission prompts use.
    /// The Team server runs the CLI it has installed, not this one, which is why the plan's deviation
    /// section hands the check that the installed binary accepts this argv to <c>POST_DEPLOY.md</c>:
    /// a flag the CLI does not know is a launch that fails, and that is observable only there.</para>
    /// <para><c>Task</c> and <c>Agent</c> are BOTH listed because the sub-agent tool has carried both
    /// names across CLI versions, and a name in this list the CLI does not know is inert — so listing
    /// the one it does not use costs nothing, and omitting the one it does would leave a reviewer a
    /// way to <c>Read</c> through a child it may not <c>Read</c> with itself.</para>
    /// <para>This is a request to the CLI, not an observed effect. Whether
    /// <c>claude -p --permission-mode plan</c> would have executed <c>Bash</c> in a non-interactive
    /// run at all was NOT measured (the audit of 2026-09-09 said so, and this does not claim
    /// otherwise). The denial costs nothing either way, which is why it does not wait for the
    /// measurement; what is asserted in the tests is what is sent.</para>
    /// </remarks>
    private static readonly string[] ReachTools =
        ["Bash", "Read", "Glob", "Grep", "WebFetch", "WebSearch", "Task", "Agent"];

    public string Provider => id;

    private static string[] DisallowedTools(ReviewerSettings settings) =>
        settings.Confined ? [.. WriteTools, .. ReachTools] : WriteTools;

    public ReviewerInvocation Build(
        string role,
        string prompt,
        string worktreePath,
        string schemaFilePath,
        string outputDir,
        ReviewerSettings settings)
    {
        var request = new ProcessRequest(
            settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : DefaultExecutable,
            [
                "-p",
                "--output-format", "json",
                "--permission-mode", "plan",
                "--disallowedTools", .. DisallowedTools(settings),
                // No MCP server at all: the person's own list held coai itself (issue #514).
                NoMcpServers.ClaudeFlag,
                "--add-dir", worktreePath,
                .. settings.Model.Length > 0 ? (string[])["--model", settings.Model] : [],
                // A row's effort, as the installed CLI spells it (2.1.289: `--effort <level>`; the levels are
                // shared/feature-availability.json's). An older CLI refuses the flag itself — VendorDiagnosis names it.
                .. settings.ReasoningEffort.Length > 0 ? (string[])["--effort", settings.ReasoningEffort] : [],
                // The row's fast mode, as a one-key settings file (todo/PLAN_fast_mode.md).
                .. ClaudeFastMode.Args(settings),
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
        return new ReviewerInvocation(Provider, role, request, OutputFile: string.Empty, this, Model: settings.Model, Effort: settings.ReasoningEffort);
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
    /// The failure the CLI reports in its own envelope, on stdout, where <c>--output-format json</c> puts it.
    /// </summary>
    /// <remarks>
    /// <para><b>Measured 2026-09-29.</b> The benchmark lost 44 Fable reviewer cells to
    /// <c>exit 1 (the CLI said nothing on stderr)</c> while the envelope said <i>"You've hit your monthly spend limit"</i>
    /// (HTTP 429); an old CLI refusing a newer model says so the same way (HTTP 400). Both envelopes carry
    /// <c>"subtype":"success"</c> beside <c>"is_error":true</c>, so <c>is_error</c> decides, never <c>subtype</c>.</para>
    /// <para>A reason only when the root is an object whose <c>is_error</c> is the boolean <c>true</c> and whose
    /// <c>result</c> is a non-blank string — so a successful review's own text can never be read as its failure. The
    /// sentence is collapsed to one line (the round summary is one line) and gets <c>(HTTP n)</c> when
    /// <c>api_error_status</c> is an integer. Anything else is not a reason and never an exception: this runs on the path
    /// of a reviewer that has already failed, and <c>GetString</c>/<c>TryGetProperty</c> throw
    /// <c>InvalidOperationException</c>, not <c>JsonException</c>, on the wrong kind (the codex adapter's lesson).</para>
    /// </remarks>
    public string WhyItFailed(ReviewerInvocation invocation, ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.StdOut.Trim());
            return FailureSentence(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    /// <summary>The envelope's <c>result</c> when the envelope says it failed; empty otherwise.</summary>
    private static string FailureSentence(JsonElement root) =>
        SaysItFailed(root) && root.TryGetProperty("result", out var reason) && reason.ValueKind == JsonValueKind.String
            ? WithStatus(OneLine(reason.GetString() ?? string.Empty), root)
            : string.Empty;

    /// <summary><c>is_error</c> as the boolean <c>true</c> — never <c>subtype</c>, which says "success" beside the error.</summary>
    private static bool SaysItFailed(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True;

    private static string OneLine(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string WithStatus(string sentence, JsonElement root) =>
        sentence.Length == 0 ? string.Empty : sentence + HttpStatus(root);

    private static string HttpStatus(JsonElement root) =>
        root.TryGetProperty("api_error_status", out var status) && status.ValueKind == JsonValueKind.Number && status.TryGetInt32(out var code)
            ? $" (HTTP {code})"
            : string.Empty;

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
                Cost(model.Value),
                // The part of the input that was a cache HIT — counted above as billed input, and named
                // here as the cached subset a follow-up turn's resent prefix is measured by (D25).
                Number(model.Value, "cacheReadInputTokens")));
        }

        return usage;
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private static double? Cost(JsonElement element) =>
        element.TryGetProperty("costUSD", out var value) && value.TryGetDouble(out var usd) ? usd : null;
}
