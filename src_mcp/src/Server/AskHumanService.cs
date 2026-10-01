using System.Runtime.CompilerServices;
using System.Text.Json;
using CoaiMcp.Core.Rounds;

namespace CoaiMcp.Server;

/// <summary>
/// The <c>ask_human</c> block, in a file of its own: the question written into the data directory the
/// extension watches, the wait for the person, and the answer as they gave it.
/// </summary>
/// <remarks>
/// <para>MOVED out of <see cref="PanelService"/> as a proved move (<c>todo/PLAN_question_consultant.md</c>, S2,
/// D12 — <c>prove-move.mjs</c> against the commit the block was cut from), with no behaviour change: the
/// fields keep the names the block reads them by, and the two helpers it called on the service
/// (<c>AddressOf</c>, <c>Both</c>) are forwarded under the same names. The service keeps a one-line
/// delegation so every caller is unchanged.</para>
/// <para>Its own file because the question consultant's gate (S3) lands HERE — the phase rule, the
/// consultant-first refusal, the fifteen-minute wait — and <c>PanelService.cs</c> was 1884 lines before this
/// cut, with a plan of its own to shrink.</para>
/// </remarks>
public sealed class AskHumanService
{
    private readonly PanelSettings _settings;
    private readonly SessionStore _store;
    private readonly Escalations _escalations;
    private readonly Serilog.ILogger _log;
    private readonly Noticing _noticing;
    private readonly Func<string, string, string, string, SessionAddress> _addressOf;

    /// <param name="addressOf">
    /// Which session a caller's arguments name — the service's own resolution, handed in rather than
    /// re-derived, because it reads the document store the service owns.
    /// </param>
    internal AskHumanService(
        PanelSettings settings,
        SessionStore store,
        Escalations escalations,
        Serilog.ILogger log,
        Noticing noticing,
        Func<string, string, string, string, SessionAddress> addressOf)
    {
        _settings = settings;
        _store = store;
        _escalations = escalations;
        _log = log;
        _noticing = noticing;
        _addressOf = addressOf;
    }

    /// <summary>
    /// Puts the question in front of a person and waits — through the data directory the extension
    /// watches, so no port is opened by either half.
    /// </summary>
    /// <remarks>
    /// The open findings ride with it: a person deciding "ship anyway?" needs to see what is still
    /// gating, and going to look for it elsewhere is how a decision gets made on a summary.
    /// </remarks>
    /// <param name="document">
    /// Which document's review is asking, when it is a document round: the same <c>documentPath</c>
    /// or <c>documentName</c> that was passed to <c>review_document</c>, as <c>resolve</c> and
    /// <c>status</c> take it. Empty asks on behalf of the BRANCH's session, which is what plan and
    /// code rounds have always meant. It always loaded the branch session, so a document review's
    /// question carried the branch's pending findings and was filed under the branch session's id —
    /// and the person's answer, looked up by that id, never reached the document session (§9.3 of
    /// the feature-review plan).
    /// </param>
    /// <param name="feature">
    /// Which plan's feature review is asking: the same <c>planPath</c> passed to <c>review_feature</c>, so
    /// the question is filed under that session and the person's answer reaches it.
    /// </param>
    public async Task<string> AskHumanAsync(
        string repoPath, string branch, string question, string document = "", string feature = "", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Error("a question is required — an empty escalation tells a person nothing");
        }

        if (Both(document, feature) is { Length: > 0 } both)
        {
            return Error(both);
        }

        var at = AddressOf(repoPath, branch, document, feature);
        var session = _store.Load(repoPath, at.Branch, at.Document, at.Feature);
        var id = Guid.NewGuid().ToString("N")[..12];
        // A question asked while the gate is HELD is asked for that hold, and one asked on a feature session
        // after its first round may be the person's request for the second: recorded on the session, so the
        // person's answer counts by identity — as the hold's own notice does.
        await RecordOnTheSessionAsync(session, at, repoPath, id, ct);

        // English, as the caller wrote it. There used to be a translator here, and a set of
        // buttons replaced the prose it existed for: the question is one fixed sentence and the
        // answer is a choice, so a subprocess per escalation that can time out or answer in the
        // wrong language was a moving part earning nothing.
        var text = question.Trim();
        var asked = new EscalationQuestion(
            id,
            session?.State.SessionId ?? "no-session",
            repoPath,
            branch,
            text,
            text,
            "en",
            string.Empty,
            session?.Pending.Where(f => f.IsGating).ToList() ?? [],
            DateTime.UtcNow.ToString("O"));

        _log.Information("escalating {Id} to a person: {Question}", id, asked.Question);
        var outcome = await _escalations.AskAsync(asked, _settings.EscalationBudget, ct);

        return outcome switch
        {
            // Their own words, unchanged. Nothing stands between the person and the caller now.
            EscalationOutcome.Answered answered => Json(
                AnswerFor(answered.Text), ServerJsonContext.Default.HumanAnswer),

            // The family's `remote-ask` fallback, verbatim in shape: nobody answered in the budget,
            // so ASK IN THE CHAT rather than stalling or deciding alone. The question file stays.
            _ => Json(
                new HumanAnswer(
                    "no_answer_yet",
                    string.Empty,
                    string.Empty,
                    $"nobody answered in {_settings.EscalationBudget.TotalMinutes:0} minutes — ask the person " +
                    $"directly in this conversation and wait for their reply. The question is still open in " +
                    $"VS Code as escalation {id}."),
                ServerJsonContext.Default.HumanAnswer),
        };
    }

    /// <summary>
    /// A question asked while the gate is held is one of the hold's, and one asked on a feature session
    /// after its first round may be the person's request for the second: the person's answer to it counts
    /// by id (<see cref="CurrentAnswer"/>), so it is recorded where <see cref="RoundMachine.RecordQuestion"/>
    /// says it belongs. The session is re-read and saved under its claim — a <c>resolve</c> may be writing
    /// the same file — and a claim that stays busy is logged and the question asked anyway: the hold's own
    /// notice still answers a hold.
    /// </summary>
    private async Task RecordOnTheSessionAsync(PersistedSession? session, SessionAddress at, string repoPath, string id, CancellationToken ct)
    {
        if (session is null || ReferenceEquals(RoundMachine.RecordQuestion(session.State, id), session.State))
        {
            return;
        }

        for (var attempt = 0; attempt < HoldClaimAttempts; attempt++)
        {
            if (TryRecordOnTheSession(at, repoPath, id))
            {
                return;
            }

            await Task.Delay(HoldClaimWait, ct);
        }

        _log.Warning(
            "the gate session for {Branch} stayed busy for {Seconds:0.0}s; question {Id} was not recorded on it — a hold's own notice still answers the hold",
            at.Branch, HoldClaimAttempts * HoldClaimWait.TotalSeconds, id);
    }

    /// <summary>One attempt, under the claim: false only when another call holds it right now.</summary>
    private bool TryRecordOnTheSession(SessionAddress at, string repoPath, string id)
    {
        using var claim = SessionClaim.TryTake(_settings.DataDir, repoPath, at.Branch, at.Document, at.Feature);
        if (claim is null)
        {
            return false;
        }

        // Re-read under the claim: the copy loaded before it may predate a resolve that has just landed,
        // and a hold released in between has nothing to record on.
        if (_store.Load(repoPath, at.Branch, at.Document, at.Feature) is not { } current
            || RoundMachine.RecordQuestion(current.State, id) is var recorded && ReferenceEquals(recorded, current.State))
        {
            return true;
        }

        try
        {
            _store.Save(current with { State = recorded });
        }
        catch (SessionStoreException e)
        {
            // The question still reaches the person; what is lost is the binding, and the log says so.
            _log.Warning(e, "question {Id} could not be recorded on the session of {Branch}", id, at.Branch);
        }

        return true;
    }

    /// <summary>How long <c>ask_human</c> waits for a busy session before asking unrecorded: twenty tries, fifty milliseconds apart.</summary>
    private const int HoldClaimAttempts = 20;

    private static readonly TimeSpan HoldClaimWait = TimeSpan.FromMilliseconds(50);

    /// <summary>The person's answer, exactly as they gave it.</summary>
    /// <remarks>
    /// It used to be translated back into the language the caller had asked in. The buttons
    /// removed the reason: a choice is not prose, and their free text — when they type any — is
    /// worth more unmediated than rendered into another language by a third model.
    /// </remarks>
    private static HumanAnswer AnswerFor(string answer) => new("answered", answer, answer, string.Empty);

    /// <summary>The session a caller's arguments name — the service's resolution, under the name the moved block calls it by.</summary>
    private SessionAddress AddressOf(string repoPath, string branch, string document, string feature) =>
        _addressOf(repoPath, branch, document, feature);

    /// <summary>A document review and a feature review are two different sessions — the service's own rule, forwarded.</summary>
    private static string Both(string document, string feature) => PanelService.Both(document, feature);

    private static string Json<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type) =>
        JsonSerializer.Serialize(value, type);

    /// <summary>A refusal, through the ONE place the wire shape is built. See <see cref="Refusal"/>.</summary>
    private string Error(string sentence, [CallerMemberName] string from = "") =>
        Refusal.Answer(sentence, _noticing, from);
}
