using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>Conservative configured work bound, armed before any round setup.</summary>
internal static class RoundDeadline
{
    internal static TimeSpan For(PanelSettings settings, Stage stage, int roles, int followUps, Serilog.ILogger log)
    {
        var perReviewer = settings.ReviewerTimeout * (1 + followUps);
        var rows = settings.Providers.Where(p => p.Enabled).Select(p => new
        {
            Provider = p,
            Count = (p.Serves(stage) ? roles : 0) + (settings.SecurityLane.Applies(stage)
                ? settings.SecurityLane.Runs.Count(r => r.Vendor == p.Provider && r.Serves(stage)) : 0),
        }).ToArray();
        var cloud = rows.Where(r => r.Provider.Runtime != "local").ToArray();
        var waves = Math.Max(Waves(cloud.Sum(r => r.Count), settings.GlobalConcurrency),
            cloud.Select(r => Waves(r.Count, settings.PerProviderConcurrency)).DefaultIfEmpty(1).Max());
        var engines = rows.Where(r => r.Provider.Runtime == "local").GroupBy(r => LocalRuntime.EngineKey(
            r.Provider.BaseUrl.Length > 0 ? r.Provider.BaseUrl : LocalRuntime.DefaultEndpoint));
        waves = Math.Max(waves, engines.Select(g => Waves(g.Sum(r => r.Count), settings.LocalConcurrency)).DefaultIfEmpty(1).Max());
        var sourceBudget = settings.SecurityLane.Applies(stage)
            && settings.SecurityLane.Runs.Any(r => r.Serves(stage) && r.Context == Core.Security.SecurityContextModes.Slice) ? SecuritySources.CollectionBudget : TimeSpan.Zero;
        var whole = RoundBudget.Expressible(settings.RoundTimeout > TimeSpan.Zero
            ? settings.RoundTimeout : RoundBudget.For(perReviewer, waves, 1) + sourceBudget);
        if (whole < perReviewer) log.Warning("the round limit of {Limit:0} minute(s) is shorter than one reviewer's own {Reviewer:0}; reviewers may be cancelled", whole.TotalMinutes, perReviewer.TotalMinutes);
        return whole;
    }

    private static int Waves(int count, int cap) => Math.Max(1, (int)Math.Ceiling(count / (double)Math.Max(1, cap)));
}
