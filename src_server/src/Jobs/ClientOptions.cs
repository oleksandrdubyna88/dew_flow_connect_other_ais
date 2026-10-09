using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Core.Catalog;

namespace CoaiServer;

/// <summary>The operator's say over a request's contract-2 fields (research/PLAN_one_model_catalog.md E2.5).</summary>
/// <param name="AcceptSystemPrompt">
/// <c>Coai:AcceptClientSystemPrompt</c>, off by default: a client's text in a prompt that runs on the company's accounts is
/// the operator's decision, not the client's.
/// </param>
/// <param name="MaxEffort"><c>Coai:MaxEffort</c> — the highest level a request may ask for; empty for no cap.</param>
public sealed record ClientOptionsPolicy(bool AcceptSystemPrompt = false, string MaxEffort = "");

/// <summary>What the server TOOK from a request's contract-2 fields, and what it did not, with the reason for each.</summary>
/// <param name="Prompt">The prompt the job runs — the request's, with the system prompt placed in it when it was taken.</param>
/// <param name="Effort">The effort the launch applies; empty for none.</param>
/// <param name="SystemPromptSha">The SHA-256 of the system prompt taken, lower-case hex; empty when none was.</param>
public sealed record TakenOptions(
    string Prompt,
    string Effort,
    string SystemPromptSha,
    IReadOnlyList<FieldNoteDto> Dropped,
    IReadOnlyList<FieldNoteDto> Clamped);

/// <summary>
/// The decision over a request's effort and system prompt: taken, clamped, or dropped — and every one not taken as asked is
/// SAID, by field and reason, so "sent" is never mistaken for "applied".
/// </summary>
/// <remarks>
/// <para><b>Effort</b> is applied only where the vendor's runtime LISTS its levels (<c>shared/feature-availability.json</c>:
/// claude). An unmeasured runtime's (codex) is dropped rather than guessed at, and a level past the operator's cap is
/// lowered to the cap.</para>
/// <para><b>A system prompt</b> is placed before the prompt's own finding contract — the client composed the whole prompt,
/// and the contract stays last — or dropped: while the operator switch is off, past
/// <see cref="CatalogLimits.MaxPromptBytes"/>, or when the prompt has no contract heading to place it before.</para>
/// </remarks>
public static class ClientOptions
{
    private const string EffortField = "effort";
    private const string SystemPromptField = "systemPrompt";

    public static TakenOptions Take(ReviewRequestDto request, string runtime, ClientOptionsPolicy policy)
    {
        var (effort, effortDropped, effortClamped) = EffortOf(request.Effort ?? string.Empty, runtime, policy.MaxEffort);
        var (prompt, sha, promptDropped) = PromptOf(request.Prompt ?? string.Empty, request.SystemPrompt ?? string.Empty, policy.AcceptSystemPrompt);

        return new TakenOptions(prompt, effort, sha, [.. effortDropped, .. promptDropped], effortClamped);
    }

    private static (string Effort, FieldNoteDto[] Dropped, FieldNoteDto[] Clamped) EffortOf(string asked, string runtime, string cap)
    {
        var row = FeatureAvailability.Builtin.EffortOf(runtime);
        var refusal = asked.Length == 0 ? string.Empty : EffortRefusal(asked, runtime, row);

        return refusal.Length > 0
            ? (string.Empty, [new FieldNoteDto(EffortField, refusal)], [])
            : Capped(asked, cap, row.Levels);
    }

    private static string EffortRefusal(string asked, string runtime, EffortRow row) => row.Source switch
    {
        "list" when !row.Levels.Contains(asked, StringComparer.Ordinal) =>
            $"'{asked}' is not a level {runtime} takes ({string.Join(", ", row.Levels)})",
        "list" => string.Empty,
        _ => $"{runtime} takes no effort this server can apply ({row.Source}) — the review runs at the vendor's default",
    };

    /// <summary>The level as asked, or the operator's cap when it is past it — a cap that is not one of the levels caps nothing.</summary>
    private static (string Effort, FieldNoteDto[] Dropped, FieldNoteDto[] Clamped) Capped(string asked, string cap, IReadOnlyList<string> levels)
    {
        var limit = IndexIn(levels, cap);

        return limit >= 0 && IndexIn(levels, asked) > limit
            ? (cap, [], [new FieldNoteDto(EffortField, $"'{asked}' is past this server's cap (Coai:MaxEffort); it runs at '{cap}'")])
            : (asked, [], []);
    }

    private static int IndexIn(IReadOnlyList<string> levels, string level) =>
        level.Length == 0 ? -1 : levels.ToList().IndexOf(level);

    private static (string Prompt, string Sha, FieldNoteDto[] Dropped) PromptOf(string prompt, string instruction, bool accept)
    {
        var refusal = instruction.Length == 0 ? string.Empty : PromptRefusal(prompt, instruction, accept);

        return (instruction.Length, refusal.Length) switch
        {
            (0, _) => (prompt, string.Empty, []),
            (_, > 0) => (prompt, string.Empty, [new FieldNoteDto(SystemPromptField, refusal)]),
            _ => (PersonInstruction.PlacedIn(prompt, instruction), Sha(instruction), []),
        };
    }

    private static string PromptRefusal(string prompt, string instruction, bool accept)
    {
        var bytes = Encoding.UTF8.GetByteCount(instruction);

        return (accept, bytes > CatalogLimits.MaxPromptBytes, PersonInstruction.HasContract(prompt)) switch
        {
            (false, _, _) => "this server does not take a client's system prompt (Coai:AcceptClientSystemPrompt is off)",
            (_, true, _) => $"it is {bytes} bytes — at most {CatalogLimits.MaxPromptBytes}",
            (_, _, false) => $"the prompt has no '{PersonInstruction.ContractHeading}' heading to place it before — it was not guessed at",
            _ => string.Empty,
        };
    }

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
