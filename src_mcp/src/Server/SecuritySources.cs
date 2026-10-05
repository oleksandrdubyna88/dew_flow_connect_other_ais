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
        var wanted = Wanted(lane, due, files);
        if (wanted.Count == 0) return new Dictionary<string, string>(StringComparer.Ordinal);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CollectionBudget);
        return await ReadFilesAsync(wanted, resolver, deadline.Token, ct);
    }

    private const string DeadlineReached = "Source read deadline reached; remaining source omitted.";

    /// <summary>
    /// The files whose source a due, triggered slice pairing will be given: ranked, none withheld, at most
    /// sixteen — or none, before anything is classified, when no slice pairing is due.
    /// </summary>
    private static IReadOnlyList<FileDiff> Wanted(SecurityLaneSetting lane, IReadOnlyList<SecurityRun> due, IReadOnlyList<FileDiff> files)
    {
        var selected = due.Where(r => r.Context == SecurityContextModes.Slice).Select(r => r.Prompt).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0) return [];
        // The lane's own table, as the roster classifies with — the files read are the ones the due cards were due on.
        var facts = SecuritySignals.Classify(files, lane.Table);
        var prompts = lane.Prompts.Where(p => selected.Contains(p.Id) && SecuritySignals.Triggered(p, facts)).ToArray();
        if (prompts.Length == 0) return [];
        var focus = prompts.SelectMany(p => p.Focus).ToHashSet();
        return [.. SecuritySignals.Rank(facts.Where(f => f.Diff.Text.Length > 0), focus).Take(MaxSourceFiles).Select(f => f.Diff)];
    }

    /// <summary>Each file in turn; the file the deadline interrupts is the last one read.</summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReadFilesAsync(IReadOnlyList<FileDiff> files,
        SourceResolver resolver, CancellationToken deadline, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            result[file.Path] = await ReadFileAsync(file, resolver, deadline, ct);
            if (deadline.IsCancellationRequested) break;
        }
        return result;
    }

    /// <summary>
    /// One file's changed regions, at most four, each window once. The deadline ending the reads is said in
    /// the file's entry; the caller cancelling propagates.
    /// </summary>
    private static async Task<string> ReadFileAsync(FileDiff file, SourceResolver resolver, CancellationToken deadline,
        CancellationToken ct)
    {
        var parts = new List<string>();
        try
        {
            foreach (var span in DiffHunks.ChangedSpans(file.Text).Take(MaxHunksPerFile))
            {
                deadline.ThrowIfCancellationRequested();
                parts.Add((await resolver.ServeChangeAsync(file.Path, Math.Max(1, span.Start), deadline)).Render());
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            parts.Add(DeadlineReached);
        }
        ct.ThrowIfCancellationRequested();
        if (deadline.IsCancellationRequested) parts.Add(DeadlineReached);
        return string.Join("\n", parts.Distinct(StringComparer.Ordinal));
    }
}
