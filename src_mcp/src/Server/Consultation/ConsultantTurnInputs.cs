using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.Context;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// What a consultation turn is BUILT from that does not depend on a record: the launch settings for a vendor row,
/// and the working tree as the consultant is shown it.
/// </summary>
/// <remarks>
/// Extracted from <see cref="ConsultationService"/> when the consultant check (epic 4 of
/// PLAN_the_consultant_works_on_every_vendor.md) came to run the SAME turn without a consultation record: a second
/// copy of either would be a check that tests a launch no consultation makes — the one thing a check must not be.
/// </remarks>
internal static class ConsultantTurnInputs
{
    /// <summary>The settings one consultation launch of <paramref name="row"/> runs under.</summary>
    /// <param name="model">The model already materialised for this turn — the row's, or a record's frozen one.</param>
    /// <param name="keys">
    /// The vault — read only for a row that authenticates with a key (<see cref="TakesAKey"/>). A claude or plain codex
    /// consultant keeps its CLI's own sign-in: a vault key would move it onto per-token billing.
    /// </param>
    /// <param name="overrides">The environment's api overrides — an api row runs with its module's view of them, as a reviewer does.</param>
    public static ReviewerSettings Settings(ProviderSettings row, string model, TimeSpan timeout, string dataDir, VaultKeys keys, Core.Api.ApiOverrides overrides) =>
        RuntimeResolution.NameOf(row.Identity()) == "api" ? WithModule(Plain(row, model, timeout, dataDir, keys), row, overrides) : Plain(row, model, timeout, dataDir, keys);

    /// <summary>
    /// The row's own timeout, else the caller's (todo/PLAN_one_model_catalog.md, C2) — the reviewers' rule
    /// (<c>RosterBuilder.TimeoutFor</c>): an api row keeps the caller's, its own limit being the whole conversation's
    /// (<c>reviewMinutes</c>). Bounds the launch AND the turn's backstop, so neither cuts the other short.
    /// </summary>
    public static TimeSpan TurnTimeout(ProviderSettings row, TimeSpan callers) =>
        row.TimeoutMinutes > 0 && RuntimeResolution.NameOf(row.Identity()) != "api" ? TimeSpan.FromMinutes(row.TimeoutMinutes) : callers;

    /// <summary>
    /// The texts a consultation or question launch must not hand back into a record — the row's system prompt, when it has
    /// one (todo/PLAN_one_model_catalog.md, C2), as a reviewer's (<c>ReviewerInvocation.Redact</c>).
    /// </summary>
    /// <remarks>Trimmed as the composers trim it before sending (epic 4's code round): a CLI echoes what it was SENT, and
    /// whitespace alone is no text — redacting it would rewrite every gap in the child's output.</remarks>
    public static IReadOnlyList<string> Redacted(string systemPrompt) => systemPrompt.Trim() is { Length: > 0 } sent ? [sent] : [];

    /// <summary>An api row's effort, ceiling and thinking switch: the row's over the environment over the module's calibrated defaults.</summary>
    private static ReviewerSettings WithModule(ReviewerSettings settings, ProviderSettings row, Core.Api.ApiOverrides overrides)
    {
        var api = ApiRowView.Of(row, overrides).Effective;

        return settings.WithApi(api);
    }

    private static ReviewerSettings Plain(ProviderSettings row, string model, TimeSpan timeout, string dataDir, VaultKeys keys) => new(row.Provider)
    {
        ApiKey = TakesAKey(row) ? keys.Keys.GetValueOrDefault(row.KeyName, string.Empty) : string.Empty,
        // Only the api runtime reads these two: the module its request is spelled in, and the price of each turn.
        Dialect = row.Dialect,
        Price = row.Price,
        ExecutablePath = row.ExecutablePath,
        Model = model,
        Timeout = TurnTimeout(row, timeout),
        // The row's CLI effort by the reviewers' own rule — claude's level, a local row's; nothing for a runtime that does
        // not take one (C2). An api row's is its module's (WithModule). The panel's local default is a reviewer setting.
        ReasoningEffort = RosterBuilder.EffortFor(row, string.Empty),
        DataDir = dataDir,
        // A consultant starts no MCP server either (issue #514).
        McpServersToSwitchOff = NoMcpServers.CodexConfigured(Environment.GetEnvironmentVariable),
        // The row's fast mode, as for a reviewer (todo/PLAN_fast_mode.md).
        Fast = row.Fast,
    };

    /// <summary>
    /// A row that authenticates with a vault key when it consults: an api row, or a codex row on somebody else's endpoint
    /// (PLAN_one_model_catalog.md E2.3). Every other runtime signs in through its own CLI.
    /// </summary>
    public static bool TakesAKey(ProviderSettings row) => RuntimeResolution.NameOf(row.Identity()) switch
    {
        "api" => true,
        "codex" => row.BaseUrl.Length > 0,
        _ => false,
    };

    /// <summary>The uncommitted change in <paramref name="repo"/>, shaped to the consultant's diff budget — or the sentence for none.</summary>
    public static async Task<string> ShapedTreeAsync(ContextAssembler context, string repo, CancellationToken ct)
    {
        var files = await context.CollectWorkingTreeAsync(repo, ct: ct);

        return files.Count == 0
            ? "(the working tree has no uncommitted change — everything is committed at HEAD)"
            : DiffShaper.Shape(files, ConsultantPrompt.DiffBudget).Text;
    }
}
