using System.Runtime.CompilerServices;
using CoaiMcp.Core.Feature;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Feature;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>What <c>review_feature</c> was called with, before any of it is believed.</summary>
/// <param name="Epics">The <c>epics</c> argument, JSON as the caller wrote it.</param>
/// <param name="Lessons">The <c>lessons</c> argument, JSON as the caller wrote it.</param>
/// <param name="Caller">Who is asking — the MCP handshake plus the model the caller declared.</param>
internal sealed record FeatureRequest(
    string RepoPath, string PlanPath, string BaseRef, string Epics, string Lessons, bool Again, CallerDeclaration Caller);

/// <summary>The two JSON arguments the caller wrote by hand, parsed and accepted — no I/O yet.</summary>
internal sealed record FeatureWritten(FeatureEpics Epics, FeatureLessons Lessons);

/// <summary>Everything the caller sent, accepted — the plan read, nothing asked of git's history or the database yet.</summary>
internal sealed record FeatureCall(FeaturePlanText Plan, FeatureEpics Epics, FeatureLessons Lessons);

/// <summary>
/// The fourth gate's entry point (S2.2b of the feature-review plan): the inputs refused before any I/O,
/// the git refs refused before anything is built, and then ONE round through the engine every stage
/// runs through — the feature's pack as its context, the feature role and the feature schema.
/// </summary>
/// <remarks>
/// <para><b>The order is the plan's</b> (§4.4, §4.5): <c>lessons</c> and <c>epics</c> (pure), the
/// repository's top level (every path below is read from it), the plan (a file inside it), then the D17
/// threshold — a plan of fewer epics than
/// <c>COAI_FEATURE_MIN_EPICS</c> is a recorded <c>skipped</c> round, not a refusal, and needs no base —
/// then the refs, then the engine. The engine owns everything after: the claim, the session made under
/// it (no <c>open</c> needed), <c>stage.Begin</c> BEFORE any skip so a standing call_human stands, the
/// D1 skip when nobody can review — decided from the settings BEFORE the pack is built — the base held
/// to and saved with the round that runs, <c>call_human</c> when every reviewer failed.</para>
/// <para><b>What is built, and in what order</b> (§4.6): the outline with the changed hunks
/// (<see cref="FeatureOutlineBuilder"/>, credential files withheld and content redacted there), the
/// gate's history of this work (<see cref="GateHistoryQuery"/>, one sentence when it cannot be read),
/// the rules at the full budget (<see cref="StageRules.Feature"/>, D18), all rendered by
/// <see cref="FeatureContext"/>.</para>
/// <para>Source requests are recorded, not served: the loop that answers them is S3.2, so this round is
/// "follow-ups = 0" by construction, and the reviewer is told so (<see cref="ReviewerPrompt"/>).</para>
/// </remarks>
internal sealed class FeatureStage(
    PanelSettings settings,
    IProcessLauncher launcher,
    RoundEngine engine,
    RosterBuilder roster,
    ISourceOutliner outliner,
    Serilog.ILogger log,
    Noticing noticing)
{
    public async Task<string> ReviewAsync(FeatureRequest request, CancellationToken ct)
    {
        try
        {
            return await ReadAsync(request, ct) switch
            {
                FeatureInput<FeatureCall>.Accepted accepted => await RunAsync(request, accepted.Value, ct),
                FeatureInput<FeatureCall>.Refused refused => Refused(refused.Sentence),
                _ => throw new InvalidOperationException("the union is closed"),
            };
        }
        catch (ContextException e)
        {
            // The one git failure the checks before the engine can meet that is not a refusal of their
            // own — the reviewable-range numstat — said as a sentence rather than an SDK-level error. The
            // engine catches its own the same way.
            return Refused(e.Message);
        }
    }

    /// <summary>
    /// What the caller sent, accepted or refused: the two JSON arguments first (pure), then the
    /// repository's top level (every path is read from it), then the plan (a file inside it).
    /// </summary>
    private async Task<FeatureInput<FeatureCall>> ReadAsync(FeatureRequest request, CancellationToken ct) =>
        Written(request) switch
        {
            FeatureInput<FeatureWritten>.Accepted written =>
                await new FeatureRefs(launcher).TopLevelProblemAsync(request.RepoPath, ct) is { Length: > 0 } notTheTop
                    ? new FeatureInput<FeatureCall>.Refused(notTheTop)
                    : WithPlan(request, written.Value),
            FeatureInput<FeatureWritten>.Refused no => new FeatureInput<FeatureCall>.Refused(no.Sentence),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    /// <summary>The D17 threshold, the refs, then the engine — the inputs already accepted.</summary>
    private async Task<string> RunAsync(FeatureRequest request, FeatureCall call, CancellationToken ct)
    {
        var skip = call.Epics.Count < settings.FeatureMinEpics
            ? SkipReasons.TooFewEpics(call.Epics.Count, settings.FeatureMinEpics)
            : string.Empty;

        return await new FeatureRefs(launcher).ResolveAsync(request.RepoPath, request.BaseRef, skip.Length > 0, ct) switch
        {
            FeatureInput<FeatureRange>.Accepted { Value: var range } =>
                await engine.RunStageAsync(request.RepoPath, SessionKey.FeatureBranch, call.Plan.Text, Run(request, call, range, skip, ct), ct),
            FeatureInput<FeatureRange>.Refused refused => Refused(refused.Sentence),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    /// <summary>The caller's two JSON inputs, in the plan's order — pure; the first refusal is the answer.</summary>
    internal static FeatureInput<FeatureWritten> Written(FeatureRequest request) =>
        (FeatureInputs.ParseLessons(request.Lessons), FeatureInputs.ParseEpics(request.Epics)) switch
        {
            (FeatureInput<FeatureLessons>.Refused no, _) => new FeatureInput<FeatureWritten>.Refused(no.Sentence),
            (_, FeatureInput<FeatureEpics>.Refused no) => new FeatureInput<FeatureWritten>.Refused(no.Sentence),
            (FeatureInput<FeatureLessons>.Accepted lessons, FeatureInput<FeatureEpics>.Accepted epics) =>
                new FeatureInput<FeatureWritten>.Accepted(new FeatureWritten(epics.Value, lessons.Value)),
            _ => throw new InvalidOperationException("the unions are closed"),
        };

    private static FeatureInput<FeatureCall> WithPlan(FeatureRequest request, FeatureWritten written) =>
        FeaturePlan.Read(request.RepoPath, request.PlanPath) switch
        {
            FeatureInput<FeaturePlanText>.Accepted plan =>
                new FeatureInput<FeatureCall>.Accepted(new FeatureCall(plan.Value, written.Epics, written.Lessons)),
            FeatureInput<FeaturePlanText>.Refused no => new FeatureInput<FeatureCall>.Refused(no.Sentence),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    /// <summary>The run the engine is handed: this stage's begin, session, head, skip policy and work.</summary>
    private StageRun Run(FeatureRequest request, FeatureCall call, FeatureRange range, string skip, CancellationToken ct) =>
        new(request.Again ? RoundMachine.BeginFeatureRoundAgain : RoundMachine.BeginFeatureRound,
            NeedsWorktree: false, Stage: Stage.FeatureReview, ReadsCheckout: false,
            (session, workingDir, sha) => WorkAsync(request.RepoPath, call, range.Base, session, workingDir, sha, ct))
        {
            Feature = call.Plan.Identity,
            // A full id, resolved once by the ref checks: the commit they passed is the commit read.
            Head = range.Head,
            WhenNobody = NobodyPolicy.RecordSkip,
            Session = new SessionRule.CreateIfAbsent(() => NewSession(request, call)),
            RolesPerVendor = Math.Max(1, settings.Rounds.EnabledRolesOf(Stage.FeatureReview).Count),
            SkipBecause = skip,
            FeatureBase = range.Base,
            RefuseBeforeBuilding = (loaded, sha) => Task.FromResult(
                skip.Length > 0 ? string.Empty : WhyNotThisRound(loaded, sha, range.Base, request.Again)),
        };

    /// <summary>
    /// Why this call may not run a round against the session as read under the claim — a different base
    /// without <c>again</c>, or <c>again</c> with the SAME base over the head the last review already read
    /// (D14) — or empty.
    /// </summary>
    /// <remarks>
    /// D14 holds in EVERY state, not only after <c>Done</c>: an open review whose last round read this head
    /// has nothing new to read either, and reopening it would run the same round again. <c>again</c> with
    /// a DIFFERENT base is a fresh review (§4.3), so the head it last read does not bind it; what that
    /// review starts over is the engine's to apply with the round that runs.
    /// </remarks>
    internal static string WhyNotThisRound(PersistedSession loaded, string head, string baseSha, bool again) =>
        FeatureBases.WhyNot(loaded.FeatureBase, baseSha, again) is { Length: > 0 } otherBase ? otherBase
        : again && !FeatureBases.IsAnother(loaded.FeatureBase, baseSha) ? Unmoved(LastReviewed(loaded), head)
        : string.Empty;

    /// <summary>The last feature round that actually ran — a skip read no head worth comparing with.</summary>
    private static RoundRecord? LastReviewed(PersistedSession session) =>
        session.Rounds.LastOrDefault(r => r.Stage == nameof(Stage.FeatureReview) && r.Verdict != RoundRecord.Skipped);

    private static string Unmoved(RoundRecord? last, string head) =>
        last is { Sha.Length: > 0 } reviewed && string.Equals(reviewed.Sha, head, StringComparison.OrdinalIgnoreCase)
            ? $"the head has not moved since feature round {reviewed.Number} reviewed {head} — land the fix pull requests, then call review_feature with again: true over the new head"
            : string.Empty;

    /// <summary>
    /// The session a first call makes for itself — with NO base yet. The base is saved with the round that
    /// runs (§4.3, <c>StageRun.FeatureBase</c>): a first call that was skipped, or whose pack could not be
    /// built, has reviewed nothing, and a later call naming another base must not be refused against a
    /// review that never happened.
    /// </summary>
    private PersistedSession NewSession(FeatureRequest request, FeatureCall call) =>
        new(new SessionState(Guid.NewGuid().ToString("N")[..8], request.RepoPath, SessionKey.FeatureBranch, settings.Rounds)
        {
            Feature = call.Plan.Identity,
            // Its own stage from the first moment: a feature session never passes through a plan stage.
            Stage = Stage.FeatureReview,
        },
        [])
        {
            OpenedUtc = DateTime.UtcNow,
            PlanText = call.Plan.Text,
            Caller = request.Caller,
        };

    /// <summary>The round's work: the pack as context, the feature roles, no checkout.</summary>
    private async Task<RoundWork> WorkAsync(
        string repoPath, FeatureCall call, string baseSha, PersistedSession session, string workingDir, string sha, CancellationToken ct)
    {
        var outline = await new FeatureOutlineBuilder(launcher, outliner).BuildAsync(repoPath, baseSha, sha, ct: ct);
        var history = await GateHistoryQuery.RenderAsync(
            new GateHistoryAsk(settings.DataDir, repoPath, baseSha, sha, [.. call.Epics.Items.Select(e => e.Branch).Where(b => b.Length > 0)],
                call.Plan.Text, session.State.SessionId),
            launcher, ct);
        var rules = RuleFiles.Collect(repoPath, RuleOrder.Staged(StageRules.Feature));
        var context = FeatureContext.Render(new FeatureContextInput(
            call.Plan.Identity, call.Plan.Text, call.Epics, call.Lessons, history,
            RulesText.Section(rules, rules.TierCoverage(StageRules.Feature)), outline, Guid.NewGuid().ToString("N")[..8]));
        LogPack(outline, rules, context);

        var round = session.State.RoundsRunThisStage + 1;

        var work = roster.BuildWork(
            settings.Rounds.RolesForRound(Stage.FeatureReview, round), workingDir, context, round,
            stage: Stage.FeatureReview, readsCheckout: false, seed: PanelService.StableSeed(session.State.SessionId, round));

        // The RESOLVED base, as the code stage records it: what the outline was actually compared against.
        return work with { BaseRef = outline.BaseSha };
    }

    /// <summary>What the reviewers were sent, in the same sentence shape the other stages log.</summary>
    private void LogPack(FeatureOutline outline, RuleBundle rules, string context) =>
        log.Information(
            "context for review: feature pack {PackBytes} bytes; {Files} file(s) changed, {Outlined} outlined, "
            + "{NotOutlined} not outlined, {Dropped} elided, {Cut} member hunk(s) cut; rules {RulesBytes} bytes, "
            + "{Matched} of {Tier} tier rule(s), {Omitted} omitted",
            System.Text.Encoding.UTF8.GetByteCount(context),
            outline.Files.Count,
            outline.Outlined,
            outline.Omissions.NotOutlined.Count,
            outline.Omissions.Dropped.Count,
            outline.Omissions.CutHunks.Count,
            rules.Bytes,
            rules.MatchedCount(StageRules.Feature),
            StageRules.Feature.Length,
            rules.Omitted.Count);

    /// <summary>A refusal, through the one boundary every refusal leaves by, and a line in the log.</summary>
    private string Refused(string sentence, [CallerMemberName] string from = "")
    {
        log.Warning("review_feature refused: {Why}", sentence);

        return Refusal.Answer(sentence, noticing, from);
    }
}
