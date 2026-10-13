using CoaiMcp.Runners.Consultation;
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
public sealed class ConsultantPlaces(string homesRoot, IProcessLauncher launcher, Action<string> note)
{
    /// <summary>The cure a refused place carries: the copy is coai's, so the cause is in the tree or this machine.</summary>
    public const string Cure =
        "this consultant reads a copy coai makes of the live tree (D6 of todo/PLAN_vibe_and_kimi_runtimes.md) and the copy was refused — "
        + "the sentence says why (a link, a ceiling, a tree that kept changing, a full disk); fix that, or consult on another runtime";

    private readonly ReviewSnapshots _maker = new(launcher, SnapshotOptions.Default, SnapshotBudget.Shared, note);

    /// <summary>
    /// The place one turn of conversation <paramref name="consultationId"/> stands in: its folder under
    /// <c>&lt;homes root&gt;/&lt;runtime's homes&gt;/&lt;id&gt;</c>, held by its turn, and the ONE snapshot of the live tree at
    /// <c>tree</c> inside it, refreshed in place (D6) — or the checkout for an adapter that asks for no copy.
    /// </summary>
    public async Task<ConsultantPlace> ForConversationAsync(IConsultantRuntime runtime, string consultationId, string repo, CancellationToken ct)
    {
        if (!runtime.NeedsASanitisedSnapshot)
        {
            return ConsultantPlace.Checkout;
        }

        if (homesRoot.Length == 0 || runtime.HomesFolder.Length == 0)
        {
            return new ConsultantPlace.Refused(
                $"{runtime.Vendor} keeps its conversation in a coai-owned folder, and this server has none configured for it (COAI_CONSULTANT_HOMES)");
        }

        var folder = ConsultantHomes.ConversationFolder(homesRoot, runtime.HomesFolder, consultationId);
        var turn = await VibeHomeTurns.TakeAsync(folder, ct);
        try
        {
            return Placed(await ConversationSnapshots.FreshAsync(_maker, folder, repo, ct), folder, turn);
        }
        catch
        {
            turn.Dispose();
            throw;
        }
    }

    /// <summary>The conversation's place, holding its folder's turn — or, refused, nothing held.</summary>
    private static ConsultantPlace Placed(SnapshotOutcome fresh, string folder, IDisposable turn)
    {
        if (fresh is SnapshotOutcome.Made made)
        {
            return new ConsultantPlace.Owned(folder, made.Snapshot.Root, [made.Snapshot.Root], [turn]);
        }

        turn.Dispose();

        return new ConsultantPlace.Refused(((SnapshotOutcome.Refused)fresh).Reason);
    }

    /// <summary>
    /// A ONE-SHOT place inside <paramref name="folder"/> (the launch's own scratch, removed by its owner): a copy of each of
    /// <paramref name="sources"/> — the live tree of each, through git — under <c>tree</c>, the launch standing in
    /// <c>tree</c> itself. Disposing it deletes the copies and gives their bytes back; any refusal deletes what was made.
    /// </summary>
    public async Task<ConsultantPlace> OneShotAsync(string folder, IReadOnlyList<string> sources, CancellationToken ct)
    {
        var tree = Path.Combine(folder, ConversationSnapshots.TreeName);
        List<ReviewSnapshot> made = [];
        try
        {
            foreach (var source in sources)
            {
                var outcome = await CopyOfAsync(source, tree, ct);
                if (outcome is SnapshotOutcome.Refused refused)
                {
                    made.ForEach(snapshot => snapshot.Dispose());

                    return new ConsultantPlace.Refused(refused.Reason);
                }

                made.Add(((SnapshotOutcome.Made)outcome).Snapshot);
            }
        }
        catch
        {
            made.ForEach(snapshot => snapshot.Dispose());
            throw;
        }

        return new ConsultantPlace.Owned(folder, tree, [.. made.Select(snapshot => snapshot.Root)], [.. made]);
    }

    /// <summary>
    /// A copy of one source — the live tree as git lists it, limited to that folder — or the refusal of a folder git has no
    /// work tree for: the copy lists through git (D6), and a plain folder has no listing to trust until a mode for one exists.
    /// </summary>
    private async Task<SnapshotOutcome> CopyOfAsync(string source, string tree, CancellationToken ct) =>
        await OutsideAGitTreeAsync(source, ct) is { Length: > 0 } why
            ? new SnapshotOutcome.Refused(why)
            : await _maker.MakeAsync(new SnapshotSource.Live(source), tree, ct);

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
