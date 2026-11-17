using CoaiMcp.Core.Rounds;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// What a deciding load found: the session to go on with, and — for a hold from before policy auto that policy auto could not release this
/// time — why (one of <see cref="LegacyHold"/>'s four reasons). Empty in every other case.
/// </summary>
internal readonly record struct Release(PersistedSession Session, string NotWritten);

/// <summary>
/// The current policy applied to a session a call has just loaded — and the one road by which a configuration releases a hold: policy auto
/// releases a hold a build BEFORE policy auto raised (<see cref="LegacyHold.Stands"/>), and no other.
/// </summary>
/// <remarks>
/// <para>The decisions are pure and live in <see cref="LegacyHold"/>; this class reads and writes. Five entries call it — the begin of every
/// review, <c>resolve</c>, <c>status</c>, <c>ask_human</c> and the fresh-document check — and <c>open</c> does not: it reads no hold and keeps the
/// config it loaded.</para>
/// <para><b>The order of <see cref="Released"/></b> is the whole safety argument. The person's answer on file wins and is read twice; a settings read
/// that failed authorises nothing; every notice is withdrawn BEFORE the session is saved, so a crash between the two leaves a held session whose
/// next load releases it again and rewrites no card; the note rides in the same save as the release; the projection into <c>coai.db</c> comes last
/// and cannot undo it. No line carries a question's text or an answer's.</para>
/// </remarks>
internal sealed class SessionPolicy(
    PanelSettings settings, SessionStore store, Escalations escalations, Projection projection, Serilog.ILogger log)
{
    /// <summary>The config under the current settings — <see cref="LegacyHold.Stamped"/>, on a session. The same object when nothing changes.</summary>
    internal PersistedSession Stamped(PersistedSession session)
    {
        var config = LegacyHold.Stamped(session.State, settings.Rounds);

        return ReferenceEquals(config, session.State.Config) ? session : session with { State = session.State with { Config = config } };
    }

    /// <summary>Releases a hold from before policy auto. The CALLER HOLDS the session's claim.</summary>
    internal Release Released(PersistedSession session)
    {
        if (!Eligible(session))
        {
            return new Release(session, string.Empty);
        }

        return Decided(session) ?? Write(session);
    }

    /// <summary><see cref="Released"/>, then <see cref="Stamped"/> on its session — for a caller that holds the claim.</summary>
    internal Release Current(PersistedSession session)
    {
        var release = Released(session);

        return release with { Session = Stamped(release.Session) };
    }

    /// <summary>The same for a caller that holds NO claim: it takes the claim for the release alone. Null for no session.</summary>
    internal Release? CurrentUnclaimed(PersistedSession? session, string repoPath, SessionAddress at)
    {
        if (session is null)
        {
            return null;
        }

        if (!Eligible(session))
        {
            return new Release(Stamped(session), string.Empty);
        }

        if (Decided(session) is { } done)
        {
            return done;
        }

        using var claim = SessionClaim.TryTake(settings.DataDir, repoPath, at.Branch, at.Document, at.Feature);
        if (claim is null)
        {
            log.Information(
                "policy auto did not release the {Stage} hold of session {SessionId} this call: another call holds the session — the next call tries again",
                StageOf(session), session.State.SessionId);

            return new Release(session, LegacyHold.ClaimBusy);
        }

        // Read AGAIN under the claim: what this call was handed may be older than the file — a resolve may have saved in between.
        return store.Load(repoPath, at.Branch, at.Document, at.Feature) is { } fresh
            ? Current(fresh)
            : new Release(session, LegacyHold.ClaimBusy);
    }

    /// <summary>Whether policy auto released this session's hold and the resolve that moves its stage has not come yet.</summary>
    /// <remarks>Read from the session itself — not held, both flags set, and the held round's note — so no member is added to the file.</remarks>
    internal static bool AwaitsItsResolve(PersistedSession session) =>
        session.State is { HumanGate: false, AwaitingResolve: true, AdvanceOnResolve: true }
        && session.Rounds is [.., { Note: LegacyHold.Note }];

    /// <summary>The stage's name as a sentence says it — the words <c>HoldNotice</c> uses.</summary>
    internal static string StageOf(PersistedSession session) => RoundSubject.StageName(session.State.Stage.ToString());

    // Step 1: not ours. This is the whole authority check, and it reads the config AS LOADED.
    private bool Eligible(PersistedSession session) => settings.Rounds.IsAutonomous && LegacyHold.Stands(session.State);

    // Steps 2 and 3: the two reasons to write nothing at all and withdraw nothing (step 2 may put a card back). Null: go on.
    private Release? Decided(PersistedSession session)
    {
        // The person's answer wins: any answer to any question of the hold — words with no button count — and the hold is theirs.
        if (CurrentAnswer.For(session, escalations) is not null)
        {
            OfferAgainTheNoticesWithdrawnBefore(session);

            return new Release(session, string.Empty);
        }

        // A failed read authorises nothing: the default it gave is policy auto, and a hold is not released on a default.
        if (settings.PolicyUnread)
        {
            log.Warning(
                "policy auto did not release the {Stage} hold of session {SessionId}: the settings file could not be read — the hold stays, and the next call reads the file again",
                StageOf(session), session.State.SessionId);

            return new Release(session, LegacyHold.SettingsUnread);
        }

        return null;
    }

    // Step 2's retry of a restore that failed (D16): the hold's NOTICES that are expired are put back while the person's answer is on file.
    // A standing hold's notice is expired by nothing but a withdrawal; a question card that ran out its own wait is not a notice and is left.
    private void OfferAgainTheNoticesWithdrawnBefore(PersistedSession session)
    {
        var failed = new List<string>();
        foreach (var id in session.State.HoldQuestions.Where(id => escalations.Read(id) is { Kind: EscalationKinds.Notice }))
        {
            switch (escalations.Restore(id))
            {
                case Restoration.Restored:
                    log.Information(
                        "a notice of the {Stage} hold of session {SessionId}, withdrawn by policy auto before the person answered, is offered again",
                        StageOf(session), session.State.SessionId);
                    break;
                case Restoration.Failed:
                    failed.Add(id);
                    break;
            }
        }

        WarnNotOffered(session, failed);
    }

    private void WarnNotOffered(PersistedSession session, List<string> failed)
    {
        if (failed.Count > 0)
        {
            log.Warning(
                "policy auto could not offer notice(s) {Ids} of the {Stage} hold of session {SessionId} again after the person's answer — the next call tries again",
                string.Join(", ", failed), StageOf(session), session.State.SessionId);
        }
    }

    // Steps 4 to 8: the release itself.
    private Release Write(PersistedSession session)
    {
        var now = DateTime.UtcNow;
        var withdrawn = new List<string>();
        var failed = new List<string>();
        foreach (var id in session.State.HoldQuestions)
        {
            switch (escalations.Withdraw(id, now))
            {
                case Withdrawal.Withdrawn:
                    withdrawn.Add(id);
                    break;
                case Withdrawal.Failed:
                    failed.Add(id);
                    break;
            }
        }

        // No short circuit above: every notice was tried. One that could not be withdrawn keeps the hold, and the next deciding load tries again.
        if (failed.Count > 0)
        {
            log.Warning(
                "policy auto could not withdraw notice(s) {Ids} of the {Stage} hold of session {SessionId} — the hold stays, and the next call tries again",
                string.Join(", ", failed), StageOf(session), session.State.SessionId);

            return new Release(session, LegacyHold.NoticeNotWithdrawn);
        }

        // The answers, read AGAIN: the last moment an answer to a card the panel still offered can exist. It is applied by today's readers, by
        // today's rules, so what this call withdrew is put back — the person can answer again — and nothing is released.
        if (CurrentAnswer.For(session, escalations) is not null)
        {
            var notRestored = new List<string>();
            var restored = 0;
            foreach (var id in withdrawn)
            {
                switch (escalations.Restore(id))
                {
                    case Restoration.Restored:
                        restored++;
                        break;
                    case Restoration.Failed:
                        notRestored.Add(id);
                        break;
                }
            }

            WarnNotOffered(session, notRestored);
            log.Information(
                "a person answered the {Stage} hold of session {SessionId} while policy auto was releasing it — their answer stands, nothing is released, and {Restored} notice(s) are offered again",
                StageOf(session), session.State.SessionId, restored);

            return new Release(session, string.Empty);
        }

        var held = session.Rounds.Count > 0 ? session.Rounds[^1] : null;
        var released = session with
        {
            State = LegacyHold.Released(session.State, settings.Rounds),
            Rounds = held is null ? session.Rounds : [.. session.Rounds[..^1], held with { Note = LegacyHold.Note }],
        };
        try
        {
            store.Save(released);
        }
        catch (SessionStoreException e)
        {
            log.Warning(
                e, "policy auto could not save the release of the {Stage} hold of session {SessionId} — the hold stays, and the next call tries again",
                StageOf(session), session.State.SessionId);

            return new Release(session, LegacyHold.SessionNotSaved);
        }

        // After the save, best effort: the projection cannot undo the release, and the note is in the session file either way.
        if (released.Rounds is [.., { } noted])
        {
            projection.Write(db => RoundNotes.Write(db, released.State.SessionId, noted.Stage, noted.Number, noted.LoggedNote), "the release note");
        }

        log.Warning(
            "policy auto released the {Stage} hold of session {SessionId} raised under {Policy} — a hold from a build before auto; {Withdrawn} notice(s) withdrawn",
            StageOf(session), session.State.SessionId, session.State.Config.OnExhausted, withdrawn.Count);

        return new Release(released, string.Empty);
    }
}
