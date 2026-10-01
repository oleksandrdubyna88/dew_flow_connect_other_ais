using System.Text;
using CoaiMcp.Core.Context;
using CoaiMcp.Core.Findings;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Core.Security;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>Extra work inside an ordinary round, using the same runtime and launch settings.</summary>
internal sealed class SecurityRoster(PanelSettings settings, RolePrompts prompts,
    Func<ProviderSettings, bool> canRun, Func<ProviderSettings, IReviewerRuntime?> runtimeFor,
    Func<ProviderSettings, IReviewerRuntime, ReviewerSettings> launchFor, Serilog.ILogger log)
{
    internal RoundWork Append(RoundWork ordinary, IReadOnlyList<FileDiff> files, Stage stage, int round,
        IReadOnlyDictionary<string, string>? sources = null)
    {
        var lane = settings.SecurityLane;
        if (!lane.Applies(stage) || !settings.Providers.Any(p => p.Serves(stage))
            || settings.Rounds.EnabledRolesOf(stage).Count == 0) return ordinary;
        var facts = SecuritySignals.Classify(files);
        var work = new List<ReviewerWork>();
        var skipped = new List<SkippedRole>();
        var excluded = new List<ExcludedRole>();
        foreach (var run in lane.Runs.Where(r => r.Serves(stage)))
        {
            try
            {
                var prompt = lane.Prompts.First(p => p.Id == run.Prompt);
                var reason = Skip(run, prompt, round);
                if (reason.Length > 0) { skipped.Add(new($"{run.Vendor}/{run.Prompt}", reason)); continue; }
                if (!SecuritySignals.Triggered(prompt, facts))
                { RecordUnmatched(run, facts, files.Count - facts.Count, skipped, excluded); continue; }
                Prepare(run, prompt, facts, sources, files.Count - facts.Count, work, excluded);
            }
            catch (Exception e)
            {
                log.Warning(e, "Could not prepare security review {Vendor}/{Prompt}", run.Vendor, run.Prompt);
                excluded.Add(new(run.Vendor, run.Prompt, $"could not prepare security review ({e.GetType().Name})"));
            }
        }
        return ordinary with
        {
            Reviewers = RosterBuilder.LocalRowsFirst([.. ordinary.Reviewers, .. work]),
            NotAsked = [.. ordinary.NotAsked, .. skipped],
            Excluded = [.. ordinary.Excluded, .. excluded],
            SecurityActive = true,
        };
    }

    private void Prepare(SecurityRun run, SecurityPrompt prompt, IReadOnlyList<SecurityFile> facts,
        IReadOnlyDictionary<string, string>? sources, int omitted, List<ReviewerWork> work, List<ExcludedRole> excluded)
    {
        var provider = settings.Providers.First(p => p.Provider == run.Vendor);
        var refusal = Refusal(provider);
        if (refusal.Length > 0) { excluded.Add(new(run.Vendor, run.Prompt, refusal)); return; }
        var body = ReadPrompt(run.Prompt);
        if (string.IsNullOrWhiteSpace(body))
        { excluded.Add(new(run.Vendor, run.Prompt, $"prompt unavailable or over 64 KiB; write text at {prompts.FileToWrite(run.Prompt)}")); return; }
        var pack = SecurityContext.Compose(body, prompt, facts, run.Context, run.ContextTokens, sources,
            launchFor(provider, runtimeFor(provider)!).MaxTokens, omitted);
        if (pack.Refusal.Length > 0) { excluded.Add(new(run.Vendor, run.Prompt, pack.Refusal)); return; }
        work.Add(Build(provider, run, pack.Text));
    }

    private string Skip(SecurityRun run, SecurityPrompt prompt, int round) =>
        round > settings.SecurityLane.MaxRounds ? "security lane round budget spent"
        : run.Refusal.Length > 0 ? run.Refusal
        : prompt.Refusal.Length > 0 ? prompt.Refusal
        : string.Empty;

    private static void RecordUnmatched(SecurityRun run, IReadOnlyList<SecurityFile> facts, int omitted,
        List<SkippedRole> skipped, List<ExcludedRole> excluded)
    {
        var oversized = facts.Count(f => f.DetectionIncomplete);
        if (oversized > 0 || omitted > 0)
            excluded.Add(new(run.Vendor, run.Prompt,
                $"trigger coverage incomplete: {oversized} oversized diffs and {omitted} files beyond the detector limit were not inspected"));
        else skipped.Add(new($"{run.Vendor}/{run.Prompt}",
            "no matching trigger in this committed change; prior fixes were not verified by this run"));
    }

    private string Refusal(ProviderSettings provider) =>
        !provider.Enabled ? "reviewer row disabled"
        : !canRun(provider) ? "reviewer runtime or credentials unavailable"
        : runtimeFor(provider) is RemoteRuntime ? "this Team runtime does not advertise the security schema; use a local CLI or API reviewer row"
        : runtimeFor(provider) is ApiRuntime ? ApiRowView.Of(provider, settings.ApiOverrides).Refusal
        : string.Empty;

    private string ReadPrompt(string id)
    {
        var path = prompts.FileToWrite(id);
        if (File.Exists(path) && new FileInfo(path).Length > SecurityContext.MaxPromptBytes) return string.Empty;
        var text = File.Exists(path) ? prompts.Written(id) : RolePrompts.ShippedDefaultFor(id, optional: true);
        if (Encoding.UTF8.GetByteCount(text) > SecurityContext.MaxPromptBytes) return string.Empty;
        var trimmed = text.Trim();
        return trimmed.StartsWith("<!-- OPERATOR:", StringComparison.Ordinal)
            && trimmed.IndexOf("-->", StringComparison.Ordinal) == trimmed.Length - 3 ? string.Empty : text;
    }

    private ReviewerWork Build(ProviderSettings provider, SecurityRun run, string prompt)
    {
        var runtime = runtimeFor(provider)!;
        var launch = launchFor(provider, runtime) with { Conversation = ConversationKey.Of(provider.Provider, run.Prompt, prompt) };
        var schema = SchemaFile.Ensure(settings.DataDir, SchemaShape.Security);
        var output = Directory.CreateTempSubdirectory("coai-answers-").FullName;
        var scratch = Directory.CreateTempSubdirectory("coai-noworkspace-").FullName;
        return new(runtime.Build(run.Prompt, prompt, scratch, schema, output, launch),
            runtime.Build(run.Prompt, prompt + RepairInstruction.Text, scratch, schema, output, launch),
            run.Prompt, Encoding.UTF8.GetByteCount(prompt))
        { IsSecurity = true };
    }
}
