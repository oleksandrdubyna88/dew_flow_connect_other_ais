using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Snapshots;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Server;

/// <summary>
/// A round's own scratch folder, deleted when the round ends: the sanitised snapshot of its tree for a runtime that asks
/// for one (D6 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>) and the coai-owned homes its Vibe (D17) and Kimi (D19) reviewers run on.
/// </summary>
/// <remarks>
/// <para><b>A <c>coai-answers-*</c> folder under the temp directory</b>, so a crash leaves nothing the six-hour scratch
/// sweep does not already remove (<c>PanelService.PruneOldScratchDirs</c>, the prefixes in <c>shared/temp-sweep.json</c>).
/// Its PATH is chosen when the round prepares and the folder is made only when something is put in it — a round with no
/// runtime that asks pays nothing.</para>
/// <para><b>One snapshot per round, shared</b> by every reviewer of a runtime that asks (D6: "one per round per pinned
/// SHA"). It is made once, before the roster is built, because making it is asynchronous and the roster is not. A refusal
/// is kept rather than thrown: the rows that need it are left out of the round with its sentence, the others run.</para>
/// <para>Owned by the round: <c>RoundEngine</c> disposes it with the round, whatever ended it.</para>
/// </remarks>
internal sealed class RoundScratch : IDisposable
{
    private readonly Action<string> _note;
    private int _disposed;

    private RoundScratch(string directory, RoundTree tree, Action<string> note, OneShotReservation homes)
    {
        Directory = directory;
        Tree = tree;
        _note = note;
        Homes = homes;
    }

    /// <summary>No scratch at all: every launch keeps today's directories, and a runtime that needs a snapshot is left out.</summary>
    public static RoundScratch None { get; } = new(string.Empty, new RoundTree.NotMade(), static _ => { }, OneShotReservation.None.Instance);

    /// <summary>
    /// The round's reservation under <c>&lt;homes root&gt;/coai-vh</c> (D1): its folder holds the reviewers' Vibe homes and their
    /// exports, short whatever TEMP is. <see cref="OneShotReservation.None"/> when no row keeps a home or no root is configured
    /// (settings built by hand); <see cref="OneShotReservation.Refused"/> leaves the home-keeping rows out of the round with its sentence.
    /// </summary>
    public OneShotReservation Homes { get; }

    /// <summary>
    /// What a home-keeping reviewer is handed as <c>OwnedScratch</c>: the reserved one-shot folder, or — with none — this scratch
    /// folder itself, made so the home has a parent (the way homes lived before the reservation).
    /// </summary>
    public string HomeFolder => Homes is OneShotReservation.Reserved reserved ? reserved.Home.Folder : Directory;

    /// <summary>
    /// <see cref="HomeFolder"/>, made when it is the round's own scratch folder (no reservation): the launch control makes a home
    /// inside a folder that exists and never the chain above it (D7), so a home-keeping reviewer's parent is made here, once.
    /// </summary>
    public string MadeHomeFolder()
    {
        if (Homes is not OneShotReservation.Reserved && Directory.Length > 0)
        {
            System.IO.Directory.CreateDirectory(Directory);
        }

        return HomeFolder;
    }

    /// <summary>The sentence the home-keeping rows are refused with, or empty.</summary>
    public string HomesRefusal => Homes is OneShotReservation.Refused refused ? refused.Sentence : string.Empty;

    /// <summary>The folder's path — empty for <see cref="None"/>.</summary>
    public string Directory { get; }

    /// <summary>The round's snapshot, its refusal, or that none was asked for.</summary>
    public RoundTree Tree { get; }

    /// <summary>
    /// A path for a new round's scratch, not made yet — and, when <paramref name="keepsAHome"/> (a runnable row's runtime writes a
    /// session into a home), its one-shot folder under <paramref name="homesRoot"/>, made now and locked until <see cref="Dispose"/>.
    /// </summary>
    public static RoundScratch Reserve(Action<string> note, string homesRoot = "", bool keepsAHome = false) =>
        new(Path.Combine(Path.GetTempPath(), $"coai-answers-{Guid.NewGuid().ToString("N")[..12]}"), new RoundTree.NotMade(), note,
            keepsAHome ? VibeHomes.ReserveOneShot(homesRoot, note) : OneShotReservation.None.Instance);

    /// <summary>A scratch at a path somebody chose (a test's own folder, never the system temp), not made yet — the same object <see cref="Reserve"/> makes.</summary>
    internal static RoundScratch At(string directory, Action<string> note) => new(directory, new RoundTree.NotMade(), note);

    /// <summary>This scratch with the round's snapshot outcome.</summary>
    public RoundScratch With(SnapshotOutcome outcome) => new(Directory, outcome switch
    {
        SnapshotOutcome.Made made => new RoundTree.Made(made.Snapshot),
        SnapshotOutcome.Refused refused => new RoundTree.Refused(refused.Reason),
        _ => throw new InvalidOperationException("the union is closed"),
    }, _note, Homes);

    /// <summary>The snapshot disposed (its bytes given back), then the whole folder deleted — best effort, said when it fails.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (Directory.Length == 0)
        {
            return;
        }

        if (Tree is RoundTree.Made made)
        {
            made.Snapshot.Dispose();
        }

        Delete();
        if (Homes is OneShotReservation.Reserved reserved)
        {
            reserved.Home.Dispose();
        }
    }

    private void Delete()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory) && KimiHomesDisposed())
            {
                GitScratch.DeleteEvenIfReadOnly(Directory);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _note($"the round's scratch folder {Directory} could not be deleted ({e.Message}); the scratch sweep takes it");
        }
    }

    /// <summary>
    /// D19's order for the Kimi homes this scratch KNOWS it holds (E4.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>, S4.15): every
    /// <c>kh-*</c> home has its <c>credentials</c> junction removed by its own name, the person's folder verified, under no live tree — BEFORE the
    /// shared deleter, which then meets no link. A stop is noted and the scratch is KEPT whole (the sweep's rule, <c>ConsultantHomeSweep</c>):
    /// deleting "the rest" would unlink the junction under a Kimi that is still running — its next refresh would write the rotated token into a
    /// folder it re-creates inside coai's home and sign the person out. The six-hour scratch sweep (<c>PanelService.PruneOldScratchDirs</c>) finishes it once
    /// the tree has ended — and it asks the same per-home hold before it deletes anything (E4.4 review B1), so it never takes the folder from under a live Kimi either.
    /// </summary>
    private bool KimiHomesDisposed()
    {
        var stopped = false;
        KimiHome.DisposeAllUnder(Directory, said =>
        {
            stopped = true;
            _note($"the round's scratch {Directory} is kept: {said}");
        });

        return !stopped;
    }
}

/// <summary>What a round has for a runtime that must not start in the checkout itself.</summary>
internal abstract record RoundTree
{
    private RoundTree()
    {
    }

    /// <summary>Nothing was asked for — no runtime of this round needs a snapshot, or the stage reads no checkout.</summary>
    public sealed record NotMade : RoundTree;

    public sealed record Made(ReviewSnapshot Snapshot) : RoundTree;

    /// <summary>Asked for and refused; the rows that need it are left out with this sentence.</summary>
    public sealed record Refused(string Reason) : RoundTree;
}

/// <summary>Where one runtime's review launch starts, and how its citations come back — or why it does not start at all.</summary>
internal abstract record LaunchPlace
{
    private LaunchPlace()
    {
    }

    public sealed record At(string Directory, Runners.Reviewers.ICitedPaths Citations) : LaunchPlace;

    public sealed record Refused(string Reason) : LaunchPlace;
}
