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
        if (launch.Handle.Length > 0 && !ConsultantHandle.IsWellFormed(launch.Handle))
        {
            // The service refuses this before any launch; a handle reaching here malformed is a
            // contract violation, and the one place it must never reach is an argv.
            throw new ArgumentException("a consultation handle must be validated before it is launched", nameof(launch));
        }

        var outputFile = Path.Combine(launch.OutputDir, $"{FileName.Safe(vendor)}-consult-{Guid.NewGuid():N}.txt");
        var request = new ProcessRequest(Executable(launch.Settings), [.. Argv(launch, outputFile)], launch.RepoPath)
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
            ? ["exec", "-s", "read-only", "--skip-git-repo-check", "--color", "never", "-C", launch.RepoPath, "--json", "-o", outputFile, .. Model(launch.Settings), "-"]
            : ["exec", "resume", launch.Handle, "-c", "sandbox_mode=\"read-only\"", "--skip-git-repo-check", "--json", "-o", outputFile, .. Model(launch.Settings), "-"];

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
        Mentions(result.StdErr, "no rollout found") || Mentions(result.StdOut, "no rollout found")
        || Mentions(result.StdErr, "thread/resume failed") || Mentions(result.StdOut, "thread/resume failed");

    private static bool Mentions(string text, string phrase) => text.Contains(phrase, StringComparison.OrdinalIgnoreCase);

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
