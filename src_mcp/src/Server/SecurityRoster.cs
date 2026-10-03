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
    /// <summary>The ONE answer to "will the lane add work to this stage's rounds at all".</summary>
    private bool Applies(Stage stage) => settings.SecurityLane.Applies(stage)
        && settings.Providers.Any(p => p.Serves(stage)) && settings.Rounds.EnabledRolesOf(stage).Count > 0;

    /// <summary>
    /// The pairings that will be asked in this round if their trigger matches — the decision
    /// <see cref="Append"/> makes, offered to the source reader so it reads nothing for a pairing that
    /// cannot run: a spent lane budget, a broken pairing, a reviewer row that cannot take the work.
    /// </summary>
    internal IReadOnlyList<SecurityRun> Due(Stage stage, int round) =>
        Applies(stage) && WithinBudget(round)
            ? [.. settings.SecurityLane.Configured(stage)
                .Where(r => settings.Providers.Any(p => p.Provider == r.Vendor && Refusal(p).Length == 0))]
            : [];

    internal RoundWork Append(RoundWork ordinary, IReadOnlyList<FileDiff> files, Stage stage, int round,
        IReadOnlyDictionary<string, string>? sources = null)
    {
        if (!Applies(stage)) return ordinary;
        var facts = SecuritySignals.Classify(files);
        var decided = new Decided([], [], []);
        foreach (var run in settings.SecurityLane.Runs.Where(r => r.Serves(stage)))
            AppendPairing(run, facts, files.Count - facts.Count, round, sources, decided);
        return ordinary with
        {
            Reviewers = RosterBuilder.LocalRowsFirst([.. ordinary.Reviewers, .. decided.Work]),
            NotAsked = [.. ordinary.NotAsked, .. decided.Skipped],
            Excluded = [.. ordinary.Excluded, .. decided.Excluded],
            SecurityActive = true,
        };
    }

    /// <summary>What the pairings of one round became: work, not asked, or unable to run.</summary>
    private sealed record Decided(List<ReviewerWork> Work, List<SkippedRole> Skipped, List<ExcludedRole> Excluded);

    /// <summary>One pairing is one unit: a failure while preparing it is recorded on it, and the next one still runs.</summary>
    private void AppendPairing(SecurityRun run, IReadOnlyList<SecurityFile> facts, int omitted, int round,
        IReadOnlyDictionary<string, string>? sources, Decided decided)
    {
        try
        {
            Route(run, facts, omitted, round, sources, decided);
        }
        catch (Exception e)
        {
            log.Warning(e, "Could not prepare security review {Vendor}/{Prompt}", run.Vendor, run.Prompt);
            decided.Excluded.Add(new(run.Vendor, run.Prompt, $"could not prepare security review ({e.GetType().Name})"));
        }
    }

    private void Route(SecurityRun run, IReadOnlyList<SecurityFile> facts, int omitted, int round,
        IReadOnlyDictionary<string, string>? sources, Decided decided)
    {
        if (!WithinBudget(round)) { decided.Skipped.Add(new($"{run.Vendor}/{run.Prompt}", BudgetSpent)); return; }
        if (settings.SecurityLane.ConfigurationRefusal(run) is { Length: > 0 } broken) { decided.Excluded.Add(new(run.Vendor, run.Prompt, broken)); return; }
        var prompt = PromptOf(run);
        if (!SecuritySignals.Triggered(prompt, facts))
        { RecordUnmatched(run, facts, omitted, decided.Skipped, decided.Excluded); return; }
        Prepare(run, prompt, facts, sources, omitted, decided.Work, decided.Excluded);
    }

    private const string BudgetSpent = "security lane round budget spent";

    private bool WithinBudget(int round) => round <= settings.SecurityLane.MaxRounds;

    private SecurityPrompt PromptOf(SecurityRun run) => settings.SecurityLane.Prompts.First(p => p.Id == run.Prompt);

    private void Prepare(SecurityRun run, SecurityPrompt prompt, IReadOnlyList<SecurityFile> facts,
        IReadOnlyDictionary<string, string>? sources, int omitted, List<ReviewerWork> work, List<ExcludedRole> excluded)
    {
        var provider = ProviderOf(run);
        var refusal = Refusal(provider);
        if (refusal.Length > 0) { excluded.Add(new(run.Vendor, run.Prompt, refusal)); return; }
        var body = ReadPrompt(run.Prompt);
        if (string.IsNullOrWhiteSpace(body))
        { excluded.Add(new(run.Vendor, run.Prompt, $"prompt unavailable or over 64 KiB; write text at {prompts.FileToWrite(run.Prompt)}")); return; }
        var budget = new SecurityContextBudget(run.ContextTokens, launchFor(provider, runtimeFor(provider)!).MaxTokens, omitted);
        var pack = SecurityContext.Compose(body, prompt, facts, run.Context, budget, sources);
        if (pack.Refusal.Length > 0) { excluded.Add(new(run.Vendor, run.Prompt, pack.Refusal)); return; }
        work.Add(Build(provider, run, pack.Text));
    }

    private ProviderSettings ProviderOf(SecurityRun run) => settings.Providers.First(p => p.Provider == run.Vendor);

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

    private string Refusal(ProviderSettings provider)
    {
        if (!provider.Enabled) return "reviewer row disabled";
        if (!canRun(provider)) return "reviewer runtime or credentials unavailable";
        return runtimeFor(provider) switch
        {
            RemoteRuntime => "this Team runtime does not advertise the security schema; use a local CLI or API reviewer row",
            ApiRuntime => ApiRowView.Of(provider, settings.ApiOverrides).Refusal,
            _ => string.Empty,
        };
    }

    private string ReadPrompt(string id)
    {
        if (IsOversizedFile(prompts.FileToWrite(id))) return string.Empty;
        // An empty or blank override is no override — the shipped text stands, as it does for every other role.
        var text = prompts.ForOptional(id);
        return IsOversizedText(text) || IsOnlyThePlaceholder(text) ? string.Empty : text;
    }

    private static bool IsOversizedFile(string path) => File.Exists(path) && new FileInfo(path).Length > SecurityContext.MaxPromptBytes;

    private static bool IsOversizedText(string text) => Encoding.UTF8.GetByteCount(text) > SecurityContext.MaxPromptBytes;

    /// <summary>The operator's unfilled template: one <c>&lt;!-- OPERATOR: … --&gt;</c> comment and nothing after it.</summary>
    private static bool IsOnlyThePlaceholder(string text)
    {
        var trimmed = text.Trim();
        return trimmed.StartsWith("<!-- OPERATOR:", StringComparison.Ordinal)
            && trimmed.IndexOf("-->", StringComparison.Ordinal) == trimmed.Length - 3;
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
