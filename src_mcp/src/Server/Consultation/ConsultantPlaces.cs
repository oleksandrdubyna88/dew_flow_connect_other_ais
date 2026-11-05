using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Snapshots;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Server;

/// <summary>
/// Where a consultant that must not stand in the checkout stands for one launch: a coai-owned folder for its home and a
/// sanitised copy of the tree it reads (D6, D17 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>) — or the checkout, unchanged,
/// for every adapter that does not ask (<see cref="IConsultantRuntime.NeedsASanitisedSnapshot"/>).
/// </summary>
/// <remarks>
/// A class rather than a record: what it holds — the conversation folder's turn, a question row's copies — is given back
/// exactly once, when the launch it was taken for has been read.
/// </remarks>
public abstract class ConsultantPlace : IDisposable
{
    private ConsultantPlace()
    {
    }

    /// <summary>The coai-owned folder the adapter keeps its home in (<c>ReviewerSettings.OwnedScratch</c>); empty in the checkout.</summary>
    public virtual string Folder => string.Empty;

    /// <summary>The copy the launch stands in (<c>ConsultantLaunch.Workdir</c>); empty in the checkout.</summary>
    public virtual string Workdir => string.Empty;

    /// <summary>The absolute roots of each copy, in the order of their sources — what a question row's prompt names.</summary>
    public virtual IReadOnlyList<string> Roots => [];

    /// <summary>Why there is no place — empty when there is one.</summary>
    public virtual string Refusal => string.Empty;

    /// <summary>What the place skipped, in the order it met it — a folder it could not list, a candidate git did not confirm, the linked worktrees it left out; empty when nothing was.</summary>
    public virtual IReadOnlyList<string> Notes => [];

    /// <summary>What the copies do NOT hold, as a sentence the model must read too (a prompt that names the copies says it) — a subset of <see cref="Notes"/>; empty when the copies are all there is.</summary>
    public virtual IReadOnlyList<string> Limitations => [];

    /// <summary>The checkout itself: every adapter that asks for no copy, exactly as before.</summary>
    public static ConsultantPlace Checkout { get; } = new InTheCheckout();

    public void Dispose()
    {
        Release();
        GC.SuppressFinalize(this);
    }

    protected virtual void Release()
    {
        // The checkout holds nothing.
    }

    private sealed class InTheCheckout : ConsultantPlace;

    /// <summary>No place, and the sentence the launch's refusal carries — with what the place had skipped before it refused.</summary>
    public sealed class Refused(string reason, IReadOnlyList<string> notes) : ConsultantPlace
    {
        public Refused(string reason)
            : this(reason, [])
        {
        }

        public override string Refusal => reason;

        public override IReadOnlyList<string> Notes => notes;
    }

    /// <summary>A coai-owned folder and copies in it, held until the launch has been read.</summary>
    public sealed class Owned(string folder, string workdir, IReadOnlyList<string> roots, IReadOnlyList<IDisposable> held, IReadOnlyList<string> notes, IReadOnlyList<string> limitations) : ConsultantPlace
    {
        private int _released;

        public Owned(string folder, string workdir, IReadOnlyList<string> roots, IReadOnlyList<IDisposable> held, IReadOnlyList<string> notes)
            : this(folder, workdir, roots, held, notes, [])
        {
        }

        public override string Folder => folder;

        public override string Workdir => workdir;

        public override IReadOnlyList<string> Roots => roots;

        public override IReadOnlyList<string> Notes => notes;

        public override IReadOnlyList<string> Limitations => limitations;

        protected override void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) == 1)
            {
                return;
            }

            foreach (var one in held)
            {
                one.Dispose();
            }
        }
    }
}

/// <summary>
/// Makes <see cref="ConsultantPlace"/>s: the stuck consultant's, which a CONVERSATION keeps across its turns, and a one-shot
/// launch's (a question row, a consultant check), which goes with its scratch folder.
/// </summary>
/// <param name="homesRoot">
/// Where the conversations' folders live — the person's local application data (<c>PanelSettings.ConsultantHomesRoot</c>);
/// empty when none is configured, and a conversation on a runtime that needs one is then refused by name.
/// </param>
/// <param name="store">This server's store as an owner (<see cref="ConversationFolder.StoreOf"/>), stamped on every conversation folder it makes.</param>
/// <param name="clock">The clock the one-shot budget and the folder stamp read; the system clock by default, a manual one in a test.</param>
public sealed class ConsultantPlaces(string homesRoot, string store, IProcessLauncher launcher, Action<string> note, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>How long a turn waits for its folder's cross-process lock — only the home sweep holds it otherwise, and briefly.</summary>
    public static readonly TimeSpan LockWait = TimeSpan.FromSeconds(10);

    /// <summary>How the folder's turn lock is waited for — off the calling thread, at most <see cref="LockWait"/>; a test's seam for a cancellation there.</summary>
    internal Func<string, CancellationToken, Task<Hold>> TakingTheLock { get; set; } =
        static (path, ct) => Task.Run(() => HeldFile.TakeWithin(path, LockWait), ct);

    /// <summary>The cure a refused place carries: the copy is coai's, so the cause is in the tree or this machine.</summary>
    public const string Cure =
        "this consultant reads a copy coai makes of the live tree (D6 of todo/PLAN_vibe_and_kimi_runtimes.md) and the copy was refused — "
        + "the sentence says why (a link, a ceiling, a tree that kept changing, a full disk); fix that, or consult on another runtime";

    /// <summary>What makes every copy — the product's ceilings and the shared budget; a test's seam for a slow copy.</summary>
    internal ReviewSnapshots Maker { get; set; } = new(launcher, SnapshotOptions.Default, SnapshotBudget.Shared, note);

    /// <summary>The walk's listing — a test's seam for an unreadable folder and for a budget that runs out during the walk.</summary>
    internal Func<string, FolderListing> Listing { get; set; } = FolderListings.Read;

    /// <summary>The walk's look for a <c>.git</c> in a folder of the deepest level — it never lists that folder; a test's seam.</summary>
    internal Func<string, FolderListing> Probing { get; set; } = FolderListings.GitEntryOf;

    /// <summary>Called with a repository's path right before its link chain is checked — a test's seam for a folder that becomes a link between the walk and the copy.</summary>
    internal Action<string> BeforeCopyOf { get; set; } = static _ => { };

    /// <summary>
    /// The place one turn of conversation <paramref name="consultationId"/> stands in: its folder under
    /// <c>&lt;homes root&gt;/&lt;runtime's homes&gt;/&lt;id&gt;</c>, held by its turn, and the ONE snapshot of the live tree at
    /// <c>tree</c> inside it, refreshed in place (D6) — or the checkout for an adapter that asks for no copy.
    /// </summary>
    /// <param name="ceiling">
    /// How long this turn may run at most — the consult turn's own deadline (<c>ConsultationDeadline.For</c> of its launch's
    /// timeout). Stamped on the folder when the copy is ready, so the home sweep never takes a home a killed server's child
    /// may still be running in (R4).
    /// </param>
    /// <remarks>
    /// A home and a copy are two declarations (the epic-3 code round): a runtime that names a
    /// <see cref="IConsultantRuntime.HomesFolder"/> is given its owned, stamped, locked conversation folder whether or not it
    /// asks for a copy — the copy is optional inside that place — and one that asks for a copy needs a homes folder to keep
    /// it in. Only a runtime that declares neither stands in the checkout with nothing held.
    /// </remarks>
    public async Task<ConsultantPlace> ForConversationAsync(IConsultantRuntime runtime, string consultationId, string repo, TimeSpan ceiling, CancellationToken ct)
    {
        if (KeepsNothing(runtime))
        {
            return ConsultantPlace.Checkout;
        }

        return Unconfigured(runtime) is { Length: > 0 } why
            ? new ConsultantPlace.Refused(why)
            : await OwnedAsync(runtime, consultationId, repo, ceiling, ct);
    }

    /// <summary>A runtime that declares neither a home nor a copy: it stands in the checkout, as every consultant before Vibe.</summary>
    private static bool KeepsNothing(IConsultantRuntime runtime) =>
        !runtime.NeedsASanitisedSnapshot && runtime.HomesFolder.Length == 0;

    /// <summary>The conversation's own folder — never through a link — held, stamped and, when asked, holding the copy.</summary>
    private async Task<ConsultantPlace> OwnedAsync(IConsultantRuntime runtime, string consultationId, string repo, TimeSpan ceiling, CancellationToken ct)
    {
        // The homes folder under the root is entered through the one type that makes it private or refuses it (D7).
        var opened = PrivateFolder.Open(homesRoot, runtime.HomesFolder, note);
        if (opened is PrivateFolderOutcome.Refused refused)
        {
            return new ConsultantPlace.Refused(refused.Sentence);
        }

        var pin = ((PrivateFolderOutcome.Opened)opened).Held;

        // The pin is the homes folder's handle: given back on EVERY way out that does not move it into a place — a link, a throw
        // while the folder is made, a cancellation while the folder's turn is waited for.
        try
        {
            var homes = PrivateFolder.PathOf(homesRoot, runtime.HomesFolder);
            var folder = ConsultantHomes.ConversationFolder(homesRoot, runtime.HomesFolder, consultationId);
            if (MadeThrough(homes, folder) is { Length: > 0 } link)
            {
                pin.Dispose();

                return new ConsultantPlace.Refused($"{link} is a link, and a conversation's folder is never made or used through one");
            }

            return await HeldAsync(new Turned(folder, runtime.NeedsASanitisedSnapshot ? repo : string.Empty, ceiling, pin), ct);
        }
        catch
        {
            pin.Dispose();
            throw;
        }
    }

    /// <summary>One turn's folder, the live tree to copy into it (empty: no copy), the turn's ceiling, and the pinned handle of the homes folder above it.</summary>
    private sealed record Turned(string Folder, string CopyOf, TimeSpan Ceiling, IDisposable Pin);

    /// <summary>Why a runtime that keeps a conversation folder has none here — no homes root, or no homes folder named — or empty.</summary>
    private string Unconfigured(IConsultantRuntime runtime) =>
        homesRoot.Length == 0 || runtime.HomesFolder.Length == 0
            ? $"{runtime.Vendor} keeps its conversation in a coai-owned folder, and this server has none configured for it (COAI_CONSULTANT_HOMES)"
            : string.Empty;

    /// <summary>The first of the homes folder and the conversation's folder that is a link — before and after it is made (R2).</summary>
    private static string MadeThrough(string homes, string folder)
    {
        if (OwnedPaths.FirstLink(homes, folder) is { Length: > 0 } before)
        {
            return before;
        }

        Directory.CreateDirectory(folder);

        return OwnedPaths.FirstLink(homes, folder);
    }

    /// <summary>
    /// The folder held — this process's turn, then the cross-process lock — and, when asked, its snapshot made fresh in it.
    /// The in-process turn is given back on EVERY way out but one: a place that took it (the epic-3 code round found a
    /// cancellation while the lock was awaited leaving it held until a restart).
    /// </summary>
    private async Task<ConsultantPlace> HeldAsync(Turned turned, CancellationToken ct)
    {
        var turn = await VibeHomeTurns.TakeAsync(turned.Folder, ct);
        var moved = false;
        try
        {
            var place = await LockedAsync(turned, turn, ct);
            moved = place is ConsultantPlace.Owned;

            return place;
        }
        finally
        {
            if (!moved)
            {
                turn.Dispose();
                turned.Pin.Dispose();
            }
        }
    }

    /// <summary>Under this process's turn: the folder's cross-process lock, then the place — the lock given back unless the place holds it.</summary>
    private async Task<ConsultantPlace> LockedAsync(Turned turned, IDisposable turn, CancellationToken ct)
    {
        if (await TakingTheLock(Path.Combine(turned.Folder, ConversationFolder.LockName), ct) is not Hold.Taken taken)
        {
            return new ConsultantPlace.Refused($"{turned.Folder} is held by another process (its turn lock), so this turn is not started — try again in a moment");
        }

        try
        {
            var stamping = new Stamping(turned.Folder, store, turned.Ceiling, _clock);

            return turned.CopyOf.Length == 0
                ? HomeOnly(turned.Folder, stamping, [taken.Stream, turn, turned.Pin])
                : Placed(await ConversationSnapshots.FreshAsync(Maker, turned.Folder, turned.CopyOf, ct), stamping, [taken.Stream, turn, turned.Pin]);
        }
        catch
        {
            taken.Stream.Dispose();
            throw;
        }
    }

    /// <summary>The conversation's home and nothing to stand in: the launch keeps its own cwd, the folder is owned and held.</summary>
    private static ConsultantPlace HomeOnly(string folder, Stamping stamping, IReadOnlyList<IDisposable> held)
    {
        stamping.Begin();

        return new ConsultantPlace.Owned(folder, string.Empty, [], [stamping, .. held], []);
    }

    /// <summary>The stamp a conversation's folder carries for a turn: begun when the copy is ready, ended when it is given back.</summary>
    private sealed class Stamping(string folder, string store, TimeSpan ceiling, TimeProvider clock) : IDisposable
    {
        public void Begin() => ConversationFolder.Begin(folder, store, clock.GetUtcNow().UtcDateTime, ceiling);

        public void Dispose() => ConversationFolder.End(folder);
    }

    /// <summary>The conversation's place, holding its folder's locks and its stamp — or, refused, nothing held.</summary>
    private static ConsultantPlace Placed(SnapshotOutcome fresh, Stamping stamping, IReadOnlyList<IDisposable> held)
    {
        if (fresh is SnapshotOutcome.Made made)
        {
            stamping.Begin();

            // The stamp is ended FIRST, while the locks still hold, so a sweep never sees a free folder with a running stamp.
            return new ConsultantPlace.Owned(Path.GetDirectoryName(made.Snapshot.Root)!, made.Snapshot.Root, [made.Snapshot.Root], [stamping, .. held], []);
        }

        foreach (var one in held)
        {
            one.Dispose();
        }

        return new ConsultantPlace.Refused(((SnapshotOutcome.Refused)fresh).Reason);
    }

    /// <summary>
    /// A ONE-SHOT place inside <paramref name="folder"/> (the launch's own scratch, removed by its owner): a copy of each of
    /// <paramref name="sources"/> — the live tree of each, through git — each in a <c>snapshot-&lt;id&gt;</c> folder of its own
    /// under <c>tree</c>, the launch standing in <c>tree</c> itself. Disposing it deletes the copies and gives their bytes back; any refusal deletes what was made.
    /// </summary>
    /// <remarks>
    /// <para>A source that is a Git work tree is copied as it is. A source that is NOT one — a parent folder of repositories —
    /// is expanded into the repositories beneath it (D1–D7, D9 of <c>research/PLAN_vibe_reads_a_folder_of_repositories.md</c>,
    /// <see cref="ParentExpansion"/>), each copied through the same method at <c>tree/&lt;grant index&gt;/&lt;relative path&gt;/snapshot-&lt;id&gt;</c>.</para>
    /// <para>The order is: every granted folder asked of git once and counted; every repository beneath every parent confirmed; the
    /// linked worktrees whose main checkout is copied left out; the bound judged over what is left — and only then the copies, made one
    /// after another before the launch, so they are bounded TOGETHER by <paramref name="budget"/> — the row's own (the #811 checkpoint
    /// round: ten roots near D6's 60 s ceiling each would spend ten minutes before Vibe started). Past it the place is refused naming how
    /// many copy targets were copied of how many were reached, and every copy made is disposed; the caller's own cancellation still
    /// leaves as one.</para>
    /// </remarks>
    public async Task<ConsultantPlace> OneShotAsync(string folder, IReadOnlyList<string> sources, TimeSpan budget, CancellationToken ct)
    {
        var tree = Path.Combine(folder, ConversationSnapshots.TreeName);
        var tally = new OneShotTally(note);

        // The budget's timer is its own source on the clock (declared first), the link second: the link is disposed first, so a timer
        // that fires between the two disposals meets a disposed link. A zero budget is out synchronously — before anything is asked of git.
        using var budgetTimer = new CancellationTokenSource(budget, _clock);
        using var copying = CancellationTokenSource.CreateLinkedTokenSource(ct, budgetTimer.Token);
        try
        {
            var refused = await CopiedAsync(sources, tree, tally, copying.Token);

            return refused.Length == 0
                ? new ConsultantPlace.Owned(folder, tree, [.. tally.Made.Select(snapshot => snapshot.Root)], [.. tally.Made], [.. tally.Notes], [.. tally.Limitations])
                : Unmade(tally, refused);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unmade(tally, OutOfBudget(tally, budget));
        }
        catch
        {
            Unmade(tally, string.Empty);
            throw;
        }
    }

    /// <summary>The granted folders, the parents expanded, then every copy in the order of the grants — or the first refusal, or the budget's.</summary>
    private async Task<string> CopiedAsync(IReadOnlyList<string> sources, string tree, OneShotTally tally, CancellationToken copying)
    {
        var granted = await GrantedAsync(sources, tally, copying);
        if (granted.Refusal.Length > 0)
        {
            return granted.Refusal;
        }

        var copier = new RepositoryCopier(Maker, launcher);
        var expansion = new ParentExpansion(launcher, copier, Listing, Probing, BeforeCopyOf);
        var expanded = await expansion.PrepareAsync(granted.Folders, tally, copying);
        if (expanded.Refusal.Length > 0)
        {
            return expanded.Refusal;
        }

        tally.Copying = true;
        foreach (var grant in granted.Folders)
        {
            var refused = grant.IsWorkTree
                ? await DirectAsync(grant, tree, copier, tally, copying)
                : await expansion.CopyAsync(grant, expanded.ToCopy[grant.Index], tree, tally, copying);
            if (refused.Length > 0)
            {
                return refused;
            }
        }

        return string.Empty;
    }

    /// <summary>The row's granted folders as the place first meets them, or the refusal of one that is not there — or of a row that already holds too many.</summary>
    private sealed record Granted(string Refusal, IReadOnlyList<GrantedFolder> Folders);

    /// <summary>
    /// Each source asked of git ONCE, before anything is copied: a work tree granted directly is counted at once (D3 — the bound is the row's,
    /// whatever its place in the list) and, when it is the top of a work tree that is not linked, remembered as a main checkout (D9). A
    /// folder that does not exist stays a refusal.
    /// </summary>
    private async Task<Granted> GrantedAsync(IReadOnlyList<string> sources, OneShotTally tally, CancellationToken copying)
    {
        List<GrantedFolder> folders = [];
        foreach (var (index, source) in sources.Index())
        {
            var root = GrantedPaths.Keyed(source);
            if (!Directory.Exists(root))
            {
                return new Granted($"the folder {root} does not exist", []);
            }

            folders.Add(await GrantedAsync(index, root, tally, copying));
        }

        return tally.Found > RepositoryWalk.MaxRepositories ? new Granted(RepositoryWalk.TooManyRepositories, []) : new Granted(string.Empty, folders);
    }

    private async Task<GrantedFolder> GrantedAsync(int index, string root, OneShotTally tally, CancellationToken copying)
    {
        var facts = await RepositoryFacts.AskAsync(launcher, root, copying);

        // A question cut short by the budget comes back as "not a work tree": the budget is what ended it, and says so.
        copying.ThrowIfCancellationRequested();
        if (!facts.InsideWorkTree)
        {
            return new GrantedFolder(index, root, facts, Duplicate: false);
        }

        var first = tally.Planned.Add(root);
        if (first)
        {
            tally.Found++;
        }

        if (first && facts.IsMain)
        {
            tally.Mains.Add(facts.CommonDir);
        }

        return new GrantedFolder(index, root, facts, Duplicate: !first);
    }

    /// <summary>A source that is a work tree: copied as before, at <c>tree/snapshot-&lt;id&gt;</c> — unless an earlier grant already names it.</summary>
    private static async Task<string> DirectAsync(GrantedFolder grant, string tree, RepositoryCopier copier, OneShotTally tally, CancellationToken copying) =>
        grant.Duplicate ? string.Empty : tally.Record(await copier.CopyAsync(grant.Root, tree, string.Empty, copying), copying);

    /// <summary>Every copy made disposed, and the refusal the place carries — with what it had skipped.</summary>
    private static ConsultantPlace.Refused Unmade(OneShotTally tally, string reason)
    {
        tally.Made.ForEach(snapshot => snapshot.Dispose());

        return new ConsultantPlace.Refused(reason, [.. tally.Notes]);
    }

    /// <summary>The budget's refusal: what had been done when it ran out — copies made, or (before the first copy) the phase it ended — and the one cure.</summary>
    private static string OutOfBudget(OneShotTally tally, TimeSpan budget) =>
        $"copying the granted folders took longer than the row's budget ({budget.TotalSeconds:0} s): {Progress(tally)} — "
        + "grant fewer or smaller folders, or give the row more time (COAI_QCONSULT_ROW_MINUTES)";

    /// <summary>"0 of 0 copied" reads as "nothing found", so before the first copy the sentence says what was going on instead.</summary>
    private static string Progress(OneShotTally tally) =>
        tally.Copying ? $"{tally.Made.Count} of {tally.Found} copied"
        : tally.Candidates == 0 ? "the budget ran out while reading the granted folders, before anything was copied"
        : $"the budget ran out while confirming {tally.Candidates} {(tally.Candidates == 1 ? "candidate" : "candidates")} with git, before anything was copied";
}
