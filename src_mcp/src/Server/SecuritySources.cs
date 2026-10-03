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

    /// <summary>The lane's configuration alone, for a caller with no round roster to ask.</summary>
    internal static Task<IReadOnlyDictionary<string, string>> ReadAsync(SecurityLaneSetting lane,
        IReadOnlyList<FileDiff> files, SourceResolver resolver, Stage stage, CancellationToken ct) =>
        ReadAsync(lane, lane.Configured(stage), files, resolver, ct);

    /// <param name="due">
    /// The pairings this round will ask (<see cref="SecurityRoster.Due"/>) — never the lane's whole
    /// configuration, so a spent budget or a row that cannot run reads no source at all.
    /// </param>
    internal static async Task<IReadOnlyDictionary<string, string>> ReadAsync(SecurityLaneSetting lane,
        IReadOnlyList<SecurityRun> due, IReadOnlyList<FileDiff> files, SourceResolver resolver, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var selected = due.Where(r => r.Context == SecurityContextModes.Slice).Select(r => r.Prompt).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) return result;
        var facts = SecuritySignals.Classify(files);
        var prompts = lane.Prompts.Where(p => selected.Contains(p.Id) && SecuritySignals.Triggered(p, facts)).ToArray();
        if (prompts.Length == 0) return result;
        var focus = prompts.SelectMany(p => p.Focus).ToHashSet();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CollectionBudget);
        foreach (var file in SecuritySignals.Rank(facts.Where(f => f.Diff.Text.Length > 0), focus).Take(MaxSourceFiles)
            .Select(f => f.Diff))
        {
            var parts = new List<string>();
            try
            {
                foreach (var span in DiffHunks.ChangedSpans(file.Text).Take(MaxHunksPerFile))
                {
                    deadline.Token.ThrowIfCancellationRequested();
                    parts.Add((await resolver.ServeChangeAsync(file.Path, Math.Max(1, span.Start), deadline.Token)).Render());
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                parts.Add("Source read deadline reached; remaining source omitted.");
            }
            ct.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested) parts.Add("Source read deadline reached; remaining source omitted.");
            result[file.Path] = string.Join("\n", parts.Distinct(StringComparer.Ordinal));
            if (deadline.IsCancellationRequested) break;
        }
        return result;
    }
}
