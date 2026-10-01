using CoaiMcp.Core.Context;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Feature;

namespace CoaiMcp.Server;

/// <summary>Bounded, cached reads from the round's pinned head. No working-tree source is read.</summary>
internal static class SecuritySources
{
    internal const int MaxSourceFiles = 16;
    internal const int MaxHunksPerFile = 4;
    internal static readonly TimeSpan CollectionBudget = TimeSpan.FromSeconds(30);

    internal static async Task<IReadOnlyDictionary<string, string>> ReadAsync(SecurityLaneSetting lane,
        IReadOnlyList<FileDiff> files, SourceResolver resolver, Stage stage, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!lane.Applies(stage) || !lane.Runs.Any(r => r.Serves(stage) && r.Context == "slice")) return result;
        var facts = SecuritySignals.Classify(files);
        var selected = lane.Runs.Where(r => r.Serves(stage) && r.Context == "slice" && r.Refusal.Length == 0)
            .Select(r => r.Prompt).ToHashSet(StringComparer.Ordinal);
        var prompts = lane.Prompts.Where(p => selected.Contains(p.Id) && p.Refusal.Length == 0
            && SecuritySignals.Triggered(p, facts)).ToArray();
        if (prompts.Length == 0) return result;
        var focus = prompts.SelectMany(p => p.Focus).ToHashSet();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CollectionBudget);
        foreach (var file in facts.Where(f => f.Diff.Text.Length > 0)
            .OrderByDescending(f => f.Signals.Count(focus.Contains)).Take(MaxSourceFiles))
        {
            var parts = new List<string>();
            try
            {
                foreach (var span in DiffHunks.ChangedSpans(file.Diff.Text).Take(MaxHunksPerFile))
                    parts.Add((await resolver.ServeChangeAsync(file.Diff.Path, Math.Max(1, span.Start), deadline.Token)).Render());
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                parts.Add("Source read deadline reached; remaining source omitted.");
            }
            result[file.Diff.Path] = string.Join("\n", parts.Distinct(StringComparer.Ordinal));
            if (deadline.IsCancellationRequested) break;
        }
        return result;
    }
}
