using CoaiMcp.Server;
using ModelContextProtocol.Server;

namespace CoaiMcp;

/// <summary>
/// The seven tools, wired to <see cref="PanelService"/>. No prefix of their own: the client
/// namespaces by its config key, so these surface as <c>mcp__coai__review_plan</c> and so on.
/// Every answer is a JSON string — trivial schemas, which is what an AOT binary with
/// reflection-based JSON turned off wants, and what agents read anyway.
/// </summary>
internal static class Tools
{
    internal static IEnumerable<McpServerTool> All(PanelService service)
    {
        yield return McpServerTool.Create(
            async () => await service.ProvidersAsync(),
            new McpServerToolCreateOptions
            {
                Name = "providers",
                Title = "Which reviewer vendors are configured and how each authenticates",
                Description = """
                    The health probe: every configured provider with whether its CLI was found, its
                    version, and its auth (own sign-in | vault key | unavailable, with the reason) —
                    plus when the CredsForDevs vault was read (key rotation lands on server restart).
                    Call it before promising anyone a review.
                    """,
                ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
            });

        yield return McpServerTool.Create(
            async (string repoPath, string branch) => await service.OpenAsync(repoPath, branch),
            new McpServerToolCreateOptions
            {
                Name = "open",
                Title = "Open (or resume) the review session for a repo+branch",
                Description = """
                    Opens the session everything else refuses without. Idempotent per repo+branch:
                    the same pair resumes the same session with its rounds intact. Also prunes any
                    worktree a killed session left behind. `repoPath` is the git checkout on THIS
                    machine; `branch` is the branch under review.
                    """,
                ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false,
            });

        yield return McpServerTool.Create(
            async (string repoPath, string branch, string planText) =>
                await service.ReviewPlanAsync(repoPath, branch, planText),
            new McpServerToolCreateOptions
            {
                Name = "review_plan",
                Title = "Send the plan to every enabled provider for independent critique",
                Description = """
                    The plan gate. One reviewer per enabled provider reads `planText` (pass the plan
                    document verbatim) plus a read-only checkout, and answers findings. The reply
                    carries the merged, de-duplicated findings, the honest reviewer count, the
                    verdict (proceed | revise | continue_anyway | call_human | escalated) and what
                    to do next. Then record decisions with `resolve` — every finding, reasons on
                    rejections.
                    """,
                ReadOnly = true, Idempotent = false, Destructive = false, OpenWorld = true,
            });

        yield return McpServerTool.Create(
            async (string repoPath, string branch, string baseRef, string planText) =>
                await service.ReviewCodeAsync(repoPath, branch, baseRef, planText),
            new McpServerToolCreateOptions
            {
                Name = "review_code",
                Title = "Three independent reviewers per provider over the branch diff",
                Description = """
                    The code gate — REFUSES until a plan round reached `proceed`. Per enabled
                    provider, three reviewers (architecture / security+reliability / UX-DX & code
                    performance) read the plan, the shaped diff of `branch` over `baseRef` (lock
                    files and build output excluded, binaries named not inlined), and a read-only
                    worktree pinned to one SHA. Same reply shape and the same `resolve` duty as
                    review_plan.
                    """,
                ReadOnly = true, Idempotent = false, Destructive = false, OpenWorld = true,
            });

        yield return McpServerTool.Create(
            async (string repoPath, string branch, string decisions) =>
                await service.ResolveAsync(repoPath, branch, decisions),
            new McpServerToolCreateOptions
            {
                Name = "resolve",
                Title = "Record accept/reject (with reasons) for the last round's findings",
                Description = """
                    What advances the round. `decisions` is a JSON array, one entry per finding
                    index from the last review reply: [{"finding": 0, "action": "accept"},
                    {"finding": 1, "action": "reject", "reason": "…"}]. A rejection without a
                    reason refuses the whole call; a reasoned rejection is discounted in later
                    rounds unless a reviewer re-raises it with a genuinely new argument.
                    """,
                ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false,
            });

        yield return McpServerTool.Create(
            async (string repoPath, string branch) => await service.StatusAsync(repoPath, branch),
            new McpServerToolCreateOptions
            {
                Name = "status",
                Title = "The session's rounds, counts and verdicts",
                Description = """
                    Re-orientation for a resumed conversation: the stage, rounds run, whether a
                    round awaits `resolve`, and the recorded trail of verdicts. Survives a server
                    restart — sessions are persisted.
                    """,
                ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
            });

        yield return McpServerTool.Create(
            (string question) => AskHuman(question),
            new McpServerToolCreateOptions
            {
                Name = "ask_human",
                Title = "Escalate a decision to the person",
                Description = """
                    For a decision the gate says is a human's. Today no VS Code window listens (the
                    ConnectOtherAIs extension is not installed/running), so this returns a refusal
                    you must surface to the human yourself — verbatim, with the question.
                    """,
                ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false,
            });
    }

    private static string AskHuman(string question) =>
        System.Text.Json.JsonSerializer.Serialize(
            new ErrorAnswer(
                "no VS Code window is listening for escalations on this machine — SURFACE THIS " +
                $"QUESTION TO THE HUMAN YOURSELF and wait for their answer: {question}"),
            ServerJsonContext.Default.ErrorAnswer);
}
