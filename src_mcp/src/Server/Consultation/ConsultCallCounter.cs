using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CoaiMcp.Server;

/// <summary>Where a call was taken from — and so the one store a give-back may return it to.</summary>
public enum CounterStore
{
    /// <summary>Nothing was taken: the call was refused, or it was never counted (an ordered consultation).</summary>
    None,

    /// <summary>The per-caller file under <c>consultations/callers</c>.</summary>
    File,

    /// <summary>The in-process fallback, used while the file cannot be written.</summary>
    Memory,
}

/// <summary>
/// The exact call a <see cref="ConsultCallCounter.TryTake"/> took: whose, from which window, from which store.
/// </summary>
/// <remarks>
/// A give-back is fenced on all three. A refund that did not name its window would, after a rollover, hand
/// a call to the NEW window that the old one spent — a fresh window with one call more than its cap. One
/// that did not name its store could take a call from the file and return it to memory, where it was
/// never counted.
/// </remarks>
public sealed record CounterTake(string Caller, DateTime WindowStart, CounterStore Store)
{
    /// <summary>Nothing taken: a give-back of this refunds nothing.</summary>
    public static readonly CounterTake Nothing = new(string.Empty, DateTime.MinValue, CounterStore.None);
}

/// <summary>What one attempt to take a call from the counter produced.</summary>
/// <param name="Note">Empty ordinarily; a sentence when the counter is held in memory, or could not be read at all.</param>
public sealed record CounterOutcome(bool Allowed, int Used, string Note)
{
    /// <summary>The call that was taken, for a give-back — <see cref="CounterTake.Nothing"/> when none was.</summary>
    public CounterTake Take { get; init; } = CounterTake.Nothing;
}

/// <summary>
/// A take that can be given back ONCE — whichever failure path of a turn gets there first.
/// </summary>
/// <remarks>
/// <para>Epic 2's review (two reviewers, independently): a failed turn gave its call back, then its record
/// write threw, and the catch-all classified the same turn again — and gave back again, with the same take.
/// The counter's own fences could not see it: both refunds named the right window and the right store. The
/// rule "one turn, at most one refund" belongs to the turn, so the turn carries this, and every refund goes
/// through <see cref="Claim"/>.</para>
/// <para>A class, deliberately, in a codebase of records: it is the one piece of state a turn must not be
/// able to copy. Atomic, so two paths racing for it cannot both win.</para>
/// </remarks>
public sealed class SingleUseTake(CounterTake take)
{
    private int _claimed;

    /// <summary>The take the first time; <see cref="CounterTake.Nothing"/> every time after.</summary>
    public CounterTake Claim() => Interlocked.Exchange(ref _claimed, 1) == 0 ? take : CounterTake.Nothing;
}

/// <summary>
/// The per-caller call cap: how many consult calls one caller session has made in its window — and the
/// bounded give-back of a call a vendor failed to serve.
/// </summary>
/// <remarks>
/// <para>The <see cref="CallerSessions"/> claim shape — one file per caller, held
/// <see cref="FileShare.None"/> while it is read, decided and rewritten — because two servers share
/// one data directory as a matter of course and a read-then-write would let both count the same call
/// as the first.</para>
/// <para><b>It never fails open.</b> The split-order claim fails open because a duplicate instruction
/// is cheaper than a silently disabled feature; a call cap that fails open is a runaway agent on a
/// paid vendor, which two reviewers called out on the plan round. When the file cannot be written the
/// cap is enforced IN MEMORY for the lifetime of this server process, and the reply says so. When a file
/// EXISTS but cannot be read — held past the contention budget, or torn into something that does not parse
/// — it says nothing about what was spent, and the caller is held at its cap with a sentence naming the
/// file, rather than handed a fresh window (epic 2's review, the gate's finding). Only a file that does
/// not exist is a fresh window.</para>
/// <para><b>The give-back keeps that property, and that is why it is fenced three ways</b>
/// (PLAN_the_consultant_works_on_every_vendor.md, E2.3). A turn that produced no advice for a reason
/// outside the caller's control — the vendor denied a command, ran out of quota, could not be started —
/// returns the call it took, so an agent is not starved of help by a vendor's failure. But a refund is
/// an increment of the cap, and an unfenced one is the cap failing open by a side door: so it returns
/// ONLY the take it names (same window — a rollover refuses it; same store — a file take is never
/// refunded into memory, nor the reverse), at most <see cref="GivesBackPerWindow"/> times per caller per
/// window, the count kept in the same file under the same lock as the take (and mirrored in memory for
/// the fallback). A refund that cannot be written is refused: the call stays spent, which is the side
/// this class has always failed on. A turn refunds at most once (<see cref="SingleUseTake"/>).</para>
/// <para>The file is one line, <c>start ⇥ used ⇥ givenBack ⇥ caller</c>. A file written before the
/// give-back existed has three fields (<c>start ⇥ used ⇥ caller</c>) and reads as none given back. <b>The
/// halves ship out of step</b>: an older server rewriting the file writes the three-field shape again, so
/// the per-window bound on give-backs resets — the cap itself does not.</para>
/// <para><b>The rewrite never truncates first.</b> The new line is written from the start of the file and
/// the file then cut to it, so a process killed mid-rewrite leaves the old count or a torn line — which
/// reads as the cap reached, above — and never an empty file that reads as a fresh window.</para>
/// </remarks>
public sealed class ConsultCallCounter(string dataDir)
{
    public static readonly TimeSpan Window = CallerSessions.Remembers;

    /// <summary>How many failed calls one caller may have handed back in one window.</summary>
    /// <remarks>
    /// Three: enough that a vendor's bad hour does not cost an agent its whole budget, few enough that a
    /// vendor that fails every time cannot turn the cap into an unlimited loop of paid launches — the
    /// fourth failure in a window keeps its call.
    /// </remarks>
    public const int GivesBackPerWindow = 3;

    /// <summary>
    /// The fallback count, held per DATA DIRECTORY rather than per instance or per process.
    /// </summary>
    /// <remarks>
    /// <para>It cannot be an instance field: <see cref="PanelServiceHost"/> rebuilds the service
    /// whenever the panel rewrites its settings file, so an instance-scoped count would reset every
    /// time somebody touched a control — which is the cap quietly turning itself off.</para>
    /// <para>It is keyed by the data directory as well as the caller, so two servers with different
    /// data directories in one process — which is what a test run is — cannot see each other's
    /// counts. The values are immutable records replaced whole under the lock, never mutated in
    /// place. (codex and gemini, code round, from two directions.)</para>
    /// </remarks>
    private static readonly Lock Gate = new();

    private static readonly Dictionary<string, Spent> Memory = new(StringComparer.Ordinal);

    private sealed record Spent(DateTime Start, int Count, int GivenBack = 0);

    /// <summary>What reading a counter file found: nothing yet, a count, or a file that says nothing.</summary>
    private abstract record Read
    {
        private Read() { }

        public sealed record Fresh : Read;

        public sealed record Recorded(Spent Spent) : Read;

        public sealed record Unreadable(string Why) : Read;
    }

    private string Dir => Path.Combine(dataDir, "consultations", "callers");

    /// <summary>How long to keep trying for the claim when another call in this process holds it.</summary>
    /// <remarks>
    /// Two consult calls from one caller session can overlap — a retry, a second agent thread — and
    /// <see cref="FileShare.None"/> refuses the second one outright. Without this it would fall
    /// straight to the memory count and report the file as unwritable, which is true of the instant
    /// and false about the machine. Milliseconds, because the held section is a read and a write.
    /// (gemini, code round.)
    /// </remarks>
    private static readonly TimeSpan Contention = TimeSpan.FromSeconds(2);

    public CounterOutcome TryTake(string caller, int cap, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);

        return Contended(
            () => TakeFromFile(caller, cap, nowUtc),
            e => TakeFromMemory(caller, cap, nowUtc, e.Message));
    }

    /// <summary>
    /// Returns the call <paramref name="take"/> took — when its window is still the current one, its
    /// store is the one that holds the count, and this caller has given back fewer than
    /// <see cref="GivesBackPerWindow"/> calls in that window. Answers whether it was given back.
    /// </summary>
    public bool GiveBack(CounterTake take, DateTime nowUtc) => take.Store switch
    {
        // Refused, never moved to the other store, when the file stays held: the call stays spent.
        CounterStore.File => Contended(() => RefundFile(take, nowUtc), _ => false),
        CounterStore.Memory => GiveBackToMemory(take, nowUtc),
        _ => false,
    };

    /// <summary>
    /// The one contention loop: <paramref name="attempt"/> again while the file is held, for
    /// <see cref="Contention"/>, then <paramref name="exhausted"/> with what the last attempt said.
    /// </summary>
    /// <remarks>It was written twice — the take and the refund — and the two copies were one edit from differing.</remarks>
    private static T Contended<T>(Func<T> attempt, Func<Exception, T> exhausted)
    {
        var deadline = DateTime.UtcNow + Contention;
        while (true)
        {
            try
            {
                return attempt();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return exhausted(e);
                }

                Thread.Sleep(Random.Shared.Next(5, 25));
            }
        }
    }

    /// <summary>
    /// What the counter would say WITHOUT taking a call — for a gate that has to know whether a consultant can
    /// still be had before it refuses a question (S3 of the question consultant, D9). Memory first when this
    /// process is already counting in memory, else the file; a file that exists and cannot be read holds the
    /// caller at its cap, as <see cref="TryTake"/> does — "unreadable" is not "nothing spent" (epic 2's review of
    /// PLAN_the_consultant_works_on_every_vendor.md, reconciled with the question consultant on the rebase).
    /// </summary>
    public CounterOutcome Peek(string caller, int cap, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caller);
        Read seen;
        lock (Gate)
        {
            seen = Memory.TryGetValue(KeyFor(caller), out var known) && nowUtc - known.Start < Window
                ? new Read.Recorded(known)
                : Persisted(caller);
        }

        if (seen is Read.Unreadable unreadable)
        {
            return HeldAtTheCap(FileFor(caller), cap, unreadable.Why);
        }

        var spent = Current(seen, nowUtc);

        return new CounterOutcome(spent.Count < cap, spent.Count, string.Empty);
    }

    private CounterOutcome TakeFromFile(string caller, int cap, DateTime nowUtc)
    {
        Directory.CreateDirectory(Dir);
        var path = FileFor(caller);
        var existed = File.Exists(path);
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(stream, leaveOpen: true);

        return Parse(reader.ReadToEnd(), existed) switch
        {
            Read.Unreadable unreadable => HeldAtTheCap(path, cap, unreadable.Why),
            var read => TakeFrom(stream, Current(read, nowUtc), caller, cap),
        };
    }

    private static CounterOutcome TakeFrom(FileStream stream, Spent spent, string caller, int cap)
    {
        if (spent.Count >= cap)
        {
            return new CounterOutcome(false, spent.Count, string.Empty);
        }

        Rewrite(stream, spent with { Count = spent.Count + 1 }, caller);

        return new CounterOutcome(true, spent.Count + 1, string.Empty) { Take = new CounterTake(caller, spent.Start, CounterStore.File) };
    }

    /// <summary>The refund, under the same lock as the take. No file is no refund — at once, not after a contention wait.</summary>
    private bool RefundFile(CounterTake take, DateTime nowUtc)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(FileFor(take.Caller), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }

        using (stream)
        using (var reader = new StreamReader(stream, leaveOpen: true))
        {
            if (Parse(reader.ReadToEnd(), existed: true) is not Read.Recorded { Spent: var spent } || !Refundable(spent, take, nowUtc))
            {
                return false;
            }

            Rewrite(stream, Refunded(spent), take.Caller);

            return true;
        }
    }

    private CounterOutcome TakeFromMemory(string caller, int cap, DateTime nowUtc, string why)
    {
        var key = KeyFor(caller);
        lock (Gate)
        {
            // SEEDED from the file when this process has not counted for this caller yet. Starting
            // at zero was a hole in the fail-closed contract: a write failure made every restart —
            // and every second server on the same data directory — hand out a whole fresh cap of
            // paid calls, while the note said the cap was "being enforced". The bytes are usually
            // still READABLE when the failure is a lock or a read-only directory, which is the
            // common case; when they are not, the caller is held at its cap — "unreadable" is not
            // "nothing spent". (CodeRabbit, on the pull request; epic 2's review.)
            var seeded = Memory.TryGetValue(key, out var known) && nowUtc - known.Start < Window
                ? new Read.Recorded(known)
                : Persisted(caller);

            return seeded is Read.Unreadable unreadable
                ? HeldAtTheCap(FileFor(caller), cap, unreadable.Why)
                : TakeInMemory(key, caller, Current(seeded, nowUtc), cap, why);
        }
    }

    /// <summary>The take itself, under <see cref="Gate"/>.</summary>
    private CounterOutcome TakeInMemory(string key, string caller, Spent spent, int cap, string why)
    {
        var note = $"the call counter at {Dir} could not be persisted ({why}); the cap of {cap} is being enforced in memory for this server process";
        if (spent.Count >= cap)
        {
            return new CounterOutcome(false, spent.Count, note);
        }

        // Replaced whole, never mutated: the value is a record and the dictionary entry is the
        // only thing that moves.
        Memory[key] = spent with { Count = spent.Count + 1 };

        return new CounterOutcome(true, spent.Count + 1, note) { Take = new CounterTake(caller, spent.Start, CounterStore.Memory) };
    }

    private bool GiveBackToMemory(CounterTake take, DateTime nowUtc)
    {
        var key = KeyFor(take.Caller);
        lock (Gate)
        {
            if (!Memory.TryGetValue(key, out var spent) || !Refundable(spent, take, nowUtc))
            {
                return false;
            }

            Memory[key] = Refunded(spent);

            return true;
        }
    }

    /// <summary>A file that exists and says nothing: the cap is reached, and the sentence names the file.</summary>
    private static CounterOutcome HeldAtTheCap(string path, int cap, string why) =>
        new(false, cap,
            $"the call counter at {path} exists but could not be read ({why}) — this caller is held at its cap of {cap} "
            + "until the file is readable again or removed");

    /// <summary>The fences: the take's own window, still current, under the per-window bound, with a call to return.</summary>
    private static bool Refundable(Spent spent, CounterTake take, DateTime nowUtc) =>
        spent.Start == take.WindowStart
        && nowUtc - spent.Start < Window
        && spent.GivenBack < GivesBackPerWindow
        && spent.Count > 0;

    private static Spent Refunded(Spent spent) => spent with { Count = spent.Count - 1, GivenBack = spent.GivenBack + 1 };

    /// <summary>The window as it stands now: the recorded one while it lasts, else a fresh one with nothing spent or given back.</summary>
    private static Spent Current(Read read, DateTime nowUtc) =>
        read is Read.Recorded { Spent: var recorded } && nowUtc - recorded.Start < Window ? recorded : new Spent(nowUtc, 0);

    /// <summary>
    /// The new line from the START of the file, then the file cut to it — never a truncate first, so a kill
    /// mid-rewrite cannot leave the empty file a fresh window would be read from.
    /// </summary>
    private static void Rewrite(FileStream stream, Spent spent, string caller)
    {
        stream.Position = 0;
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true))
        {
            writer.Write($"{spent.Start.ToString("o", CultureInfo.InvariantCulture)}\t{spent.Count}\t{spent.GivenBack}\t{caller}\n");
        }

        stream.SetLength(stream.Position);
    }

    /// <summary>
    /// The memory key. The separator is a NUL because neither a path nor a caller id can contain one, so no
    /// pair of them can collide by concatenation — written as an ESCAPE: it was a raw NUL byte in this
    /// file, which is the same string and an invisible one. (SonarCloud, Critical, on the pull request.)
    /// </summary>
    private string KeyFor(string caller) => Path.GetFullPath(dataDir) + "\0" + caller;

    /// <summary>What the file last recorded for this caller — read-only, on the path where the write already failed.</summary>
    /// <remarks>
    /// Best effort and read-only: this runs precisely because the ordinary path failed, so it must not throw a
    /// second time. A file that is not there is a fresh window; one that is there and cannot be read or parsed
    /// is <see cref="Read.Unreadable"/> — a counter that forgets what is already spent is not a counter.
    /// </remarks>
    private Read Persisted(string caller)
    {
        try
        {
            return Parse(File.ReadAllText(FileFor(caller)), existed: true);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Read.Fresh();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Read.Unreadable(e.Message);
        }
    }

    /// <summary>
    /// The line as written — four fields, or the three of a file older than the give-back (none given back).
    /// Empty text is a fresh window only for a file this call created; an existing file that is empty or does
    /// not parse is <see cref="Read.Unreadable"/>.
    /// </summary>
    private static Read Parse(string content, bool existed)
    {
        var fields = content.Split('\t');
        return fields.Length >= 2
               && DateTime.TryParse(fields[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
               && int.TryParse(fields[1].Trim(), out var count)
            ? new Read.Recorded(new Spent(start.ToUniversalTime(), count, GivenBackIn(fields)))
            : Unparsed(content, existed);
    }

    private static Read Unparsed(string content, bool existed) =>
        !existed && content.Length == 0
            ? new Read.Fresh()
            : new Read.Unreadable(content.Length == 0 ? "the file is empty" : "the file does not parse");

    /// <summary>The third field of a four-field line; zero for the three-field shape, whose third field is the caller.</summary>
    private static int GivenBackIn(string[] fields) =>
        fields.Length >= 4 && int.TryParse(fields[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var given) ? given : 0;

    private string FileFor(string caller) =>
        Path.Combine(Dir, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(caller))) + ".txt");
}
