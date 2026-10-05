using System.Runtime.InteropServices;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Context;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// <c>coai-mcp --check-consultant --caller &lt;kind&gt;</c>: ONE real, paid turn of the consultant a caller kind resolves to,
/// in a scratch repository with a marker inside and a canary outside — and exactly one JSON document on stdout saying
/// what came of it.
/// </summary>
/// <remarks>
/// <para><b>Why a mode.</b> The Consultant tab's Check button (epic 5) runs it; the panel never speaks MCP to this
/// binary, so it is a one-shot mode selected by <c>args[0]</c> before any transport opens (<c>.agents/PROJECT.md</c>),
/// and its stdout is its whole interface: one document, at the END. The deadline the panel derives its kill cap from
/// is in the state file from the start and in that document, never printed earlier.</para>
/// <para><b>Exit codes.</b> 0 for every classified outcome — answered, failed (with its kind, what and cure),
/// unavailable, already-checking. 65 for a request without a known <c>--caller</c>. 74 when the data directory cannot
/// hold the state or its lock cannot be opened at all, before anything is launched. Never 64: that means "this binary
/// has never heard of the mode", which is how the panel spots an old server.</para>
/// <para><b>The order is the safety.</b> Preflight first — a consultant that cannot be had launches nothing and takes
/// no lock. Then the HELD lock (<see cref="ConsultCheckLock"/>): a second check of the same kind answers
/// <c>already-checking</c> and launches nothing — and so does a fresh <c>checking</c> the OTHER side wrote into a data
/// directory both sides share, where the lock does not cross the seam. Then the persisted <c>checking</c> state,
/// before the scratch sweep and the paid launch (durable status: a reload shows the truth, and a check that dies leaves
/// a state every reader on this side settles to <c>abandoned</c> by the free lock). The heartbeat the holder writes is
/// the liveness signal for the OTHER side, where the lock cannot be seen; on this side the lock decides. The final state
/// is written by the holder, after the heartbeat has stopped, before the lock goes. The deadline in the state is the
/// PROMISED end — the cancellation plus the teardown after it — computed once, when the check began.</para>
/// <para>A unit of its own rather than more of <c>Program.cs</c>, for the reason <c>CadenceReadMode</c> gives.</para>
/// </remarks>
internal static class ConsultantCheckMode
{
    private const string Usage = "--check-consultant needs --caller <claude|codex|gemini|other>";

    /// <summary>
    /// How often the holder refreshes <c>heartbeatUtc</c> — the OTHER side's liveness signal, where the lock cannot be
    /// seen; on this side the lock decides. One constant, beside the rule that reads it.
    /// </summary>
    internal static TimeSpan HeartbeatEvery => ConsultCheckState.HeartbeatEvery;

    /// <param name="noticing">Where a notice goes — handed down from <c>Program</c>, which composes every one of them.</param>
    internal static async Task<int> RunAsync(string[] args, Noticing noticing)
    {
        var configuration = SettingsFile.Layer(
            SettingsFile.DataDirFrom(Environment.GetEnvironmentVariable).Path,
            Environment.GetEnvironmentVariable,
            // STDERR: stdout carries the one document the panel parses.
            Program.Note);
        using var stopping = new CancellationTokenSource();
        // A person's Ctrl+C or a SIGTERM ends the turn the way a cancellation does — the vendor's tree killed, the
        // state written as `cancelled` — instead of leaving it for a reader to call abandoned.
        ConsoleCancelEventHandler interrupted = (_, e) =>
        {
            e.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += interrupted;
        using var terminated = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            stopping.Cancel();
        });
        try
        {
            var launcher = new ProcessLauncher();
            // The same read `--providers` does: a consultant on an api row or somebody else's endpoint authenticates with a
            // key from the vault, and a check that launched it without one would test a launch no consultation makes.
            var keys = await KeyVault.ForThisMachine(launcher, Environment.GetEnvironmentVariable).ReadFromConfigurationAsync(configuration, stopping.Token);
            var settings = PanelSettings.FromEnvironment(configuration);
            var (code, answer, why) = args.Length > 0 && args[0] == "--check-model"
                ? await AnswerModelAsync(settings, await Console.In.ReadToEndAsync(stopping.Token), launcher, Path.GetTempPath(), Program.Note, noticing, stopping.Token, keys)
                : await AnswerAsync(settings, args, launcher, Path.GetTempPath(), Program.Note, noticing, stopping.Token, keys);
            if (answer.Length > 0)
            {
                await Console.Out.WriteLineAsync(answer);
            }

            if (why.Length > 0)
            {
                Program.Note(why);
            }

            return code;
        }
        finally
        {
            Console.CancelKeyPress -= interrupted;
        }
    }

    /// <summary>The exit code, what goes to stdout and what goes to stderr — the whole mode, with no console in it.</summary>
    /// <param name="tempRoot">Where the scratch repository is made — the temp directory, a test's own in a test.</param>
    /// <param name="warn">Where a problem that does not change the outcome is said — stderr.</param>
    internal static async Task<(int Code, string Out, string Err)> AnswerAsync(
        PanelSettings settings, string[] args, IProcessLauncher launcher, string tempRoot, Action<string> warn, Noticing noticing, CancellationToken ct)
        => await AnswerAsync(settings, args, launcher, tempRoot, warn, noticing, ct, VaultKeys.None("no vault was read"));

    /// <param name="keys">The vault, for a consultant on an api row or somebody else's endpoint.</param>
    internal static async Task<(int Code, string Out, string Err)> AnswerAsync(
        PanelSettings settings, string[] args, IProcessLauncher launcher, string tempRoot, Action<string> warn, Noticing noticing, CancellationToken ct, VaultKeys keys)
    {
        var kind = KindOf(args);
        if (kind.Length == 0)
        {
            return (65, string.Empty, Usage); // EX_DATAERR — never 64
        }

        try
        {
            return await CheckAsync(new ConsultantParts(settings, launcher, warn, noticing, keys), kind, tempRoot, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (74, string.Empty, $"the consultant check could not use the data directory {settings.DataDir}: {e.Message}"); // EX_IOERR
        }
    }

    /// <summary>
    /// <c>--check-model</c> (PLAN_one_model_catalog.md D10): the same paid check, of ONE catalog row read as
    /// <c>{"row": {…}}</c> on stdin — a row that reviews nothing (a consultant's, a Bugz model's) is on no wire this
    /// binary reads, and the row checked is the one on the screen. Its record is its own, <c>model-&lt;id&gt;</c>, so two
    /// rows check apart and neither touches a caller kind's.
    /// </summary>
    internal static async Task<(int Code, string Out, string Err)> AnswerModelAsync(
        PanelSettings settings, string stdin, IProcessLauncher launcher, string tempRoot, Action<string> warn, Noticing noticing, CancellationToken ct, VaultKeys keys)
    {
        var rows = RowsOf(stdin);
        if (rows.Count == 0)
        {
            return (65, string.Empty, ModelUsage); // EX_DATAERR — never 64
        }

        try
        {
            return await CheckRowAsync(new ConsultantParts(settings, launcher, warn, noticing, keys), ModelKey(rows[0]), rows[0], tempRoot, ct);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (74, string.Empty, $"the model check could not use the data directory {settings.DataDir}: {e.Message}"); // EX_IOERR
        }
    }

    private const string ModelUsage = "--check-model reads {\"row\": {\"id\": …, \"runtime\": …}} on stdin — the catalog row to check";

    /// <summary>The record a model's check is kept under — file-safe, apart from every caller kind's.</summary>
    internal static string ModelKey(ProviderSettings row) => $"model-{row.Provider}";

    /// <summary>The row on stdin, read by the settings' own row parser — none when the request holds no usable row.</summary>
    private static IReadOnlyList<ProviderSettings> RowsOf(string stdin)
    {
        try
        {
            using var request = System.Text.Json.JsonDocument.Parse(stdin);
            var root = request.RootElement;

            return root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("row", out var row) && row.ValueKind == System.Text.Json.JsonValueKind.Object
                ? PanelSettings.ParseVendors($"[{row.GetRawText()}]")
                : [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>The caller kind <c>--caller</c> names, or empty when it names none this binary knows.</summary>
    private static string KindOf(string[] args) =>
        Program.Flags(args).TryGetValue("--caller", out var asked) && CallerIdentity.Kinds.Contains(asked.Trim().ToLowerInvariant())
            ? asked.Trim().ToLowerInvariant()
            : string.Empty;

    private static async Task<(int Code, string Out, string Err)> CheckAsync(ConsultantParts parts, string kind, string tempRoot, CancellationToken ct)
    {
        var preflight = parts.Consultations.Preflight(kind);
        if (!preflight.Available)
        {
            return Printed(new ConsultCheckRecord { CallerKind = kind, State = ConsultCheckStates.Unavailable, Side = ConsultHealth.Side(), Reason = preflight.Reason });
        }

        // The preflight resolved this same entry an instant ago; a Definition is what it answered available for.
        var row = ((ResolvedConsultant.Definition)ConsultantResolver.Resolve(
            ConsultantRouting.For(parts.Settings.Consultants, kind), kind, parts.Settings.Providers)).Vendor;

        return await LockedAsync(parts, kind, row, tempRoot, ct);
    }

    /// <summary>A model's check: a row that cannot consult is unavailable, by name and before any lock; any other runs as a consultant's.</summary>
    private static async Task<(int Code, string Out, string Err)> CheckRowAsync(ConsultantParts parts, string key, ProviderSettings row, string tempRoot, CancellationToken ct) =>
        Runners.Consultation.ConsultantResolution.For(row.Identity()) is null
            ? Printed(new ConsultCheckRecord
            {
                CallerKind = key,
                State = ConsultCheckStates.Unavailable,
                Side = ConsultHealth.Side(),
                Vendor = row.Provider,
                Reason = Runners.Consultation.ConsultantResolution.CannotConsult(row.Identity()),
            })
            : await LockedAsync(parts, key, row, tempRoot, ct);

    /// <summary>
    /// One check of one row under ONE key — a caller kind's or a model's: the exclusive lock, the durable record, and a
    /// second paid launch beside a live one refused.
    /// </summary>
    private static async Task<(int Code, string Out, string Err)> LockedAsync(ConsultantParts parts, string key, ProviderSettings row, string tempRoot, CancellationToken ct)
    {
        var store = new ConsultCheckStore(parts.Settings.DataDir);
        using var held = ConsultCheckLock.TryTake(store, key);
        var stored = store.Read(key);
        if (AlreadyRunning(held, stored))
        {
            // Held here, or — in one data directory both sides share, where the lock does not cross the seam — a fresh
            // `checking` the other side wrote: either way a second paid launch beside it is the thing to refuse.
            return Printed(AlreadyChecking(key, stored));
        }

        return await UnderTheLockAsync(parts, store, new Begun(key, row, ConsultantCheck.Budget(parts.Settings.ReviewerTimeout), DateTime.UtcNow), tempRoot, ct);
    }

    /// <summary>Another check holds this side's lock — or the other side wrote a fresh <c>checking</c> the lock cannot see.</summary>
    private static bool AlreadyRunning(ConsultCheckLock? held, ConsultCheckRecord? stored) =>
        held is null || (stored is not null && ConsultCheckState.RunningOnTheOtherSide(stored, ConsultHealth.Side(), DateTime.UtcNow));

    /// <summary>What a check began with: who, on what, under which budget, and when — its deadlines are derived ONCE from this.</summary>
    private sealed record Begun(string Kind, ProviderSettings Row, TimeSpan Budget, DateTime StartedUtc)
    {
        public CheckDeadlines Deadlines { get; } = ConsultantCheck.Deadlines(StartedUtc, Budget);
    }

    private static async Task<(int Code, string Out, string Err)> UnderTheLockAsync(
        ConsultantParts parts, ConsultCheckStore store, Begun begun, string tempRoot, CancellationToken ct)
    {
        var checking = Started(begun);
        // Persisted BEFORE anything else — the unpaced scratch sweep included — and before anything is paid for; a data
        // directory that cannot hold it is the 74 above, with nothing launched.
        store.Write(checking);
        SweptAtStart(tempRoot, parts.Warn);
        using var whole = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // ONE deadline, computed when the check began: the same instant the promised end is derived from.
        whole.CancelAfter(Positive(begun.Deadlines.CancelAtUtc - DateTime.UtcNow));
        var deadline = begun.Deadlines.CancelAtUtc - begun.StartedUtc;
        var ended = await BeatingWhileAsync(
            stop => ConsultCheckHeartbeat.RunAsync(store, checking, HeartbeatEvery, parts.Warn, stop),
            () => ScratchAndTurnAsync(parts, checking, begun,
                () => ConsultationFailing.KilledAs(ct.IsCancellationRequested, whole.IsCancellationRequested, deadline), tempRoot, ct, whole.Token));

        return Finished(store, ended with { FinishedUtc = ConsultationStore.Stamp(DateTime.UtcNow) }, parts.Warn);
    }

    /// <summary>
    /// <paramref name="work"/> with the heartbeat running beside it — and the heartbeat STOPPED, awaited to its end,
    /// before the result is handed back, so a late beat can never put <c>checking</c> back over the final write.
    /// </summary>
    internal static async Task<ConsultCheckRecord> BeatingWhileAsync(Func<CancellationToken, Task> heartbeat, Func<Task<ConsultCheckRecord>> work)
    {
        using var beating = new CancellationTokenSource();
        var beat = heartbeat(beating.Token);
        try
        {
            return await work();
        }
        finally
        {
            await beating.CancelAsync();
            await beat;
        }
    }

    private static async Task<ConsultCheckRecord> ScratchAndTurnAsync(
        ConsultantParts parts, ConsultCheckRecord checking, Begun begun, Func<ConsultFailure> killedAs,
        string tempRoot, CancellationToken caller, CancellationToken turn)
    {
        var made = await ScratchAsync(parts, checking, begun, killedAs, tempRoot, turn);
        if (made.Outcome is not ScratchOutcome.Made { Scratch: var scratch })
        {
            return made.Ended;
        }

        try
        {
            return await parts.Turn().RunAsync(checking, begun.Row, scratch, begun.Budget, killedAs, caller, turn);
        }
        finally
        {
            ConsultCheckScratch.Delete(scratch.Root, parts.Warn);
        }
    }

    /// <summary>The scratch — or the record the check ends in without one.</summary>
    private sealed record Scratched(ScratchOutcome Outcome, ConsultCheckRecord Ended);

    /// <summary>
    /// The scratch, or how the check ended without it: git's refusal is <c>unavailable</c> with git's reason, and a
    /// cancellation or the deadline while it was being made is classified like any other — <c>cancelled</c> or
    /// <c>deadline</c> — never an "unavailable" that hides who stopped it (epic 4's code round).
    /// </summary>
    private static async Task<Scratched> ScratchAsync(
        ConsultantParts parts, ConsultCheckRecord checking, Begun begun, Func<ConsultFailure> killedAs, string tempRoot, CancellationToken turn)
    {
        try
        {
            var outcome = await ConsultCheckScratch.CreateAsync(parts.Launcher, tempRoot, parts.Warn, turn);

            return new Scratched(outcome, outcome is ScratchOutcome.Refused refused
                ? checking with { State = ConsultCheckStates.Unavailable, Reason = refused.Why }
                : checking);
        }
        catch (OperationCanceledException)
        {
            return new Scratched(new ScratchOutcome.Refused("stopped"), parts.Turn().Ended(checking, begun.Row, killedAs(), DateTime.UtcNow - begun.StartedUtc));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            var why = $"the scratch repository could not be written under {tempRoot}: {e.Message}";

            return new Scratched(new ScratchOutcome.Refused(why), checking with { State = ConsultCheckStates.Unavailable, Reason = why });
        }
    }

    /// <summary>Leftover scratch older than a day, removed at the start of every check — unpaced, after `checking` is on disk.</summary>
    private static void SweptAtStart(string tempRoot, Action<string> warn)
    {
        var swept = ConsultCheckScratch.Sweep(tempRoot, DateTime.UtcNow, warn);
        if (swept > 0)
        {
            warn($"consultant check: removed {swept} leftover scratch director(y/ies) older than a day");
        }
    }

    private static TimeSpan Positive(TimeSpan span) => span > TimeSpan.Zero ? span : TimeSpan.Zero;

    /// <summary>The final state, written by the holder — and printed even when the write failed, because stdout is the interface.</summary>
    private static (int Code, string Out, string Err) Finished(ConsultCheckStore store, ConsultCheckRecord ended, Action<string> warn)
    {
        try
        {
            store.Write(ended);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warn($"consultant check: the result could not be written to {store.StatePath(ended.CallerKind)}: {e.Message}");
        }

        return Printed(ended);
    }

    /// <summary>The <c>checking</c> state: its deadline is the PROMISED end — the cancellation plus the teardown after it.</summary>
    private static ConsultCheckRecord Started(Begun begun) => new()
    {
        CallerKind = begun.Kind,
        State = ConsultCheckStates.Checking,
        StartedUtc = ConsultationStore.Stamp(begun.StartedUtc),
        DeadlineUtc = ConsultationStore.Stamp(begun.Deadlines.PromisedUtc),
        HeartbeatUtc = ConsultationStore.Stamp(begun.StartedUtc),
        Side = ConsultHealth.Side(),
        Vendor = begun.Row.Provider,
        Runtime = RuntimeResolution.NameOf(begun.Row.Identity()),
        Model = begun.Row.Model,
    };

    /// <summary>The answer when another check is running: that check's own times, side and vendor, so the panel can show them.</summary>
    /// <remarks>
    /// Only from a state that says <c>checking</c>: the holder may not have written its own yet, and a PREVIOUS check's
    /// result — its marker, its canary — copied into this answer would describe a turn this one never saw.
    /// </remarks>
    private static ConsultCheckRecord AlreadyChecking(string kind, ConsultCheckRecord? stored)
    {
        var running = stored is { State: ConsultCheckStates.Checking } ? stored : new ConsultCheckRecord { Side = ConsultHealth.Side() };

        return new ConsultCheckRecord
        {
            CallerKind = kind,
            State = ConsultCheckStates.AlreadyChecking,
            StartedUtc = running.StartedUtc,
            DeadlineUtc = running.DeadlineUtc,
            HeartbeatUtc = running.HeartbeatUtc,
            Side = running.Side.Length > 0 ? running.Side : ConsultHealth.Side(),
            Vendor = running.Vendor,
            Runtime = running.Runtime,
            Model = running.Model,
            Reason = $"another check of the {kind} consultant is running — nothing was launched; its result will replace this one",
        };
    }

    private static (int Code, string Out, string Err) Printed(ConsultCheckRecord record) =>
        (0, ConsultCheckStore.Serialize(record), string.Empty);
}

/// <summary>
/// The holder's heartbeat: <c>heartbeatUtc</c> rewritten on a timer — the liveness signal on the OTHER side of the
/// Windows/WSL seam, and for a reader that cannot open the lock; on this side the lock decides.
/// </summary>
/// <remarks>
/// A beat that cannot be written is said and the next one tries again; the loop ends when the check cancels it, and
/// the check waits for it to end before the final write (<see cref="ConsultantCheckMode.BeatingWhileAsync"/>).
/// </remarks>
internal static class ConsultCheckHeartbeat
{
    public static async Task RunAsync(ConsultCheckStore store, ConsultCheckRecord checking, TimeSpan every, Action<string> warn, CancellationToken stop)
    {
        using var timer = new PeriodicTimer(every);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                Beat(store, checking, warn);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the check, which is how it ends.
        }
    }

    private static void Beat(ConsultCheckStore store, ConsultCheckRecord checking, Action<string> warn)
    {
        try
        {
            store.Write(checking with { HeartbeatUtc = ConsultationStore.Stamp(DateTime.UtcNow) });
        }
        catch (Exception failure)
        {
            // The detached loop's catch-all: the next beat tries again.
            warn($"consultant check: a heartbeat could not be written, and the next one will try again: {failure.Message}");
        }
    }
}
