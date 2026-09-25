using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Codex as a consultant: one process per turn, the conversation resumed by the thread id codex
/// names on its own <c>--json</c> stream.
/// </summary>
/// <remarks>
/// <para>Two things differ from the REVIEW argv (<c>CodexRuntime.Build</c>), both measured on
/// 2026-09-12 (phase 0b of <c>PLAN_consultant.md</c>). <b>No <c>--ephemeral</c></b>: with it the
/// thread cannot be resumed — <c>thread/resume failed: no rollout found</c> — so a consultation
/// writes a session into the person's own codex store, the trade the chat feature already makes.
/// <b>No schema flag</b>: the consultant answers prose, through <c>-o</c>.</para>
/// <para>A RESUMED turn is a different shape again: <c>codex exec resume</c> accepts neither
/// <c>-s</c> nor <c>-C</c>, so the sandbox rides a config override and the process must run in the
/// directory the thread was started in — which the caller guarantees by launching every turn in the
/// repository.</para>
/// </remarks>
public sealed class CodexConsultant(IReviewerRuntime inner, string vendor = "codex") : IConsultantRuntime
{
    public string Vendor => vendor;

    public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);
        // Named through the one place that also decides what the sweep may delete.
        var outputFile = Path.Combine(launch.OutputDir, ConsultantArtefacts.Name(vendor, ".txt"));
        var argv = (string[])[.. Argv(launch, outputFile)];
        ConsultantLaunches.MustCarryNoLineBreak(argv);

        var request = new ProcessRequest(Executable(launch.Settings), argv, launch.RepoPath)
        {
            Environment = launch.Settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["OPENAI_API_KEY"] = launch.Settings.ApiKey }
                : [],
            StdIn = launch.Prompt,
            Timeout = launch.Settings.Timeout,
        };

        return new ReviewerInvocation(vendor, ConsultantRoles.Consult, request, outputFile, inner, Model: launch.Settings.Model);
    }

    private static IEnumerable<string> Argv(ConsultantLaunch launch, string outputFile) =>
        launch.Handle.Length == 0
            ? ["exec", "-s", "read-only", "--skip-git-repo-check", "--color", "never", "-C", launch.RepoPath, "--json", "-o", outputFile, .. Model(launch.Settings), .. NoMcpServers.CodexArgs(launch.Settings.McpServersToSwitchOff), "-"]
            // UNQUOTED, and measured: `-c` parses its value as TOML and falls back to the raw string,
            // so `sandbox_mode=read-only` arrives as the string this wants. The quoted form worked
            // too, but an embedded double quote inside an argument that reaches cmd.exe through an
            // npm shim is a re-tokenisation waiting for the wrong input. Verified 2026-09-12:
            // a thread resumed with this exact form returned the number planted in turn 1. (gemini, code round.)
            : ["exec", "resume", launch.Handle, "-c", "sandbox_mode=read-only", "--skip-git-repo-check", "--json", "-o", outputFile, .. Model(launch.Settings), .. NoMcpServers.CodexArgs(launch.Settings.McpServersToSwitchOff), "-"];

    private static IEnumerable<string> Model(ReviewerSettings settings) =>
        settings.Model.Length > 0 ? ["-m", settings.Model] : [];

    private static string Executable(ReviewerSettings settings) =>
        settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : "codex";

    /// <summary>The <c>thread_id</c> of the <c>thread.started</c> event, or empty when no well-formed one was said.</summary>
    public string ReadHandle(ProcessResult result)
    {
        foreach (var line in result.StdOut.Split('\n'))
        {
            if (ThreadIdIn(line) is { } id && ConsultantHandle.IsWellFormed(id))
            {
                return id;
            }
        }

        return string.Empty;
    }

    public bool DroppedTheConversation(ProcessResult result) =>
        ConsultantLaunches.Mentions(result, "no rollout found")
        || ConsultantLaunches.Mentions(result, "thread/resume failed");

    private static string? ThreadIdIn(string line)
    {
        if (!line.Contains("thread.started", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line.Trim());
            return document.RootElement.TryGetProperty("thread_id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null; // a log line that happened to mention the event is not the event
        }
    }
}
