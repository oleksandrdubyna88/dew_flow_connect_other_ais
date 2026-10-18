using System.Globalization;
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

    /// <summary>What the place skipped, in the order it met it — a folder it could not list, a candidate git did not confirm; empty when nothing was.</summary>
    public virtual IReadOnlyList<string> Notes => [];

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
    public sealed class Owned(string folder, string workdir, IReadOnlyList<string> roots, IReadOnlyList<IDisposable> held, IReadOnlyList<string> notes) : ConsultantPlace
    {
        private int _released;

        public override string Folder => folder;

        public override string Workdir => workdir;

        public override IReadOnlyList<string> Roots => roots;

        public override IReadOnlyList<string> Notes => notes;

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

    /// <summary>The walk's listing — a test's seam for an unreadable folder and for a budget that runs out during the walk.</summary>
    internal Func<string, FolderListing> Listing { get; set; } = FolderListings.Read;

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
            var stamping = new Stamping(turned.Folder, store, turned.Ceiling);

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
    /// is expanded into the repositories beneath it (D1–D7 of <c>todo/PLAN_vibe_reads_a_folder_of_repositories.md</c>), each
    /// copied through the same method at <c>tree/&lt;grant index&gt;/&lt;relative path&gt;/snapshot-&lt;id&gt;</c>.</para>
    /// <para>The copies are made one after another, before the launch, so they are bounded TOGETHER by <paramref name="budget"/> —
    /// the row's own (the #811 checkpoint round: ten roots near D6's 60 s ceiling each would spend ten minutes before Vibe
    /// started). Past it the place is refused naming how many copy targets were copied of how many were reached, and every
    /// copy made is disposed; the caller's own cancellation still leaves as one.</para>
    /// </remarks>
    public async Task<ConsultantPlace> OneShotAsync(string folder, IReadOnlyList<string> sources, TimeSpan budget, CancellationToken ct)
    {
        var tree = Path.Combine(folder, ConversationSnapshots.TreeName);
        var tally = new Tally();
        using var copying = CancellationTokenSource.CreateLinkedTokenSource(ct);
        copying.CancelAfter(budget);
        try
        {
            var refused = await CopiedAsync(sources, tree, tally, copying.Token);

            return refused.Length == 0
                ? new ConsultantPlace.Owned(folder, tree, [.. tally.Made.Select(snapshot => snapshot.Root)], [.. tally.Made], [.. tally.Notes])
                : Unmade(tally, refused);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unmade(tally, OutOfBudget(tally.Made.Count, tally.Found, budget));
        }
        catch
        {
            Unmade(tally, string.Empty);
            throw;
        }
    }

    /// <summary>What one one-shot place has done so far: its copies, its notes, how many copy targets it has reached, and which repositories it holds.</summary>
    private sealed class Tally
    {
        public List<ReviewSnapshot> Made { get; } = [];

        public List<string> Notes { get; } = [];

        /// <summary>Copy targets known so far: +1 when a work-tree root is reached, +n when an expanded root's candidates are CONFIRMED (not yet copied).</summary>
        public int Found { get; set; }

        /// <summary>The repositories already copied, by full path — directly granted work trees included (D6): the first in source order wins.</summary>
        public HashSet<string> Copied { get; } = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <summary>Each source taken in turn into <paramref name="tally"/> — or the first refusal, or the budget's, when it ran out.</summary>
    private async Task<string> CopiedAsync(IReadOnlyList<string> sources, string tree, Tally tally, CancellationToken copying)
    {
        for (var index = 0; index < sources.Count; index++)
        {
            if (await SourceAsync(index, sources[index], tree, tally, copying) is { Length: > 0 } refused)
            {
                return refused;
            }
        }

        return string.Empty;
    }

    private async Task<string> SourceAsync(int index, string source, string tree, Tally tally, CancellationToken copying)
    {
        if (!Directory.Exists(source))
        {
            return $"the folder {source} does not exist";
        }

        return await IsAWorkTreeAsync(source, copying)
            ? await DirectAsync(source, tree, tally, copying)
            : await ExpandedAsync(index, source, tree, tally, copying);
    }

    /// <summary>A source that is a work tree: copied as before, at <c>tree/snapshot-&lt;id&gt;</c> — unless an earlier grant already copied it.</summary>
    private async Task<string> DirectAsync(string source, string tree, Tally tally, CancellationToken copying)
    {
        var key = Keyed(source);
        if (tally.Copied.Contains(key))
        {
            return string.Empty;
        }

        tally.Found++;

        return Recorded(await CopyOfAsync(source, tree, string.Empty, copying), key, tally, copying);
    }

    /// <summary>
    /// A source that is not a work tree: the repositories beneath it (D2), the ones git confirms validated BEFORE the first is
    /// copied — so the budget's "of" is the real total and the bound counts confirmed repositories (D3, D4). A candidate git
    /// does not confirm is skipped with a note; a copy that is refused refuses the row — nothing here catches it.
    /// </summary>
    private async Task<string> ExpandedAsync(int index, string root, string tree, Tally tally, CancellationToken copying)
    {
        if (OwnedPaths.IsLinkIfThere(root))
        {
            return LinkSentence(root, root);
        }

        return RepositoryWalk.Find(Path.GetFullPath(root), Listing, copying) switch
        {
            WalkOutcome.Refused refused => refused.Reason,
            WalkOutcome.Found found => await ExpandedFoundAsync(index, root, tree, found, tally, copying),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    /// <summary>The walk's candidates: its notes said, each validated, the bound and the "none" sentence applied, then every confirmed one copied.</summary>
    private async Task<string> ExpandedFoundAsync(int index, string root, string tree, WalkOutcome.Found walked, Tally tally, CancellationToken copying)
    {
        foreach (var skipped in walked.Notes)
        {
            Say(tally, skipped);
        }

        var confirmed = await ConfirmedAsync(walked.Repositories, tally, copying);
        if (tally.Found > RepositoryWalk.MaxRepositories)
        {
            return $"the folder {root} holds more than {RepositoryWalk.MaxRepositories} Git repositories within two levels, more than a Vibe disk row copies — grant the project folders themselves";
        }

        return confirmed.Valid == 0
            ? $"the folder {root} holds no Git repository within two levels — Vibe reads copies coai makes through Git"
            : await CopiedExpandedAsync(index, root, tree, confirmed.ToCopy, tally, copying);
    }

    /// <summary>The candidates that are work trees and not yet copied, and how many of all were valid (a repository already copied is valid, and not copied again).</summary>
    private sealed record Confirmed(IReadOnlyList<FoundRepository> ToCopy, int Valid);

    /// <summary>Discovery's side of D4: each candidate asked of git, in order, until the bound is passed. One broken checkout never costs the others.</summary>
    private async Task<Confirmed> ConfirmedAsync(IReadOnlyList<FoundRepository> candidates, Tally tally, CancellationToken copying)
    {
        List<FoundRepository> toCopy = [];
        var valid = 0;
        foreach (var candidate in candidates)
        {
            copying.ThrowIfCancellationRequested();
            var already = tally.Copied.Contains(Keyed(candidate.Path));
            var confirms = already || await IsAWorkTreeAsync(candidate.Path, copying);

            // A confirmation cut short by the budget comes back as "not a work tree": the budget is what ended it, and says so.
            copying.ThrowIfCancellationRequested();
            if (!confirms)
            {
                Say(tally, $"{candidate.Path} was skipped: it holds a .git, but git does not confirm it as a work tree");
                continue;
            }

            valid++;
            if (!already)
            {
                toCopy.Add(candidate);
                tally.Found++;
            }

            if (tally.Found > RepositoryWalk.MaxRepositories)
            {
                break;
            }
        }

        return new Confirmed(toCopy, valid);
    }

    /// <summary>Copy's side of D4: every confirmed repository through <see cref="CopyOfAsync"/>; the first refusal refuses the row.</summary>
    private async Task<string> CopiedExpandedAsync(int index, string root, string tree, IReadOnlyList<FoundRepository> repositories, Tally tally, CancellationToken copying)
    {
        foreach (var repository in repositories)
        {
            BeforeCopyOf(repository.Path);
            var destination = Path.Combine([tree, index.ToString(CultureInfo.InvariantCulture), .. repository.Segments]);
            var refused = Recorded(await CopyOfAsync(repository.Path, destination, root, copying), Keyed(repository.Path), tally, copying);
            if (refused.Length > 0)
            {
                return refused;
            }
        }

        return string.Empty;
    }

    /// <summary>A copy that was made is kept, whatever follows; a copy cut short by the budget comes back as a refusal of its own, and one that finished past it is past it.</summary>
    private static string Recorded(SnapshotOutcome outcome, string key, Tally tally, CancellationToken copying)
    {
        if (outcome is SnapshotOutcome.Made copy)
        {
            tally.Made.Add(copy.Snapshot);
            tally.Copied.Add(key);
        }

        copying.ThrowIfCancellationRequested();

        return outcome is SnapshotOutcome.Refused refused ? refused.Reason : string.Empty;
    }

    /// <summary>A skip, said to the log's sink and kept on the place for the row.</summary>
    private void Say(Tally tally, string text)
    {
        tally.Notes.Add(text);
        note(text);
    }

    private static string Keyed(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Every copy made disposed, and the refusal the place carries — with what it had skipped.</summary>
    private static ConsultantPlace.Refused Unmade(Tally tally, string reason)
    {
        tally.Made.ForEach(snapshot => snapshot.Dispose());

        return new ConsultantPlace.Refused(reason, [.. tally.Notes]);
    }

    private static string OutOfBudget(int copied, int of, TimeSpan budget) =>
        $"copying the granted folders took longer than the row's budget ({budget.TotalSeconds:0} s): {copied} of {of} copied — "
        + "grant fewer or smaller folders, or give the row more time (COAI_QCONSULT_ROW_MINUTES)";

    /// <summary>
    /// A copy of one repository — the live tree as git lists it, limited to that folder — or the refusal of a folder git has no
    /// work tree for: the copy lists through git (D6), and a plain folder has no listing to trust until a mode for one exists.
    /// </summary>
    /// <param name="linkRoot">
    /// The granted root the repository was found beneath, or empty for a work-tree root granted directly: the links are
    /// checked from the repository up to and including it (D5) — or, for a direct root, up to its work tree's top.
    /// </param>
    /// <remarks>
    /// A granted root reached through a LINK is refused too (P1 of the S3 plan round): the root itself, or any folder above
    /// it up to its work tree's top — a copy through one would be of whatever it points at. Folders above the work tree are
    /// the machine's own layout (a relocated profile; macOS's <c>/var</c>) and are not asked. Inside the root, a link git
    /// lists is refused by the snapshot itself. The git check is repeated here for a found repository: it closes the window
    /// between the walk and the copy.
    /// </remarks>
    private async Task<SnapshotOutcome> CopyOfAsync(string source, string tree, string linkRoot, CancellationToken ct) =>
        !await IsAWorkTreeAsync(source, ct) ? new SnapshotOutcome.Refused(NotAWorkTree(source))
        : LinkAbove(source, linkRoot) is { Length: > 0 } link ? new SnapshotOutcome.Refused(LinkSentence(link, linkRoot.Length == 0 ? source : linkRoot))
        : await Maker.MakeAsync(new SnapshotSource.Live(source), tree, ct);

    private static string LinkSentence(string link, string granted) =>
        $"{link} is a link, and the granted folder {granted} is reached through it — grant the folder it points at instead";

    private static string NotAWorkTree(string source) =>
        $"the folder {source} is not inside a Git work tree — this runtime reads a copy coai makes through Git (D6 of todo/PLAN_vibe_and_kimi_runtimes.md)";

    private static string LinkAbove(string source, string linkRoot) =>
        linkRoot.Length == 0 ? LinkUpToTheWorkTree(source) : LinkUpTo(source, linkRoot);

    /// <summary>The first folder from <paramref name="repository"/> up to and INCLUDING the granted <paramref name="root"/> that is a link, or empty (D5).</summary>
    private static string LinkUpTo(string repository, string root) =>
        UpTo(Path.GetFullPath(repository), Keyed(root)).FirstOrDefault(OwnedPaths.IsLinkIfThere) ?? string.Empty;

    private static IEnumerable<string> UpTo(string folder, string top)
    {
        for (var at = folder; at is not null; at = Path.GetDirectoryName(at))
        {
            yield return at;
            if (string.Equals(Keyed(at), top, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                yield break;
            }
        }
    }

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

    /// <summary>Whether git calls <paramref name="folder"/> inside a work tree. A question git did not answer (the budget cut it short) is "no".</summary>
    private async Task<bool> IsAWorkTreeAsync(string folder, CancellationToken ct)
    {
        var asked = await launcher.RunAsync(
            new ProcessRequest("git", ["rev-parse", "--is-inside-work-tree"], folder) { Timeout = TimeSpan.FromSeconds(30) }, ct);

        return asked.ExitCode == 0 && asked.StdOut.Trim() == "true";
    }
}
