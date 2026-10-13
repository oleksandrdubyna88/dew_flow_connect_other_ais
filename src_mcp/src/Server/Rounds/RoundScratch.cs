using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Snapshots;

namespace CoaiMcp.Server;

/// <summary>
/// A round's own scratch folder, deleted when the round ends: the sanitised snapshot of its tree for a runtime that asks
/// for one (D6 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>) and the coai-owned homes its Vibe reviewers run on (D17).
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

    private RoundScratch(string directory, RoundTree tree, Action<string> note)
    {
        Directory = directory;
        Tree = tree;
        _note = note;
    }

    /// <summary>No scratch at all: every launch keeps today's directories, and a runtime that needs a snapshot is left out.</summary>
    public static RoundScratch None { get; } = new(string.Empty, new RoundTree.NotMade(), static _ => { });

    /// <summary>The folder's path — empty for <see cref="None"/>.</summary>
    public string Directory { get; }

    /// <summary>The round's snapshot, its refusal, or that none was asked for.</summary>
    public RoundTree Tree { get; }

    /// <summary>A path for a new round's scratch, not made yet.</summary>
    public static RoundScratch Reserve(Action<string> note) =>
        new(Path.Combine(Path.GetTempPath(), $"coai-answers-{Guid.NewGuid().ToString("N")[..12]}"), new RoundTree.NotMade(), note);

    /// <summary>This scratch with the round's snapshot outcome.</summary>
    public RoundScratch With(SnapshotOutcome outcome) => new(Directory, outcome switch
    {
        SnapshotOutcome.Made made => new RoundTree.Made(made.Snapshot),
        SnapshotOutcome.Refused refused => new RoundTree.Refused(refused.Reason),
        _ => throw new InvalidOperationException("the union is closed"),
    }, _note);

    /// <summary>The snapshot disposed (its bytes given back), then the whole folder deleted — best effort, said when it fails.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1 || Directory.Length == 0)
        {
            return;
        }

        if (Tree is RoundTree.Made made)
        {
            made.Snapshot.Dispose();
        }

        Delete();
    }

    private void Delete()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                GitScratch.DeleteEvenIfReadOnly(Directory);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _note($"the round's scratch folder {Directory} could not be deleted ({e.Message}); the scratch sweep takes it");
        }
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
