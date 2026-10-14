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

    /// <summary>No place, and the sentence the launch's refusal carries.</summary>
    public sealed class Refused(string reason) : ConsultantPlace
    {
        public override string Refusal => reason;
    }

    /// <summary>A coai-owned folder and copies in it, held until the launch has been read.</summary>
    public sealed class Owned(string folder, string workdir, IReadOnlyList<string> roots, IReadOnlyList<IDisposable> held) : ConsultantPlace
    {
        private int _released;

        public override string Folder => folder;

        public override string Workdir => workdir;

        public override IReadOnlyList<string> Roots => roots;

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
public sealed class ConsultantPlaces(string homesRoot, string store, IProcessLauncher launcher, Action<string> note)
{
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
        var homes = Path.Combine(homesRoot, runtime.HomesFolder);
        var folder = ConsultantHomes.ConversationFolder(homesRoot, runtime.HomesFolder, consultationId);

        return MadeThrough(homes, folder) is { Length: > 0 } link
            ? new ConsultantPlace.Refused($"{link} is a link, and a conversation's folder is never made or used through one")
            : await HeldAsync(new Turned(folder, runtime.NeedsASanitisedSnapshot ? repo : string.Empty, ceiling), ct);
    }

    /// <summary>One turn's folder, the live tree to copy into it (empty: no copy), and the turn's ceiling.</summary>
    private sealed record Turned(string Folder, string CopyOf, TimeSpan Ceiling);

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
            var stamping = new Stamping(turned.Folder, store, turned.Ceiling);

            return turned.CopyOf.Length == 0
                ? HomeOnly(turned.Folder, stamping, [taken.Stream, turn])
                : Placed(await ConversationSnapshots.FreshAsync(Maker, turned.Folder, turned.CopyOf, ct), stamping, [taken.Stream, turn]);
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

        return new ConsultantPlace.Owned(folder, string.Empty, [], [stamping, .. held]);
    }

    /// <summary>The stamp a conversation's folder carries for a turn: begun when the copy is ready, ended when it is given back.</summary>
    private sealed class Stamping(string folder, string store, TimeSpan ceiling) : IDisposable
    {
        public void Begin() => ConversationFolder.Begin(folder, store, DateTime.UtcNow, ceiling);

        public void Dispose() => ConversationFolder.End(folder);
    }

    /// <summary>The conversation's place, holding its folder's locks and its stamp — or, refused, nothing held.</summary>
    private static ConsultantPlace Placed(SnapshotOutcome fresh, Stamping stamping, IReadOnlyList<IDisposable> held)
    {
        if (fresh is SnapshotOutcome.Made made)
        {
            stamping.Begin();

            // The stamp is ended FIRST, while the locks still hold, so a sweep never sees a free folder with a running stamp.
            return new ConsultantPlace.Owned(Path.GetDirectoryName(made.Snapshot.Root)!, made.Snapshot.Root, [made.Snapshot.Root], [stamping, .. held]);
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
    /// The copies are made one after another, before the launch, so they are bounded TOGETHER by <paramref name="budget"/> —
    /// the row's own (the #811 checkpoint round: ten roots near D6's 60 s ceiling each would spend ten minutes before Vibe
    /// started). Past it the place is refused naming how many roots were copied of how many, and every copy made is disposed;
    /// the caller's own cancellation still leaves as one.
    /// </remarks>
    public async Task<ConsultantPlace> OneShotAsync(string folder, IReadOnlyList<string> sources, TimeSpan budget, CancellationToken ct)
    {
        var tree = Path.Combine(folder, ConversationSnapshots.TreeName);
        List<ReviewSnapshot> made = [];
        using var copying = CancellationTokenSource.CreateLinkedTokenSource(ct);
        copying.CancelAfter(budget);
        try
        {
            var refused = await CopiedAsync(sources, tree, made, copying.Token);

            return refused.Length == 0 ? new ConsultantPlace.Owned(folder, tree, [.. made.Select(snapshot => snapshot.Root)], [.. made]) : Unmade(made, refused);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unmade(made, OutOfBudget(made.Count, sources.Count, budget));
        }
        catch
        {
            Unmade(made, string.Empty);
            throw;
        }
    }

    /// <summary>Each source copied in turn into <paramref name="made"/> — or the first refusal, or the budget's, when it ran out.</summary>
    private async Task<string> CopiedAsync(IReadOnlyList<string> sources, string tree, List<ReviewSnapshot> made, CancellationToken copying)
    {
        foreach (var source in sources)
        {
            var outcome = await CopyOfAsync(source, tree, copying);
            if (outcome is SnapshotOutcome.Made copy)
            {
                made.Add(copy.Snapshot);
            }

            // A copy cut short by the budget comes back as a refusal of its own (a git listing "did not answer in time"),
            // and one that finished past it is past it: the budget is what ended them, and the sentence says so.
            copying.ThrowIfCancellationRequested();
            if (outcome is SnapshotOutcome.Refused refused)
            {
                return refused.Reason;
            }
        }

        return string.Empty;
    }

    /// <summary>Every copy made disposed, and the refusal the place carries.</summary>
    private static ConsultantPlace.Refused Unmade(List<ReviewSnapshot> made, string reason)
    {
        made.ForEach(snapshot => snapshot.Dispose());

        return new ConsultantPlace.Refused(reason);
    }

    private static string OutOfBudget(int copied, int of, TimeSpan budget) =>
        $"copying the granted folders took longer than the row's budget ({budget.TotalSeconds:0} s): {copied} of {of} copied — "
        + "grant fewer or smaller folders, or give the row more time (COAI_QCONSULT_ROW_MINUTES)";

    /// <summary>
    /// A copy of one source — the live tree as git lists it, limited to that folder — or the refusal of a folder git has no
    /// work tree for: the copy lists through git (D6), and a plain folder has no listing to trust until a mode for one exists.
    /// </summary>
    /// <remarks>
    /// A granted root reached through a LINK is refused too (P1 of the S3 plan round): the root itself, or any folder above
    /// it up to its work tree's top — a copy through one would be of whatever it points at. Folders above the work tree are
    /// the machine's own layout (a relocated profile; macOS's <c>/var</c>) and are not asked. Inside the root, a link git
    /// lists is refused by the snapshot itself.
    /// </remarks>
    private async Task<SnapshotOutcome> CopyOfAsync(string source, string tree, CancellationToken ct) =>
        await OutsideAGitTreeAsync(source, ct) is { Length: > 0 } why ? new SnapshotOutcome.Refused(why)
        : LinkUpToTheWorkTree(source) is { Length: > 0 } link
            ? new SnapshotOutcome.Refused($"{link} is a link, and the granted folder {source} is reached through it — grant the folder it points at instead")
            : await Maker.MakeAsync(new SnapshotSource.Live(source), tree, ct);

    /// <summary>The first folder from <paramref name="source"/> up to its work tree's top that is a link, or empty.</summary>
    private static string LinkUpToTheWorkTree(string source) =>
        UpToTheWorkTree(Path.GetFullPath(source)).FirstOrDefault(OwnedPaths.IsLinkIfThere) ?? string.Empty;

    /// <summary>The folder and each one above it, up to and including the first that holds a <c>.git</c>.</summary>
    private static IEnumerable<string> UpToTheWorkTree(string folder)
    {
        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
        {
            yield return at;
            if (HoldsGit(at))
            {
                yield break;
            }
        }
    }

    private static bool HoldsGit(string folder) =>
        Directory.Exists(Path.Combine(folder, ".git")) || File.Exists(Path.Combine(folder, ".git"));

    private async Task<string> OutsideAGitTreeAsync(string source, CancellationToken ct)
    {
        if (!Directory.Exists(source))
        {
            return $"the folder {source} does not exist";
        }

        var asked = await launcher.RunAsync(
            new ProcessRequest("git", ["rev-parse", "--is-inside-work-tree"], source) { Timeout = TimeSpan.FromSeconds(30) }, ct);

        return asked.ExitCode == 0 && asked.StdOut.Trim() == "true"
            ? string.Empty
            : $"the folder {source} is not inside a Git work tree — this runtime reads a copy coai makes through Git (D6 of "
              + "todo/PLAN_vibe_and_kimi_runtimes.md), and a plain folder cannot be copied that way yet; grant a folder of a repository";
    }
}
