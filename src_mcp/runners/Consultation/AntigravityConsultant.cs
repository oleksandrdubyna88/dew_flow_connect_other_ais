using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// Antigravity as a consultant: one process per turn, the conversation resumed by the
/// <c>conversation_id</c> its own event stream names.
/// </summary>
/// <remarks>
/// <para>Measured 2026-09-12, and it is the measurement the plan left owed: the same NDJSON stream
/// the reviewer uses, with <c>--add-dir</c> pointed at a real checkout, then
/// <c>--conversation &lt;id&gt;</c> — turn 1 in 9 s, turn 2 in 5 s returning the number planted in
/// turn 1 on the same conversation id. The plan had allowed for this vendor having no resume at all,
/// in which case it would have carried its own transcript; it has one.</para>
/// <para><b>Its usage is CUMULATIVE across the conversation</b> — turn 1 reported 14 138 input
/// tokens and turn 2 reported 30 843, which is turn 1 plus turn 2 rather than turn 2 alone. That is a
/// property of the vendor's own reporting and of the reviewer adapter that reads it, not of this
/// class; it is recorded here because a consultation is the first thing in this product to run the
/// same conversation twice and therefore the first place it is visible.</para>
/// </remarks>
public sealed class AntigravityConsultant(IReviewerRuntime inner, string vendor = "antigravity") : IConsultantRuntime
{
    public string Vendor => vendor;

    public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

    /// <summary>
    /// Measured: turn 1 reported 14 138 input tokens and turn 2 reported 30 843 — turn 1 plus turn 2.
    /// </summary>
    /// <remarks>
    /// The vendor reports the CONVERSATION's total on every turn, and a consultation is the first
    /// thing in this product to run one conversation twice, so this is the first place it shows.
    /// Declared here and subtracted by the caller, which is the only side holding the running total.
    /// (gemini, this story's plan round, and it was right that recording it was not the same as
    /// handling it.)
    /// </remarks>
    public bool UsageIsCumulative => true;

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);
        var request = new ProcessRequest(
            launch.Settings.ExecutablePath.Length > 0 ? launch.Settings.ExecutablePath : AntigravityStream.InstalledExecutable,
            [
                // Empty ON PURPOSE, as for a review: the flag is mandatory in stream mode and a value
                // here is refused outright.
                "--print=",
                "--input-format", "stream-json",
                "--output-format", "stream-json",
                "--mode", "plan",
                .. launch.Handle.Length > 0 ? (string[])["--conversation", launch.Handle] : [],
                .. launch.Settings.Model.Length > 0 ? (string[])["--model", launch.Settings.Model] : [],
                "--add-dir", launch.RepoPath,
            ],
            launch.RepoPath)
        {
            Environment = launch.Settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["ANTIGRAVITY_API_KEY"] = launch.Settings.ApiKey }
                : [],
            // Serialised, never interpolated: a prompt contains quotes and newlines.
            StdIn = AntigravityStream.UserMessage(launch.Prompt),
            Timeout = launch.Settings.Timeout,
        };
        ConsultantLaunches.MustCarryNoLineBreak(request.Arguments);

        return new ReviewerInvocation(vendor, ConsultantRoles.Consult, request, string.Empty, inner, Model: launch.Settings.Model);
    }

    /// <summary>The <c>conversation_id</c> of the last event that names one.</summary>
    /// <remarks>
    /// Read from the LAST rather than the first, and from any event rather than only the result: a
    /// killed turn has no result event at all, and the id is on every <c>step_update</c> before it —
    /// which is exactly the turn whose handle must survive.
    /// </remarks>
    public string ReadHandle(ProcessResult result)
    {
        var found = string.Empty;
        foreach (var line in result.StdOut.Split('\n'))
        {
            if (ConversationIdIn(line) is { } id && ConsultantHandle.IsWellFormed(id))
            {
                found = id;
            }
        }

        return found;
    }

    public bool DroppedTheConversation(ProcessResult result) =>
        ConsultantLaunches.Mentions(result, "conversation not found")
        || ConsultantLaunches.Mentions(result, "no conversation with id");

    private static string? ConversationIdIn(string line)
    {
        if (!line.Contains("conversation_id", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line.Trim());

            return Find(document.RootElement);
        }
        catch (JsonException)
        {
            return null; // a half-written final line, or prose beside the stream
        }
    }

    /// <summary>The id wherever this event carries it — the envelope's own property, or one level in.</summary>
    private static string? Find(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("conversation_id", out var here) && here.ValueKind == JsonValueKind.String)
        {
            return here.GetString();
        }

        foreach (var property in element.EnumerateObject())
        {
            if (Find(property.Value) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
