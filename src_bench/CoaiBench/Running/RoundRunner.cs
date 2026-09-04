using System.Text.Json.Nodes;
using CoaiBench.Model;

namespace CoaiBench.Running;

/// <summary>
/// One case through one server: open, the plan gate, resolve, the code gate, resolve.
/// </summary>
/// <remarks>
/// <para>The protocol in the order the product refuses to let anybody skip. Every finding is
/// ACCEPTED, because the bench measures what the gate produced and not how a caller argued with it;
/// a rejection would change the next round's arithmetic and make two runs incomparable.</para>
/// <para>Each run gets its own BRANCH — <c>bench/&lt;arm&gt;-&lt;case&gt;-&lt;repeat&gt;</c> — because a
/// session is keyed by repo+branch and its plan stage happens once. Two runs of one case on one
/// branch would be one session, and the second would be refused rather than measured.</para>
/// </remarks>
public sealed class RoundRunner(GateClient client, string repo, TimeSpan timeout)
{
    /// <summary>
    /// How many times the plan stage may be asked before the bench gives up on reaching the code one.
    /// </summary>
    /// <remarks>
    /// The SERVER's own budget is what actually stops it — this is only a ceiling so a bench cannot
    /// loop forever against a gate configured to allow it.
    /// </remarks>
    private const int MaxPlanRounds = 4;

    /// <summary>The verdicts that open the code stage — the same three the server acts on.</summary>
    internal static bool Passed(string verdict) =>
        verdict is "proceed" or "good_enough" or "continue_anyway";

    public async Task<RunRecord> RunAsync(Case work, string arm, int repeat, int lane, Stages stages)
    {
        var record = new RunRecord(work, arm, repeat, lane);
        var stageResults = new List<StageResult>();
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            await client.HandshakeAsync(deadline.Token);
            var branch = Bench.BranchFor(work);
            await client.CallAsync("open", Args(("repoPath", repo), ("branch", branch)), deadline.Token);

            var planText = await File.ReadAllTextAsync(Path.Combine(repo, work.PlanFile), deadline.Token);
            var planPassed = stages == Stages.Diffs;
            if (stages is Stages.Plans or Stages.Both)
            {
                // The plan stage REPEATS until it passes or the rounds run out, which is what a real
                // caller does and what the protocol is built around. The first version ran it once
                // and went straight to the code gate, and three of four code rounds were refused
                // with "no plan round has reached 'proceed'" — the product being right and the bench
                // measuring its own mistake.
                for (var round = 1; round <= MaxPlanRounds && !planPassed; round++)
                {
                    var plan = await StageAsync($"plan-{round}", "review_plan",
                        Args(("repoPath", repo), ("branch", branch), ("planText", planText)), branch, deadline.Token);
                    stageResults.Add(plan);
                    planPassed = Passed(plan.Verdict);
                    if (plan.Verdict.Length == 0)
                    {
                        break; // the round did not run at all; another attempt measures nothing
                    }
                }
            }

            if (stages is Stages.Diffs or Stages.Both && work.Commit.Length > 0 && planPassed)
            {
                stageResults.Add(await StageAsync("code", "review_code",
                    Args(("repoPath", repo), ("branch", branch), ("baseRef", work.BaseRef), ("planText", planText)),
                    branch, deadline.Token));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException)
        {
            record = record with { HarnessError = $"{e.GetType().Name}: {e.Message}" };
        }

        return record with
        {
            Stages = stageResults,
            FinishedUtc = DateTime.UtcNow,
            ServerSaid = client.ServerSaid,
        };
    }

    /// <summary>One gate stage, and the resolve that the next one is refused without.</summary>
    private async Task<StageResult> StageAsync(
        string name, string tool, JsonObject arguments, string branch, CancellationToken ct)
    {
        var (answer, seconds) = await client.CallAsync(tool, arguments, ct);
        var result = Read(name, seconds, answer);
        if (result.Verdict.Length > 0)
        {
            await client.CallAsync(
                "resolve",
                Args(("repoPath", repo), ("branch", branch), ("decisions", AcceptAll(result.Findings.Count))),
                ct);
        }

        return result;
    }

    /// <summary>Accepting everything: the bench measures the gate, not a policy for arguing with it.</summary>
    private static string AcceptAll(int findings) =>
        $"[{string.Join(",", Enumerable.Range(0, findings).Select(i => $$"""{"finding":{{i}},"action":"accept"}"""))}]";

    private static StageResult Read(string name, double seconds, JsonNode? answer)
    {
        if (answer is not JsonObject reply)
        {
            return new StageResult(name, seconds, Error: "the tool answered with nothing");
        }

        return new StageResult(
            name,
            seconds,
            Verdict: Text(reply, "verdict"),
            Error: Text(reply, "error"),
            GatingCount: (int)Number(reply, "gatingCount"),
            Reviewers: Text(reply, "reviewers"),
            TokensIn: Number(reply["cost"] as JsonObject, "tokensIn"),
            TokensOut: Number(reply["cost"] as JsonObject, "tokensOut"),
            CostUsd: (reply["cost"] as JsonObject)?["costUsd"]?.GetValue<double?>())
        {
            Findings = [.. (reply["findings"] as JsonArray ?? []).OfType<JsonObject>().Select(FindingFrom)],
            Commands = [.. (reply["commands"] as JsonArray ?? []).Select(c => c?.GetValue<string>() ?? string.Empty)],
        };
    }

    private static Finding FindingFrom(JsonObject one) => new(
        Severity: Text(one, "severity"),
        Category: Text(one, "category"),
        File: Text(one, "file"),
        Line: (int)Number(one, "line"),
        Title: Text(one, "title"),
        Why: Text(one, "why"),
        Fix: Text(one, "fix"),
        Role: Text(one, "role"),
        IsGating: one["isGating"]?.GetValue<bool>() ?? false,
        Providers: [.. (one["providers"] as JsonArray ?? []).Select(p => p?.GetValue<string>() ?? string.Empty)]);

    private static string Text(JsonObject? from, string name) =>
        from?[name]?.GetValue<string>() ?? string.Empty;

    private static long Number(JsonObject? from, string name) =>
        from?[name]?.GetValue<long>() ?? 0;

    private static JsonObject Args(params (string Name, string Value)[] pairs)
    {
        var arguments = new JsonObject();
        foreach (var (name, value) in pairs)
        {
            arguments[name] = value;
        }

        return arguments;
    }
}
