using CoaiMcp.Runners.Files;

namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// One sanitised snapshot: its root, its manifest, the path mapping D6 names, and the read-serving resolution S4's ACP
/// shim will go through. Disposal deletes it and gives its bytes back to the budget.
/// </summary>
/// <remarks>
/// <para><b>The manifest decides, not the disk.</b> <see cref="MapBack"/> maps an absolute path back to the repository
/// only when the manifest holds it; <see cref="Resolve"/> serves only a manifest entry, by the manifest's own spelling,
/// and refuses when any component on the way is a link; <see cref="Read"/> opens that spelling and asks the same
/// question again at the moment of the open. A reviewer's citation of a file that was excluded, or a model's request for
/// a path that was never copied, is answered with nothing rather than with whatever happens to be on the disk under that
/// name.</para>
/// <para><b>The root never changes.</b> A resumed Vibe session is bound to the folder it was created in (§22 of
/// research/RESULTS_vibe_confinement.md), so a conversation refreshes its snapshot IN PLACE through
/// <see cref="RefreshAsync"/>: the manifest and the hold are replaced, the path is not. A stateful class, therefore,
/// rather than a record — it owns a directory and a reservation for as long as it lives.</para>
/// <para><b>A refresh and a disposal never run together.</b> Both take the snapshot's turn: a disposal arriving while a
/// refresh copies waits for the refresh to land its swap (or fail it whole) and only then deletes the root — the review
/// of S1 found the opposite order deleting the root under the copy and disposing a semaphore the refresh still held. The
/// turn itself is never disposed, so a refresh asked after the disposal is refused rather than thrown at.</para>
/// </remarks>
public sealed class ReviewSnapshot : IDisposable
{
    private readonly ReviewSnapshots _maker;
    private readonly Action<string> _note;

    /// <summary>Held by a refresh for its whole length and by the disposal for its own, so neither sees the other half done.</summary>
    private readonly SemaphoreSlim _turn = new(1, 1);

    private SnapshotBudget.Hold _hold;
    private bool _disposed;

    internal ReviewSnapshot(ReviewSnapshots maker, string root, SnapshotManifest manifest, SnapshotBudget.Hold hold, Action<string> note)
    {
        _maker = maker;
        _note = note;
        _hold = hold;
        Root = root;
        Manifest = manifest;
    }

    /// <summary>The snapshot's directory — the CLI's working folder.</summary>
    public string Root { get; }

    /// <summary>A seam for the tests, run between the link walk and the open of a read — the window a swapped-in link must not win.</summary>
    internal Action BeforeOpen { get; set; } = static () => { };

    /// <summary>What the snapshot holds now; replaced whole by a refresh, never edited.</summary>
    public SnapshotManifest Manifest { get; private set; }

    internal long HeldBytes => _hold.Bytes;

    private StringComparison RootRule => Manifest.CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// The repository-relative path (forward slashes) an absolute path inside the snapshot names — or empty when it is
    /// outside the snapshot, is not absolute, or names nothing the manifest holds.
    /// </summary>
    public string MapBack(string absolutePath) => SnapshotPaths.Resolve(Root, RootRule, absolutePath, Manifest.Holds).Relative;

    /// <summary>
    /// The stored path of the manifest entry <paramref name="requestedPath"/> names, or a refusal: lexical normalisation,
    /// containment by path components, ordinal manifest matching, every form that is not a plain absolute path refused
    /// (<see cref="SnapshotPaths"/> lists them), and no link on the way from the root to the file.
    /// </summary>
    /// <remarks>
    /// The link check here is a check at ONE moment. A caller that will read the file must do so through
    /// <see cref="Read"/>, which asks again at the moment of the open; a path resolved here and opened later by the
    /// caller's own means has a window in which a link can be swapped in.
    /// </remarks>
    public SnapshotPath Resolve(string requestedPath)
    {
        var resolved = SnapshotPaths.Resolve(Root, RootRule, requestedPath, Manifest.Holds);
        if (resolved.Refusal.Length > 0)
        {
            return new SnapshotPath.Refused(resolved.Refusal);
        }

        var linked = SnapshotLinks.FirstLinkOnTheWay(Root, resolved.Relative);

        return linked.Length > 0 ? new SnapshotPath.Refused(LinkRefusal(linked)) : new SnapshotPath.Inside(Stored(resolved.Relative));
    }

    /// <summary>
    /// The bytes of the manifest entry <paramref name="requestedPath"/> names, read from the stored path — refused when
    /// anything between the root and the file is a link, or cannot be inspected, at the moment of the read.
    /// </summary>
    public SnapshotRead Read(string requestedPath)
    {
        var resolved = SnapshotPaths.Resolve(Root, RootRule, requestedPath, Manifest.Holds);
        if (resolved.Refusal.Length > 0)
        {
            return new SnapshotRead.Refused(resolved.Refusal);
        }

        var linked = SnapshotLinks.FirstLinkOnTheWay(Root, resolved.Relative);

        return linked.Length > 0 ? new SnapshotRead.Refused(LinkRefusal(linked)) : ReadStored(resolved.Relative);
    }

    /// <summary>
    /// The live tree copied again into this root (D6 as amended: one stable path per conversation, re-filled before
    /// each turn). On a refusal the previous copy and manifest stand, whole.
    /// </summary>
    public async Task<SnapshotRefresh> RefreshAsync(SnapshotSource.Live source, CancellationToken ct = default)
    {
        if (!_turn.Wait(0, ct))
        {
            return new SnapshotRefresh.Refused($"{Root} is busy: a refresh or a disposal is in progress");
        }

        try
        {
            if (_disposed)
            {
                return new SnapshotRefresh.Refused($"{Root} was disposed; a disposed snapshot is not refreshed");
            }

            if (!Directory.Exists(Root))
            {
                return new SnapshotRefresh.Refused($"{Root} is gone — swept, or deleted by hand; a new snapshot is needed");
            }

            return await _maker.RefreshAsync(this, source, ct);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>The swap's bookkeeping: the new manifest and hold in, the old hold given back.</summary>
    /// <remarks>
    /// Called under the turn the refresh holds, so the disposal cannot be running; the guard keeps the invariant
    /// explicit rather than assumed — a disposed snapshot takes nothing and lets nothing stand on disk.
    /// </remarks>
    internal void Replace(SnapshotManifest manifest, SnapshotBudget.Hold hold)
    {
        if (_disposed)
        {
            hold.Dispose();
            TryDeleteRoot();
            return;
        }

        var previous = _hold;
        Manifest = manifest;
        _hold = hold;
        previous.Dispose();
    }

    /// <summary>
    /// Waits for a refresh in flight, then deletes the snapshot — best effort, said when it fails — and gives its bytes
    /// back. Never throws. The wait is bounded by the refresh's own ceilings.
    /// </summary>
    public void Dispose()
    {
        _turn.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            TryDeleteRoot();
        }
        finally
        {
            _hold.Dispose();
            _turn.Release();
        }
    }

    private static string LinkRefusal(string linked) =>
        $"{linked} is a link, or cannot be inspected, and is not followed";

    private void TryDeleteRoot()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                GitScratch.DeleteEvenIfReadOnly(Root);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _note($"the snapshot {Root} could not be deleted ({e.Message}); the scratch sweep takes it with its directory");
        }
    }

    private string Stored(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// The read itself, by handle: opened, the handle's own name checked against the entry, the bytes read from it
    /// against <see cref="HandleRead.DefaultCap"/> — <see cref="HandleRead"/> says why a read by path was not enough.
    /// </summary>
    private SnapshotRead ReadStored(string relative)
    {
        BeforeOpen();

        return HandleRead.Read(Root, relative, HandleRead.DefaultCap, RootRule) switch
        {
            FileRead.Served served => new SnapshotRead.Served(served.Bytes),
            FileRead.Refused refused => new SnapshotRead.Refused($"{relative}: {refused.Reason}"),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }
}
