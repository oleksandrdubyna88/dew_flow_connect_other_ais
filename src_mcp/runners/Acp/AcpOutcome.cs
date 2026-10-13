namespace CoaiMcp.Runners.Acp;

/// <summary>One turn to ask of an ACP agent: where to start it, what to start it in, which model, the prompt, and how long it has.</summary>
/// <param name="Executable">The CLI — the profile's default, or the path a row configured.</param>
/// <param name="Cwd">The agent's working folder: the sanitised snapshot, or an empty scratch folder. The read boundary is the caller's.</param>
/// <param name="Model">The value of the profile's model option, set before the prompt (D15).</param>
/// <param name="Prompt">The whole prompt, sent as one text block of <c>session/prompt</c>.</param>
/// <param name="Deadline">The client's own deadline, under the caller's timeout — reaching it ends with a reason where a kill leaves a guess.</param>
public sealed record AcpAsk(string Executable, string Cwd, string Model, string Prompt, TimeSpan Deadline);

/// <summary>How a turn ended — the one thing an exit code is made from.</summary>
public enum AcpEnd
{
    /// <summary>The prompt was answered: <c>stopReason</c> was <c>end_turn</c>.</summary>
    Answered,

    /// <summary>The agent could not be started at all — the executable is not there, or would not run.</summary>
    NotStarted,

    /// <summary>The agent answered one of the client's requests with an error, or stopped for a reason that is not an answer.</summary>
    AgentFailed,

    /// <summary>The client's own deadline struck before the answer.</summary>
    Deadline,

    /// <summary>The stream broke: the agent closed its output, wrote something that is not JSON-RPC, or stopped listening.</summary>
    Protocol,

    /// <summary>The caller's token fired — somebody withdrew the job; never retried, never a failure of the model.</summary>
    Cancelled,
}

/// <summary>Context tokens as the agent reported them: <c>usage_update {used, size}</c> — no input/output split, no cost (D15, D9).</summary>
/// <param name="Captured">False when no <c>usage_update</c> arrived: unknown, never zero.</param>
public sealed record AcpUsage(long ContextUsed, long ContextSize, bool Captured)
{
    public static readonly AcpUsage Unknown = new(0, 0, false);
}

/// <summary>A permission request the client refused: which tool asked, and which option said no.</summary>
/// <param name="Tool">The tool call's title — <c>Write pwned.txt</c>.</param>
/// <param name="Kind">The tool call's kind — <c>edit</c>, <c>execute</c>.</param>
/// <param name="Option">The option kind chosen (<c>reject_once</c>), or <c>cancelled</c> when no reject option was offered.</param>
public sealed record AcpRefusal(string Tool, string Kind, string Option);

/// <summary>One tool call the agent announced, with the last status it reported.</summary>
public sealed record AcpToolCall(string Id, string Title, string Kind, string Status);

/// <summary>One <c>fs/read_text_file</c> the agent asked for, and what the boundary decided.</summary>
public sealed record AcpReadRecord(string Path, bool Served, string Reason);

/// <summary>Everything one turn produced, however it ended.</summary>
/// <param name="End">How it ended.</param>
/// <param name="Answer">The <c>agent_message_chunk</c> text, concatenated — present on a failure too, as far as it got.</param>
/// <param name="Reason">The sentence for a non-answer: the agent's error, the deadline, the broken stream.</param>
/// <param name="StopReason">The prompt's <c>stopReason</c> when one arrived, else empty.</param>
/// <param name="SessionId">The session the agent minted, for a later resume (S5), else empty.</param>
/// <param name="Usage">The last <c>usage_update</c>, or unknown.</param>
/// <param name="Refusals">Every permission request, each refused.</param>
/// <param name="ToolCalls">Every tool call announced, for the trace.</param>
/// <param name="Reads">Every read asked for, served or refused.</param>
/// <param name="StdErr">The agent's stderr, bounded — where a CLI writes its own diagnosis.</param>
/// <param name="AgentPid">The agent's process id, 0 when it never started — what a test checks is gone, and what a trace names.</param>
/// <param name="DroppedTraceEntries">How many refusals, tool calls and reads the trace caps dropped — said, never silently lost.</param>
/// <param name="TruncatedTraceFields">How many retained trace fields were cut to the per-field limit, an ellipsis marking each.</param>
public sealed record AcpOutcome(
    AcpEnd End,
    string Answer,
    string Reason,
    string StopReason,
    string SessionId,
    AcpUsage Usage,
    IReadOnlyList<AcpRefusal> Refusals,
    IReadOnlyList<AcpToolCall> ToolCalls,
    IReadOnlyList<AcpReadRecord> Reads,
    string StdErr,
    int AgentPid,
    int DroppedTraceEntries = 0,
    int TruncatedTraceFields = 0);
