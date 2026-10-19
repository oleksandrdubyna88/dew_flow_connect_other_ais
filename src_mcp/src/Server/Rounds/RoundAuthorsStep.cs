using System.Collections.Immutable;
using CoaiMcp.Core.Authors;
using CoaiMcp.Runners.Git;
using CoaiMcp.Store;

namespace CoaiMcp.Server;

/// <summary>
/// Who wrote the code a finished round reviewed, read and attributed on the answer path, under a budget (E5.S4 of
/// <c>todo/PLAN_a_round_records_who_wrote_the_code_and_how_much_E5.md</c>): the caller's transcript is located, read text-free, assigned to the
/// round's windows by E4 and kept as an <see cref="AuthorsSnapshot"/> — within <see cref="AuthorsBudget"/>, never failing the round.
/// </summary>
/// <remarks>
/// <para><b>Which rounds:</b> a SETTLED plan or code round — recorded and <c>done</c>, by E4's own rule (<see cref="Windows.IsSettled"/>) —
/// gets a snapshot; every other round gets null, which reads <i>not recorded</i>. <c>Assign</c> is never called for anything else.</para>
/// <para><b>The order:</b> locate (a refusal ends the step, no peer query) → peers and the gate's own timeline (one read-only lookup) →
/// <see cref="RoundAttribution.ReadFrom"/> → the reader, bounded by the time left → <see cref="AttributionMapping"/> → <c>Assign</c> →
/// <see cref="RoundAuthorsBuilder.Build"/> → the snapshot. <b>This step writes no mapping of its own.</b> Everything E5 builds WITHOUT E4 is
/// <see cref="AuthorsSnapshot.Unknown"/> — no row, every count <c>-1</c> — with <c>Reader</c> and <c>Attribution</c> <c>0</c> for the
/// component that did not run.</para>
/// <para><b>Seams</b> (no interface for one implementation): the caller's environment, the clock, the budget, the peer source, the reader,
/// <c>Assign</c> and the session loader are constructor parameters whose defaults are the real thing. The identity is read ONLY through the
/// injected environment, never <c>Environment.GetEnvironmentVariable</c> — a suite run inside Claude Code holds a real session id.</para>
/// <para><b>Diagnostics are never states.</b> The reader's counts are logged at Debug, as numbers; none becomes a reason, a column or a
/// snapshot member, and no line carries a byte of what a transcript said.</para>
/// </remarks>
internal sealed class RoundAuthorsStep
{
    private readonly PanelSettings _settings;
    private readonly Serilog.ILogger _log;
    private readonly TimeProvider _clock;
    private readonly Func<string, string?> _env;
    private readonly AuthorsBudget _budget;
    private readonly AuthorPeerSource _peers;
    private readonly Func<CallerTranscriptLocation, ReadBudget, DateTime?, ObservedSession> _read;
    private readonly Func<AttributionInput, WindowAssignment> _assign;

    public RoundAuthorsStep(
        PanelSettings settings,
        Serilog.ILogger log,
        SessionStore store,
        Func<string, string?>? environment = null,
        TimeProvider? clock = null,
        AuthorsBudget? budget = null,
        AuthorPeerSource? peers = null,
        Func<CallerTranscriptLocation, ReadBudget, DateTime?, ObservedSession>? read = null,
        Func<AttributionInput, WindowAssignment>? assign = null,
        Func<string, string, PersistedSession?>? load = null)
    {
        _settings = settings;
        _log = log;
        _clock = clock ?? TimeProvider.System;
        _env = environment ?? Environment.GetEnvironmentVariable;
        _budget = budget ?? AuthorsBudget.Default;
        _peers = peers ?? RealPeers(settings, load ?? ((repo, branch) => store.Load(repo, branch)), _clock);
        _read = read ?? CallerTranscript.Read;
        _assign = assign ?? RoundAttribution.Assign;
    }

    /// <summary>The peer lookup over the real store: a short read-only connection per call, opened and closed here.</summary>
    private static AuthorPeerSource RealPeers(PanelSettings settings, Func<string, string, PersistedSession?> load, TimeProvider clock) =>
        (lookup, deadline) =>
        {
            using var connection = AuthorPeers.OpenReadOnly(settings.DataDir);

            return AuthorPeers.Find(connection, lookup, load, deadline, clock);
        };

    /// <summary>
    /// The snapshot of <paramref name="record"/> — a round about to be saved, which <paramref name="session"/> does not hold yet — or null
    /// for a round that is not settled. Never throws for a failing reader: that is <c>unobserved: reader failed</c>.
    /// </summary>
    public AuthorsSnapshot? For(PersistedSession session, RoundRecord record)
    {
        if (!IsSettled(record))
        {
            return null;
        }

        var started = _clock.GetTimestamp();
        var reached = new Reached();
        try
        {
            return Logged(Attribute(session, record, _clock.GetUtcNow() + _budget.MaxTime, reached), started);
        }
        catch (Exception e)
        {
            // The type only, never the message: an exception from a reader may quote what it read.
            _log.Warning("the authors of {Stage} round {Number} could not be attributed: {Failure}", record.Stage, record.Number, e.GetType().Name);

            return Logged(Failed(record, reached, AuthorsSnapshot.ReaderFailed), started);
        }
    }

    /// <summary>What a failure can still say: which id and which version the run had reached before it stopped.</summary>
    private sealed class Reached
    {
        public CallerSessionId Session { get; set; } = CallerSessionId.None;

        public int Reader { get; set; }
    }

    private static bool IsSettled(RoundRecord record) =>
        Windows.IsSettled(AttributionMapping.Timeline(string.Empty, string.Empty, default, [record], _ => string.Empty).Rounds[0]);

    private AuthorsSnapshot Attribute(PersistedSession session, RoundRecord record, DateTimeOffset deadline, Reached reached) =>
        CallerTranscriptLocator.Locate(record.Caller?.Vendor ?? string.Empty, _env, _settings.TranscriptHome) switch
        {
            CallerTranscriptLocation.Found found => Observe(new Subject(session, record, found, deadline), reached),
            CallerTranscriptLocation.NotObservable refused => Unobserved(record, refused),
            _ => throw new InvalidOperationException("A transcript location is Found or NotObservable."),
        };

    private static AuthorsSnapshot Unobserved(RoundRecord record, CallerTranscriptLocation.NotObservable refused) =>
        AuthorsSnapshot.Unknown(record.Stage, record.Number, refused.Session.Id, ObservationKinds.Unobserved, [refused.Reason], refused.Session.Source, 0, 0);

    /// <summary>One round being attributed: what the phases share.</summary>
    private sealed record Subject(PersistedSession Session, RoundRecord Record, CallerTranscriptLocation.Found Found, DateTimeOffset Deadline)
    {
        public string Caller => Found.Session.Id;
    }

    private AuthorsSnapshot Observe(Subject subject, Reached reached)
    {
        reached.Session = subject.Found.Session;
        var set = _peers(new PeerLookup(subject.Caller, subject.Session.State.SessionId), subject.Deadline);
        if (Past(subject.Deadline))
        {
            return OutOfTime(subject, reader: 0);
        }

        var gate = GateOf(subject, set);
        var peers = set.PeersKnown ? set.Peers : [];
        var bound = RoundAttribution.ReadFrom(InputOf(subject, EmptyObservation(subject.Caller), gate, peers, set.PeersKnown));
        var observed = _read(subject.Found, new ReadBudget(_budget.MaxBytes, Left(subject.Deadline), _clock), bound?.UtcDateTime);
        reached.Reader = ObservedSession.ReaderVersion;
        LogDiagnostics(observed);

        return Past(subject.Deadline) ? OutOfTime(subject, ObservedSession.ReaderVersion) : Assigned(subject, observed, gate, peers, set.PeersKnown);
    }

    private AuthorsSnapshot Assigned(Subject subject, ObservedSession observed, GateTimeline gate, ImmutableArray<GateTimeline> peers, bool peersKnown)
    {
        var input = InputOf(subject, AttributionMapping.Observation(observed), gate, peers, peersKnown);
        WindowAssignment assignment;
        try
        {
            assignment = _assign(input);
        }
        catch (ArgumentException)
        {
            // E4 refused the input (a duplicated journal round, a caller that is not the round's): never repaired, never merged.
            return AuthorsSnapshot.Unknown(
                subject.Record.Stage, subject.Record.Number, subject.Caller, ObservationKinds.Unobserved, [AuthorsSnapshot.InvalidInput],
                subject.Found.Session.Source, ObservedSession.ReaderVersion, 0);
        }

        var authors = RoundAuthorsBuilder.Build(input, assignment);
        var (submodule, uncovered) = WriteCounts(authors, subject.Session.State.RepoPath);
        var snapshot = AuthorsSnapshot.From(authors, subject.Found.Session.Source, ObservedSession.ReaderVersion, RoundAttribution.Version, submodule, uncovered);

        return Past(subject.Deadline) ? WithBudgetStop(snapshot) : snapshot;
    }

    /// <summary>The gate's own timeline: the round being attributed APPENDED (the save has not added it), each earlier round's caller by §3's order.</summary>
    private static GateTimeline GateOf(Subject subject, AuthorPeerSet set) =>
        AttributionMapping.Timeline(
            subject.Session.State.SessionId,
            subject.Session.State.RepoPath,
            subject.Session.OpenedUtc,
            [.. subject.Session.Rounds, subject.Record],
            round => ReferenceEquals(round, subject.Record) ? subject.Caller : EarlierCaller(round, set));

    private static string EarlierCaller(RoundRecord round, AuthorPeerSet set) =>
        round.Authors is { Session.Length: > 0 } snapshot
            ? snapshot.Session
            : set.EarlierCallers.GetValueOrDefault((round.Stage, round.Number)) ?? string.Empty;

    private static CallerObservation EmptyObservation(string caller) =>
        new(caller, new Core.Authors.ObservationState(ObservationKinds.Observed, []), [], [], [], []);

    private static AttributionInput InputOf(Subject subject, CallerObservation observation, GateTimeline gate, ImmutableArray<GateTimeline> peers, bool peersKnown) =>
        new(observation, gate, peers, AttributionMapping.Universe(gate, peers, observation), subject.Record.Stage, subject.Record.Number, peersKnown);

    /// <summary>
    /// Writes into another checkout, as counts only (gate finding 0 — no root is stored): every call listed is uncovered, and the ones whose
    /// root is a declared submodule of the checkout are also submodule writes. <c>-1</c> both when E4 found nothing to attribute — an
    /// empty list would otherwise read as a measured zero (M1).
    /// </summary>
    private (int Submodule, int Uncovered) WriteCounts(RoundAuthors authors, string repoPath)
    {
        if (authors.State == ObservationKinds.Unobserved)
        {
            return (AuthorsSnapshot.NotMeasured, AuthorsSnapshot.NotMeasured);
        }

        var mounts = Mounts(repoPath);

        return (authors.WritesOutsideTheGateCheckout.Where(writes => mounts.Any(mount => Repositories.Contains(mount, writes.Root))).Sum(writes => writes.Calls),
            authors.WritesOutsideTheGateCheckout.Sum(writes => writes.Calls));
    }

    /// <summary>The declared submodule paths of the checkout, parsed rather than asked of git — no process is launched on the answer path.</summary>
    private List<string> Mounts(string repoPath)
    {
        var mounts = GitModules.In(repoPath).Select(mount => Repositories.Normalise(repoPath) + "/" + mount.Path).ToList();
        if (mounts.Count == 0)
        {
            _log.Debug("no submodule is declared in {Repository}: no write is counted as one", repoPath);
        }

        return mounts;
    }

    // ---------- the budget ----------

    private bool Past(DateTimeOffset deadline) => _clock.GetUtcNow() >= deadline;

    private TimeSpan Left(DateTimeOffset deadline) => deadline - _clock.GetUtcNow();

    /// <summary>The deadline passed before attribution ran: nothing measured, so no rows and every count unknown.</summary>
    private static AuthorsSnapshot OutOfTime(Subject subject, int reader) =>
        AuthorsSnapshot.Unknown(
            subject.Record.Stage, subject.Record.Number, subject.Caller, ObservationKinds.Partial, [ObservationReasons.ReadBudget],
            subject.Found.Session.Source, reader, 0);

    /// <summary>The deadline passed AFTER attribution: what was built is kept, and its roll-up says the budget was spent.</summary>
    private static AuthorsSnapshot WithBudgetStop(AuthorsSnapshot snapshot) => snapshot.State switch
    {
        ObservationKinds.Observed => snapshot with { State = ObservationKinds.Partial, Reasons = [ObservationReasons.ReadBudget] },
        ObservationKinds.Partial when !snapshot.Reasons.Contains(ObservationReasons.ReadBudget) => snapshot with { Reasons = [.. snapshot.Reasons, ObservationReasons.ReadBudget] },
        _ => snapshot,
    };

    private AuthorsSnapshot Failed(RoundRecord record, Reached reached, string reason) =>
        AuthorsSnapshot.Unknown(
            record.Stage, record.Number, reached.Session.Id, ObservationKinds.Unobserved, [reason], reached.Session.Source, reached.Reader, 0);

    // ---------- the log ----------

    private AuthorsSnapshot Logged(AuthorsSnapshot snapshot, long started)
    {
        _log.Debug(
            "authors of {Stage} round {Number}: {State} in {Elapsed:0} ms, {Rows} row(s)",
            snapshot.Stage, snapshot.Number, snapshot.Render(), _clock.GetElapsedTime(started).TotalMilliseconds, snapshot.Rows.Length);

        return snapshot;
    }

    /// <summary>Numbers only: what the reader counted, never a word of what it read.</summary>
    private void LogDiagnostics(ObservedSession observed)
    {
        var d = observed.Diagnostics;
        _log.Debug(
            "authors reader: files {FilesRead} read, {FilesSkipped} skipped by last write, {BytesScanned} B scanned, {BytesInSlice} B in the slice, "
            + "{LinesSkipped} lines skipped, {LinesTooLong} too long, {ReadFailures} read failures, {TornTails} torn tails, {SyntheticLines} synthetic, "
            + "{MetasWithoutModel} metas without a model, {ValuesOffShape} values off shape, {WriteCallsRefused} write calls refused, "
            + "{WriteCallsUnanswered} unanswered, {MessagesWithoutUsage} messages without usage, {MessagesDropped} dropped at the budget, "
            + "{AgentFilesEmpty} agent files empty",
            d.FilesRead, d.FilesSkippedByLastWrite, d.BytesScanned, d.BytesInSlice, d.LinesSkippedBeforeParse, d.LinesTooLong, d.ReadFailures,
            d.TornTails, d.SyntheticLines, d.MetasWithoutModel, d.ValuesOffShape, d.WriteCallsRefused, d.WriteCallsUnanswered,
            d.MessagesWithoutUsage, d.MessagesDroppedAtBudget, d.AgentFilesEmpty);
    }
}
