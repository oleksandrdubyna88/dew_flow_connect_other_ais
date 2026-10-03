using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.QuestionConsult;
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

    /// <summary>The one read tool observed working headless in <c>--mode plan</c>, and the shell's standing.</summary>
    public string Toolbox => AntigravityFollowUps.Toolbox;

    /// <summary>The permission words this launch was denied — the stream's list, and the stderr sentence's.</summary>
    public IReadOnlyList<string> DeniedActions(ReviewerLaunch launched) =>
        launched.Process is { } process ? AntigravityStream.DeniedActions(process.StdOut, process.StdErr) : [];

    /// <summary>A silent launch read by agy's permission words — a denied command, a denied read, or the empty answer.</summary>
    public ConsultFailure SilentFailure(ReviewerLaunch launched, ConsultFailure unexplained) =>
        AntigravityFollowUps.Failure(DeniedActions(launched), unexplained);

    /// <summary>
    /// The same conversation, told the denied permission will not come — when a permission a follow-up can
    /// cure WAS denied, and the stream named the conversation to continue.
    /// </summary>
    /// <remarks>
    /// <para>Both, because each absence means something different. Nothing curable denied is an empty turn
    /// for another reason — or a denial no follow-up was measured against (<see cref="AntigravityFollowUps.For"/>
    /// answers empty for it) — which telling it about permissions would not cure. No id is a conversation
    /// that cannot be continued — a fresh launch would only meet the same denial.</para>
    /// <para>Whether the launch ANSWERED is not asked here. <c>ConsultantTurn</c> asks it before it asks this,
    /// and it is the one place that decides whether a follow-up runs at all; asking twice was two copies of
    /// one decision, waiting to disagree.</para>
    /// <para>The id comes from <see cref="ReadHandle"/>, the scan this adapter already trusts for a resume,
    /// so the conversation continued and the conversation recorded are one reading of one stream; and
    /// <see cref="AntigravityStream.Continue"/> REPLACES the <c>--conversation</c> a resumed turn already
    /// carries rather than adding a second.</para>
    /// </remarks>
    public ReviewerInvocation? FollowUp(ReviewerInvocation first, ReviewerLaunch launched)
    {
        var said = AntigravityFollowUps.For(DeniedActions(launched));
        var conversation = ConversationOf(launched);

        return said.Length > 0 && conversation.Length > 0
            ? AntigravityStream.Continue(first, conversation, said)
            : null;
    }

    private string ConversationOf(ReviewerLaunch launched) =>
        launched.Process is { } process ? ReadHandle(process) : string.Empty;

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);

        return launch.Confinement is LaunchConfinement.Planned planned
            ? Planned(launch, planned.Plan)
            : AsShipped(launch);
    }

    /// <summary>The stuck consultant, as it ships: plan mode, the live checkout added, resumable.</summary>
    private ReviewerInvocation AsShipped(ConsultantLaunch launch)
    {
        var request = Request(launch,
            [
                .. Stream,
                "--mode", "plan",
                .. launch.Handle.Length > 0 ? (string[])["--conversation", launch.Handle] : [],
                .. Model(launch.Settings),
                "--add-dir", launch.RepoPath,
            ],
            launch.RepoPath);

        return new ReviewerInvocation(vendor, ConsultantRoles.Consult, request, string.Empty, inner, Model: launch.Settings.Model);
    }

    /// <summary>
    /// A question row: the measured stream launch (F7) with the plan's flags — <c>--mode plan</c>, and
    /// <c>--add-dir</c> per granted root — one shot, in the plan's directory.
    /// </summary>
    private ReviewerInvocation Planned(ConsultantLaunch launch, Confinement.Planned plan)
    {
        var request = Request(launch, [.. Stream, .. plan.Flags, .. Model(launch.Settings)], ConsultantLaunches.Cwd(launch, plan));

        return new ReviewerInvocation(
            vendor, ConsultantRoles.Question, ConsultantLaunches.ForQuestion(request), string.Empty, inner, Model: launch.Settings.Model);
    }

    /// <summary>
    /// The stream launch both shapes share. <c>--print=</c> is empty ON PURPOSE, as for a review: the
    /// flag is mandatory in stream mode and a value here is refused outright — and `--print &lt;value&gt;`
    /// swallows the next flag (F7).
    /// </summary>
    private static readonly string[] Stream = ["--print=", "--input-format", "stream-json", "--output-format", "stream-json"];

    private static IEnumerable<string> Model(ReviewerSettings settings) =>
        settings.Model.Length > 0 ? ["--model", settings.Model] : [];

    private static ProcessRequest Request(ConsultantLaunch launch, string[] arguments, string cwd)
    {
        var request = new ProcessRequest(
            launch.Settings.ExecutablePath.Length > 0 ? launch.Settings.ExecutablePath : AntigravityStream.InstalledExecutable,
            arguments,
            cwd)
        {
            Environment = launch.Settings.ApiKey.Length > 0
                ? new Dictionary<string, string?> { ["ANTIGRAVITY_API_KEY"] = launch.Settings.ApiKey }
                : [],
            // Serialised, never interpolated: a prompt contains quotes and newlines.
            StdIn = AntigravityStream.UserMessage(launch.Prompt),
            Timeout = launch.Settings.Timeout,
        };
        ConsultantLaunches.MustCarryNoLineBreak(request.Arguments);

        return request;
    }

    /// <summary>The <c>conversation_id</c> of the last event that names one.</summary>
    /// <remarks>
    /// Read from the LAST rather than the first, and from any event rather than only the result: a
    /// killed turn has no result event at all, and the id is on every <c>step_update</c> before it —
    /// which is exactly the turn whose handle must survive.
    /// </remarks>
    public string ReadHandle(ProcessResult result)
    {
        // Scanned from the END, and stopping at the first one found. A long turn with several steps
        // emits thousands of events; splitting the whole stream into an array to keep the last match
        // allocated all of them to use one. The id is the same on every event that carries it, so the
        // last is reached first this way. (gemini, code round.)
        var stdout = result.StdOut.AsSpan();
        for (var end = stdout.Length; end > 0;)
        {
            var start = stdout[..end].LastIndexOf('\n') + 1;
            if (ConversationIdIn(stdout[start..end].ToString()) is { } id && ConsultantHandle.IsWellFormed(id))
            {
                return id;
            }

            end = start - 1;
        }

        return string.Empty;
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
