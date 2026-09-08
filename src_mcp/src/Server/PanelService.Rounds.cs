using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Worktrees;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// The addressable half of the code gate: reserve a round, run exactly that one, read it back.
/// </summary>
/// <remarks>
/// <para><b>Why this exists beside <c>review_code</c> rather than inside it.</b> <c>review_code</c>
/// creates a round and runs it in one call, and every caller in the field depends on exactly that.
/// A caller that must survive its own crash needs the two halves apart: a name for the round it is
/// about to start, written down BEFORE the non-idempotent part, and a way to ask about that one
/// name afterwards. Folding both contracts into one tool would give it two idempotency rules
/// selected by an optional argument — a silent change of meaning for the callers who never send
/// it.</para>
/// <para><b>The database is required here, and only here.</b> Everywhere else it is a projection
/// and a write that fails is a log line. A reservation is the only record that a round was ever
/// named, so a database that will not open means the reservation is REFUSED — handing back a
/// locator this server could not store would be a name nothing answers to.</para>
/// </remarks>
public sealed partial class PanelService
{
    /// <summary>
    /// Fires immediately after an addressable round's answer is durably committed.
    /// </summary>
    /// <remarks>
    /// A seam for one test and nothing else: the crash window this pass closed is between the
    /// durable write and the reply, and the only way to prove it is closed is to die exactly there.
    /// Null in every real run, so production has no branch to get wrong.
    /// </remarks>
    internal Action? AfterDurableCompletion { get; set; }

    /// <summary>This server instance, as a running round records its owner.</summary>
    /// <remarks>
    /// Fresh per <c>PanelService</c>. A process id alone is reused within the hour, so a row
    /// naming only a pid can be read as owned by a stranger's process that happens to hold it
    /// now. This is never compared against a clock — it is an identity, not a lease.
    /// </remarks>
    private readonly string _instance = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Fires after every check the caller made and before the round's SHA is settled.
    /// </summary>
    /// <remarks>
    /// The other seam for one test, and it sits exactly where the window was: past every check
    /// `run_round` makes, and before the stage decides which commit to read. A branch moved here
    /// used to be resolved by the stage itself and reviewed under the previous commit's
    /// attestation. Null in every real run.
    /// </remarks>
    internal Func<Task>? BeforeWorktree { get; set; }

    /// <summary>
    /// Fires once the session claim is held and before the session is read.
    /// </summary>
    /// <remarks>
    /// The third seam, and the only way to observe WHERE the fallback scope is read. A read
    /// taken before the claim and one taken after are identical in every run that is not
    /// racing, so a test that cannot change the session in between cannot tell them apart.
    /// Null in every real run.
    /// </remarks>
    internal Action? AfterClaimTaken { get; set; }

    /// <summary>
    /// Closes rounds left `running` by a process that is gone, and frees the session's slot.
    /// </summary>
    /// <remarks>
    /// <para><b>Holding the session claim IS the proof.</b> Every mutating call on this session
    /// takes that claim and holds it for its whole duration, so a caller that owns it knows no
    /// round of this session is running anywhere on this machine. A row that still says
    /// `running` therefore belongs to a process that died. No timeout is consulted and none
    /// exists: a review that takes an hour is exactly as alive as one that takes a minute, and
    /// that is the whole reason this is not a lease.</para>
    /// <para><b>Only this machine's rounds.</b> The claim is a file handle, and a handle proves
    /// liveness where the filesystem is local. A round owned by another machine — two servers
    /// over a shared data directory — is left exactly as it is and said so in the log, because
    /// nothing here can prove that machine's process is gone.</para>
    /// <para><b>Failed, never not_started and never completed.</b> The round WAS dispatched: a
    /// reviewer may have run, and its cost is spent. `failed` says that honestly. `not_started`
    /// would license a free retry of a call that may already have happened, and nothing here
    /// re-dispatches anything — a new round needs a new token and a deliberate `run_round`.</para>
    /// </remarks>
    private void ReleaseOrphanedRounds(string repoPath, string branch)
    {
        using var db = Store.RoundsDb.Open(_settings.DataDir, _log);
        if (db is null)
        {
            return;
        }

        foreach (var stranded in db.RunningRounds(repoPath, branch))
        {
            if (!string.Equals(stranded.OwnerMachine, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                && stranded.OwnerMachine.Length > 0)
            {
                _log.Warning(
                    "round {Round} is running on {Machine} and was left alone: this server cannot "
                    + "prove another machine's process is gone",
                    stranded.Locator.RoundId, stranded.OwnerMachine);
                continue;
            }

            db.Fail(stranded.Locator);
            _log.Warning(
                "round {Round} was left running by process {Pid} ({Instance}) and is now failed; "
                + "it was dispatched, so it is spent — reserve a new round to try again",
                stranded.Locator.RoundId, stranded.OwnerPid, stranded.OwnerInstance);
        }
    }

    /// <summary>
    /// The session update a finished round owes, as an intention that can be applied later.
    /// </summary>
    /// <remarks>
    /// The exact document, and the hash the file must still carry for that document to be the right
    /// thing to write. Both are decided before the transaction that stores the answer, so the two
    /// halves of a completed round are decided together even though they cannot be written together.
    /// </remarks>
    private static Store.SessionCommit OwedSessionUpdate(
        RoundLocator locator, string repoPath, string branch, string before, PersistedSession next)
    {
        var json = SessionStore.Serialise(next);

        return new Store.SessionCommit(locator, repoPath, branch, Digest(before), json, Digest(json));
    }

    private static string Digest(string text) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text)));

    /// <summary>
    /// Pays whatever the outbox still owes this session, and says how many it paid.
    /// </summary>
    /// <remarks>
    /// <para>Idempotent by construction, which is the whole point: an update is written only over
    /// the state it was computed from, and a file that already carries the result is recognised as
    /// done rather than written again. So running this twice cannot add a round to the trail, repeat
    /// a finding, or move the stage a second time — and it starts no reviewer, so it spends no
    /// budget.</para>
    /// <para>A session that is neither the expected state nor the result is a CONFLICT: somebody
    /// advanced it while a finished round still owed it an update. That is recorded and left
    /// unapplied. Overwriting would destroy whatever they did, and the round's answer is still
    /// readable by its locator either way.</para>
    /// <para>The caller must hold the session claim. This mutates the session file, so it is a
    /// mutating path — never reachable from <c>round_status</c>.</para>
    /// </remarks>
    private int ApplyOwedUpdates(string repoPath, string branch)
    {
        using var db = Store.RoundsDb.Open(_settings.DataDir, _log);
        if (db is null)
        {
            return 0;
        }

        var paid = 0;
        foreach (var owed in db.UnappliedCommits(repoPath, branch))
        {
            var current = Digest(_store.RawText(repoPath, branch));
            if (current == owed.ResultSha256)
            {
                // Already written — by this process before it died, or by an earlier catch-up.
                db.MarkApplied(owed.Locator);
                continue;
            }

            if (current != owed.ExpectedSha256)
            {
                var reason =
                    $"the session moved after round {owed.Locator.RoundId} finished: it expected "
                    + $"{Short(owed.ExpectedSha256)} and found {Short(current)}. The round's answer is "
                    + "readable by its locator; the session was NOT overwritten.";
                _log.Warning("{Reason}", reason);
                db.MarkConflict(owed.Locator, reason);
                continue;
            }

            _store.WriteExact(repoPath, branch, owed.SessionJson);
            db.MarkApplied(owed.Locator);
            paid++;
            _log.Information(
                "applied the session update owed by round {Round}", owed.Locator.RoundId);
        }

        return paid;
    }

    private static string Short(string sha) => sha.Length >= 12 ? sha[..12] : sha;

    /// <summary>
    /// The attestation an addressable round publishes: its reservation's, over the SHA that ran.
    /// </summary>
    /// <remarks>
    /// The equality is asserted rather than assumed. <c>PinnedSha</c> IS this attestation's head,
    /// so a difference here is a wiring bug rather than a moved branch — and publishing a verdict
    /// under an attestation that does not describe the commit reviewed is the one outcome this
    /// whole mechanism exists to make impossible.
    /// </remarks>
    private static SubjectAttestation Attested(StageRun stage, string sha)
    {
        var attestation = stage.Attestation
            ?? throw new InvalidOperationException("an addressable round runs with the attestation it reserved");

        return attestation.HeadSha == sha
            ? attestation
            : throw new InvalidOperationException(
                $"the round ran at {sha} but attested {attestation.HeadSha}; the pin did not reach the worktree");
    }

    /// <summary>
    /// Reserves a code round and pins what it will read. No reviewer runs and no budget moves.
    /// </summary>
    /// <param name="clientToken">
    /// The caller's own idempotency key — its durable id for this round. Reserving twice with the
    /// same token returns the same locator instead of a second round, which is what makes a retry
    /// after a lost answer safe rather than a way to spend two rounds.
    /// </param>
    public async Task<string> ReserveRoundAsync(
        string repoPath,
        string branch,
        string baseRef,
        string clientToken)
    {
        if (string.IsNullOrWhiteSpace(clientToken) || clientToken.Length > RoundLocator.MaxPart)
        {
            return Error(
                "clientToken is required and must be at most 128 characters — it is how a retry "
                + "after a lost answer finds the round it already reserved instead of starting a second.");
        }

        var session = _store.Load(repoPath, branch);
        if (session is null)
        {
            return Error("no session for this repo+branch — call open first");
        }

        SubjectAttestation subject;
        try
        {
            subject = await new SubjectResolver(_launcher).ResolveAsync(repoPath, branch, baseRef);
        }
        catch (WorktreeException e)
        {
            // Nothing is reserved against a subject that will not resolve: a locator pinned to a
            // blank is a locator whose attestation can never be checked.
            return Error(e.Message);
        }

        using var db = Store.RoundsDb.Open(_settings.DataDir, _log);
        if (db is null)
        {
            return Error(
                "the rounds database could not be opened, so no round can be reserved — a locator "
                + "this server cannot store is a name nothing would answer to. See the coai-mcp log.");
        }

        try
        {
            var (reserved, created) = db.Reserve(
                session.State.SessionId,
                clientToken,
                Stage.CodeReview.ToString(),
                repoPath,
                branch,
                subject,
                () => Guid.NewGuid().ToString("N"),
                out var mismatch);
            if (mismatch is { } disagreement)
            {
                // The token already names a round, and it is not the one being asked for. Returning
                // the old reservation would present a pin on other code as if it matched these
                // arguments — the caller would then run a review it believes is about today's work.
                return Error(
                    $"clientToken '{clientToken}' already reserved round {reserved.Locator.RoundId} with a "
                    + $"different {disagreement.Field}: reserved '{disagreement.Reserved}', requested "
                    + $"'{disagreement.Requested}'. A token names ONE round; use a new token for a new "
                    + "subject, or read the existing one back with round_status.");
            }
            _log.Information(
                "round {Round} {What} for session {Session} at {Head} (tree {Tree})",
                reserved.Locator.RoundId, created ? "reserved" : "resumed",
                reserved.Locator.SessionId, reserved.Subject.HeadSha, reserved.Subject.TreeSha);

            // The stored attestation is returned, never the freshly resolved one: a resumed
            // reservation is pinned to what it was pinned to, and showing today's ids would hide
            // exactly the movement `run_round` is about to refuse.
            return Json(
                new ReservationAnswer(
                    Wire(reserved.Locator),
                    Wire(reserved.Subject),
                    reserved.ReadBackState,
                    !created,
                    ReservationInstruction(reserved)),
                ServerJsonContext.Default.ReservationAnswer);
        }
        catch (Exception e)
        {
            _log.Error(e, "the round could not be reserved");

            return Error($"the round could not be reserved: {e.Message}");
        }
    }

    private static string ReservationInstruction(ReservedRound reserved) => reserved.State switch
    {
        RoundLifecycle.Reserved =>
            "Store this locator before calling run_round. Its attestation is what the round will "
            + "read; run_round refuses if the checkout has moved since.",
        RoundLifecycle.Running =>
            "This locator is already dispatched. Do not call run_round again — read it back with "
            + "round_status.",
        RoundLifecycle.Completed =>
            "This locator already completed. round_status returns its whole answer; run_round "
            + "returns the same answer and starts nothing.",
        _ =>
            "This locator was dispatched and ended without an answer. Reserve a new round with a "
            + "new token; nothing here may be re-run under this one.",
    };

    /// <summary>
    /// Runs exactly the reserved round named by the locator, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>Repeating the call cannot produce a second round: a completed locator replays its
    /// stored answer, a running one is refused, and a failed one is spent. Only a locator sitting
    /// at <c>reserved</c> dispatches anything, and it is moved off that state inside the same
    /// transaction that the guard reads.</para>
    /// <para>The subject is re-resolved and compared before dispatch. A branch that moved between
    /// the reservation and the run is a different subject, and reviewing it under a locator that
    /// attested the old one would make the attestation a label rather than evidence.</para>
    /// </remarks>
    public async Task<string> RunRoundAsync(
        string providerId,
        string sessionId,
        string roundId,
        string planText)
    {
        var locator = new RoundLocator(providerId, sessionId, roundId);
        if (!locator.IsComplete)
        {
            return Error(
                "providerId, sessionId and roundId are all required — a locator missing a part "
                + "names no round.");
        }

        using var db = Store.RoundsDb.Open(_settings.DataDir, _log);
        if (db is null)
        {
            return Error("the rounds database could not be opened, so no reserved round can be run.");
        }

        var known = db.Read(locator);
        if (known is null)
        {
            return Error($"no such round at this server: {locator}. Reserve one with reserve_round.");
        }

        // Taken BEFORE the round is claimed, so a call that finds the session busy never leaves a
        // locator marked running with nothing behind it. It covers the legacy paths too, which take
        // no locator at all and would otherwise run beside this one over the same session file.
        using var claim = SessionClaim.TryTake(_settings.DataDir, known.RepoPath, known.Branch);
        if (claim is null)
        {
            return Error(SessionClaim.Busy(known.Branch));
        }

        var reserved = db.Read(locator);
        if (reserved is null)
        {
            // Never "not started": this server does not know the locator, which is a different
            // fact and licenses nothing.
            return Error($"no such round at this server: {locator}. Reserve one with reserve_round.");
        }

        if (reserved.State == RoundLifecycle.Completed)
        {
            // The whole point of the token: a caller that lost the answer gets it back rather than
            // spending a second round to hear the same thing.
            return reserved.ResultJson.Length > 0
                ? reserved.ResultJson
                : Error($"round {locator} completed but its answer was not stored; read it with round_status.");
        }

        if (reserved.State == RoundLifecycle.Running)
        {
            return Error(
                $"round {locator} is already running. Read it back with round_status; a second "
                + "dispatch would double a call that has not finished.");
        }

        if (reserved.State == RoundLifecycle.Failed)
        {
            return Error(
                $"round {locator} was dispatched and ended without an answer, so it is spent. "
                + "Reserve a new round with a new token.");
        }

        SubjectAttestation now;
        try
        {
            now = await new SubjectResolver(_launcher).ResolveAsync(
                reserved.RepoPath, reserved.Branch, reserved.Subject.BaseRef);
        }
        catch (WorktreeException e)
        {
            return Error(e.Message);
        }

        if (!now.Matches(reserved.Subject))
        {
            return Error(
                $"the checkout has moved since this round was reserved: it attested {reserved.Subject.HeadSha} "
                + $"(tree {reserved.Subject.TreeSha}) and now resolves to {now.HeadSha} (tree {now.TreeSha}). "
                + "Reserve a new round for the new subject; this one stays reserved and unspent.");
        }

        // Past every refusal that runs nothing. The CLAIM decides who dispatches — not the state
        // read back afterwards, which says `running` to the winner and the loser alike.
        var (dispatch, _) = db.Begin(locator, Store.RoundOwner.Here(_instance));
        switch (dispatch)
        {
            case RoundClaim.Claimed:
                break;

            case RoundClaim.AlreadyCompleted:
                return db.Read(locator)?.ResultJson is { Length: > 0 } stored
                    ? stored
                    : Error($"round {locator} completed but its answer was not stored; read it with round_status.");

            case RoundClaim.AlreadyRunning:
                return Error(
                    $"round {locator} is already running — another caller claimed it. Read it back "
                    + "with round_status; a second dispatch would double a call that has not finished.");

            case RoundClaim.SessionBusy:
                return Error(
                    $"another round of session {locator.SessionId} is running. One round at a time per "
                    + "session: the round trail and the stage state are one document, and two rounds "
                    + "in flight over it corrupt both. Read the running round back with round_status.");

            default:
                return Error($"round {locator} could not be started; it is {reserved.ReadBackState}.");
        }

        // The database released this call to dispatch, so the round is now spent whatever happens.
        // The answer is committed inside RunStageAsync, in one transaction with the round it
        // belongs to — there is no second write here to crash between.
        var answer = await RunPinnedCodeRoundAsync(reserved, planText, claim);
        if (IsRefusal(answer))
        {
            // Dispatched and no answer: honest, and deliberately not re-runnable. Whether a reviewer
            // ran before it failed is not knowable from here, so the round is spent rather than
            // quietly offered again.
            db.Fail(locator);
            _log.Warning("round {Round} was dispatched and did not answer", locator.RoundId);

            return answer;
        }

        return answer;
    }

    /// <summary>
    /// The code stage, pinned to what the reservation attested and bound to its locator.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="ReviewCodeAsync"/>: that call resolves the branch tip for itself,
    /// which is a second resolution after the one this round was checked against. A commit landing
    /// in between would be reviewed and then published under the previous commit's attestation.
    /// </remarks>
    private Task<string> RunPinnedCodeRoundAsync(ReservedRound reserved, string planText, SessionClaim claim)
    {
        // The scope is not resolved here either: the run itself falls back to the session's own
        // under the claim this call already holds, so a concurrent `resolve` cannot slip a
        // different one in between. The refusal for a scope with no substance moves with it.
        return RunStageAsync(
            reserved.RepoPath,
            reserved.Branch,
            planText,
            CodeStage(reserved.RepoPath, reserved.Branch, reserved.Subject.BaseRef, default) with
            {
                PinnedSha = reserved.Subject.HeadSha,
                Locator = reserved.Locator,
                Attestation = reserved.Subject,
            },
            default,
            claim);
    }

    /// <summary>
    /// Reads back exactly one locator. Strictly read-only: it starts nothing and spends nothing.
    /// </summary>
    /// <remarks>
    /// A locator this server has never issued reports <c>unknown</c>, never <c>not_started</c>.
    /// "I have no record" and "nothing ran" look identical from here and are not the same fact, and
    /// only the second would license dispatching again.
    /// </remarks>
    public Task<string> RoundStatusAsync(string providerId, string sessionId, string roundId)
    {
        var locator = new RoundLocator(providerId, sessionId, roundId);
        if (!locator.IsComplete)
        {
            return Task.FromResult(Error(
                "providerId, sessionId and roundId are all required — a locator missing a part names no round."));
        }

        using var db = Store.RoundsDb.Open(_settings.DataDir, _log);
        var reserved = db?.Read(locator);
        if (reserved is null)
        {
            return Task.FromResult(Json(
                new RoundStatusAnswer(
                    Wire(locator),
                    RoundLifecycle.Unknown,
                    db is null
                        ? "the rounds database could not be opened, so nothing can be read back. This is "
                          + "not evidence that the round did not run."
                        : "this server has no such round. That is not evidence that no review ran — only "
                          + "that this server cannot say."),
                ServerJsonContext.Default.RoundStatusAnswer));
        }

        var status = Json(
            new RoundStatusAnswer(
                Wire(reserved.Locator),
                reserved.ReadBackState,
                StatusInstruction(reserved.ReadBackState),
                Wire(reserved.Subject)),
            ServerJsonContext.Default.RoundStatusAnswer);

        // The stored answer is injected as the DOCUMENT it is, not deserialised into
        // `ReviewAnswer` and written out again. That round trip silently dropped the locator and
        // the attestation the stored reply carries — they are not fields of `ReviewAnswer` — so a
        // read-back and the original reply disagreed about exactly the two things this whole
        // mechanism exists to carry.
        return Task.FromResult(
            reserved.State == RoundLifecycle.Completed && reserved.ResultJson.Length > 0
                ? WithRawProperty(status, "review", reserved.ResultJson)
                : status);
    }

    /// <summary>Adds one property whose value is an already-serialised JSON document.</summary>
    private static string WithRawProperty(string json, string name, string rawValue)
    {
        using var outer = JsonDocument.Parse(json);
        using var inner = JsonDocument.Parse(rawValue);
        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in outer.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WritePropertyName(name);
            inner.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string StatusInstruction(string state) => state switch
    {
        RoundLifecycle.NotStarted =>
            "Reserved and never dispatched: nothing external has run under this locator.",
        RoundLifecycle.Running =>
            "Dispatched and not finished. Do not dispatch again.",
        RoundLifecycle.Completed =>
            "Finished. The review below is the whole answer, and resolve still owes it decisions.",
        RoundLifecycle.Failed =>
            "Dispatched and ended without an answer. It is spent; reserve a new round.",
        _ => "This server cannot say what became of this round.",
    };

    /// <summary>Is this reply a refusal rather than a review?</summary>
    /// <remarks>
    /// Parsed, not pattern-matched on text: <see cref="ErrorAnswer"/> is the one shape with an
    /// <c>error</c> property, and a <see cref="ReviewAnswer"/> deserialised through it leaves that
    /// property null. A prefix check would call a finding titled "error" a refusal.
    /// </remarks>
    private static bool IsRefusal(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, ServerJsonContext.Default.ErrorAnswer)?.Error is { Length: > 0 };
        }
        catch (JsonException)
        {
            return true;
        }
    }

    /// <summary>
    /// The review answer with the locator and attestation folded in.
    /// </summary>
    /// <remarks>
    /// Written into the serialised document rather than added to <see cref="ReviewAnswer"/>, so the
    /// shape <c>review_code</c> has always returned is untouched for every caller still using it.
    /// </remarks>
    private static string WithAttestation(string reviewJson, RoundLocator locator, SubjectAttestation subject)
    {
        using var document = JsonDocument.Parse(reviewJson);
        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            writer.WritePropertyName("locator");
            JsonSerializer.Serialize(writer, Wire(locator), ServerJsonContext.Default.LocatorDto);
            writer.WritePropertyName("attestation");
            JsonSerializer.Serialize(writer, Wire(subject), ServerJsonContext.Default.AttestationDto);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static LocatorDto Wire(RoundLocator locator) =>
        new(locator.ProviderId, locator.SessionId, locator.RoundId);

    private static AttestationDto Wire(SubjectAttestation subject) =>
        new(
            subject.RepoIdentity,
            subject.BaseRef,
            subject.BaseSha,
            subject.HeadSha,
            subject.TreeSha,
            subject.SubjectHash);
}
