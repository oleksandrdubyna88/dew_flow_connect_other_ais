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
    public static ReviewerSettings Settings(ProviderSettings row, string model, TimeSpan timeout, string dataDir) => new(row.Provider)
    {
        ExecutablePath = row.ExecutablePath,
        Model = model,
        Timeout = timeout,
        DataDir = dataDir,
        // A consultant starts no MCP server either (issue #514).
        McpServersToSwitchOff = NoMcpServers.CodexConfigured(Environment.GetEnvironmentVariable),
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
