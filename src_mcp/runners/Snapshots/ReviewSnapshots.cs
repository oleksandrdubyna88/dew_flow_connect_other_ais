using System.ComponentModel;
using System.Diagnostics;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Snapshots;

/// <summary>
/// Makes sanitised snapshots: a fresh coai-owned copy of a tree, with every path the reviewed branch could use to
/// configure the CLI left out (D6 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Why a copy at all.</b> Vibe's reads are bounded to its working folder — M-V11 measured 3 of 3 <c>tool_denied</c>
/// for a <c>read_file</c> outside it (research/RESULTS_vibe_confinement.md §1) — so an empty working folder cannot
/// read the checkout, and the checkout itself carries whatever <c>.vibe/</c>, skills and <c>AGENTS.md</c> the reviewed
/// branch wrote. A copy that holds neither is the second independent block beside the CLI's own flags: M-V14 measured
/// 3 of 3 that a copy deliberately TRUSTED still fired no hook from the source tree or the copy's parent (§2).</para>
/// <para><b>Nothing partial is ever visible.</b> The copy is made into a sibling directory named <c>.partial</c> inside
/// the scratch directory and renamed over at the end — one rename within one directory — so a caller finds a whole
/// snapshot or none; a refusal (a ceiling, a link, a collision, a change under the copy after one retry, a long path)
/// deletes the partial directory and publishes nothing. A refresh in place (<see cref="ReviewSnapshot.RefreshAsync"/>)
/// builds the new tree the same way, then swaps it under the SAME root path by two renames.</para>
/// <para><b>One helper, several callers.</b> The round's launch directory, the consultant's launch and the question row
/// are S2/S3's seams; this class knows none of them. Which runtimes ask for a snapshot is the adapter's
/// <c>NeedsASanitisedSnapshot</c>, never a vendor name here.</para>
/// </remarks>
/// <param name="note">Where a non-fatal event is said — a retry, a directory that would not delete. <see cref="Quiet"/> says nothing.</param>
public sealed class ReviewSnapshots(IProcessLauncher launcher, SnapshotOptions options, SnapshotBudget budget, Action<string> note)
{
    /// <summary>How a snapshot directory is named inside its scratch directory.</summary>
    public const string Prefix = "snapshot-";

    private const string PartialSuffix = ".partial";
    private const string AsideSuffix = ".old-";

    private readonly SnapshotOptions _options = options;
    private readonly SnapshotBudget _budget = budget;
    private readonly SnapshotListing _listing = new(launcher);

    /// <summary>The product's defaults: the measured ceilings, the shared budget, and no note.</summary>
    public ReviewSnapshots(IProcessLauncher launcher)
        : this(launcher, SnapshotOptions.Default, SnapshotBudget.Shared, Quiet)
    {
    }

    /// <summary>A note sink that says nothing — the default, and what a test passes when it listens to nothing.</summary>
    public static void Quiet(string note)
    {
        // Deliberately nothing: the caller asked for no notes.
    }

    /// <summary>A snapshot of <paramref name="source"/> inside <paramref name="scratchDirectory"/>, or a refusal with a sentence.</summary>
    /// <remarks>
    /// The scratch directory is the caller's <c>coai-answers-*</c> directory, so the snapshot goes with it when the
    /// launch ends — and when a crashed launch leaves it, with the sweep that removes those directories after six hours.
    /// </remarks>
    public async Task<SnapshotOutcome> MakeAsync(SnapshotSource source, string scratchDirectory, CancellationToken ct = default)
    {
        var built = await BuildTwiceAsync(source, scratchDirectory, replacing: 0, ct);

        return built switch
        {
            Attempt.Built b => Publish(b, Path.Combine(scratchDirectory, Path.GetFileName(b.Directory)[..^PartialSuffix.Length])),
            Attempt.Refused r => new SnapshotOutcome.Refused($"the snapshot of {source.Directory} was refused: {r.Reason}"),
            _ => throw new InvalidOperationException("a changed attempt never leaves BuildTwiceAsync"),
        };
    }

    /// <summary>
    /// A snapshot of <paramref name="source"/> published AT <paramref name="root"/> — a path its owner fixed, the stuck
    /// consultant's <c>coai-vibe/&lt;id&gt;/tree</c> — or a refusal with a sentence.
    /// </summary>
    /// <remarks>
    /// <para>A conversation keeps ONE snapshot path across its turns (D6 as amended, §22 B/E: a resumed session is bound to
    /// the folder it was created in) — across a restart of the server too, when the object that refreshed it in place is
    /// gone and only the tree it left is there. So the copy is built COMPLETE beside the root, under every rule a snapshot
    /// has, and only then takes the root's place: by one rename when nothing is there, by the same two renames a refresh
    /// uses when a tree is (the old one aside, the new one in, the old one back if the second rename fails). A refusal
    /// leaves whatever was at the root exactly as it was.</para>
    /// </remarks>
    public async Task<SnapshotOutcome> MakeAtAsync(SnapshotSource source, string root, CancellationToken ct = default)
    {
        var built = await BuildTwiceAsync(source, Path.GetDirectoryName(root)!, replacing: 0, ct);

        return built switch
        {
            Attempt.Built b => Directory.Exists(root) || File.Exists(root) ? ReplaceAt(b, root) : Publish(b, root),
            Attempt.Refused r => new SnapshotOutcome.Refused($"the snapshot of {source.Directory} was refused: {r.Reason}"),
            _ => throw new InvalidOperationException("a changed attempt never leaves BuildTwiceAsync"),
        };
    }

    /// <summary>A tree left at <paramref name="root"/> replaced by the built one — or, refused, left where it was.</summary>
    private SnapshotOutcome ReplaceAt(Attempt.Built built, string root)
    {
        var aside = root + AsideSuffix + ShortId();
        if (!Move(root, aside, out var why) || !MovedIn(built.Directory, root, aside))
        {
            built.Hold.Dispose();
            TryDelete(built.Directory);

            return new SnapshotOutcome.Refused($"the snapshot could not take the place of what is at {root} ({why}); that is left as it was");
        }

        TryDelete(aside);

        return new SnapshotOutcome.Made(new ReviewSnapshot(this, root, built.Manifest, built.Hold, note));
    }

    /// <summary>The live tree copied again into <paramref name="snapshot"/>'s own root; the previous copy stands on a refusal.</summary>
    internal async Task<SnapshotRefresh> RefreshAsync(ReviewSnapshot snapshot, SnapshotSource.Live source, CancellationToken ct)
    {
        var built = await BuildTwiceAsync(source, Path.GetDirectoryName(snapshot.Root)!, snapshot.HeldBytes, ct);

        return built switch
        {
            Attempt.Built b => Swap(snapshot, b),
            Attempt.Refused r => new SnapshotRefresh.Refused($"the refresh of {snapshot.Root} from {source.Directory} was refused: {r.Reason}"),
            _ => throw new InvalidOperationException("a changed attempt never leaves BuildTwiceAsync"),
        };
    }

    /// <summary>One attempt's result: a whole partial directory with its manifest and hold, a change under the copy, or a refusal.</summary>
    private abstract record Attempt
    {
        private Attempt()
        {
        }

        public sealed record Built(string Directory, SnapshotManifest Manifest, SnapshotBudget.Hold Hold) : Attempt;

        /// <summary>A path moved under the copy — <c>changed</c>, <c>was added</c>, <c>was removed</c>, <c>appeared or went</c>.</summary>
        public sealed record Changed(string Path, string How) : Attempt;

        public sealed record Refused(string Reason) : Attempt;
    }

    /// <summary>The copy, retried once over a fresh listing when the tree moved under it — then refused: the tree is moving.</summary>
    private async Task<Attempt> BuildTwiceAsync(SnapshotSource source, string scratchDirectory, long replacing, CancellationToken ct)
    {
        var first = await BuildOnceAsync(source, scratchDirectory, 1, replacing, ct);
        if (first is not Attempt.Changed changed)
        {
            return first;
        }

        note($"{changed.Path} {changed.How} while {source.Directory} was being copied; copying it again");
        var second = await BuildOnceAsync(source, scratchDirectory, 2, replacing, ct);

        return second is Attempt.Changed again
            ? new Attempt.Refused($"{again.Path} {again.How} while the tree was being copied, on the retry as well; the tree is moving")
            : second;
    }

    /// <remarks>
    /// A value, never an exception (CodeRabbit on #785, 2026-10-12): a scratch directory that cannot be made, a destination
    /// the case probe cannot write, a <c>git</c> that cannot start — each is a refusal with its sentence, the partial copy
    /// deleted. Only what nobody expects (a cancellation among it) still leaves as an exception.
    /// </remarks>
    private async Task<Attempt> BuildOnceAsync(SnapshotSource source, string scratchDirectory, int attempt, long replacing, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var partial = Path.Combine(scratchDirectory, $"{Prefix}{ShortId()}{PartialSuffix}");
        try
        {
            Directory.CreateDirectory(scratchDirectory);
            Directory.CreateDirectory(partial);
            var made = await FillAsync(source, partial, attempt, replacing, clock, ct);

            return Kept(made, partial);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Win32Exception)
        {
            TryDelete(partial);

            return new Attempt.Refused($"the snapshot could not be made in {scratchDirectory}: {e.Message}");
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    /// <summary>The attempt as it is — the partial copy deleted unless it was built.</summary>
    private Attempt Kept(Attempt made, string partial)
    {
        if (made is not Attempt.Built)
        {
            TryDelete(partial);
        }

        return made;
    }

    private async Task<Attempt> FillAsync(SnapshotSource source, string partial, int attempt, long replacing, Stopwatch clock, CancellationToken ct)
    {
        var rule = DestinationCase.Probe(partial);
        var planned = await PlanAsync(source, partial, rule, ct);
        if (planned is Planning.Refused refused)
        {
            return new Attempt.Refused(refused.Reason);
        }

        if (clock.Elapsed > _options.MaxElapsed)
        {
            return new Attempt.Refused(SnapshotCopier.TookTooLong(clock.Elapsed, _options.MaxElapsed));
        }

        var plan = ((Planning.Planned)planned).Plan;
        var reserving = _budget.Reserve(plan.Bytes, Path.GetDirectoryName(partial)!, replacing);
        if (reserving is SnapshotBudget.Reserving.Refused noRoom)
        {
            return new Attempt.Refused(noRoom.Reason.Trim());
        }

        return await CopyAsync(source, plan, partial, ((SnapshotBudget.Reserving.Held)reserving).Hold, attempt, rule, clock, ct);
    }

    private async Task<Planning> PlanAsync(SnapshotSource source, string partial, StringComparison rule, CancellationToken ct) =>
        await _listing.ListAsync(source, ct) switch
        {
            Listing.Failed failed => new Planning.Refused(failed.Reason),
            Listing.Listed listed => SnapshotPlanner.Plan(source.Directory, partial, listed.Entries, listed.Unavailable, _options, source.IncludesUntracked, rule),
            _ => throw new InvalidOperationException("the union is closed"),
        };

    /// <summary>
    /// The copy under its hold, and — for a tree that can move — the whole inventory checked again after the last file:
    /// the hold goes with the attempt on success and is given back on anything else.
    /// </summary>
    private async Task<Attempt> CopyAsync(
        SnapshotSource source, CopyPlan plan, string partial, SnapshotBudget.Hold hold, int attempt, StringComparison rule, Stopwatch clock, CancellationToken ct)
    {
        try
        {
            var copied = SnapshotCopier.CopyAll(plan, _options, attempt, clock, ct);
            if (copied is Copying.Done done && source.MovesUnderTheCopy)
            {
                copied = await SettledAsync(source, plan, done.Stamps, partial, rule, clock, ct);
            }

            return Attempted(copied, plan, partial, rule, hold);
        }
        catch
        {
            hold.Dispose();
            throw;
        }
    }

    /// <summary>What the copy came to, as an attempt: built, with the hold; or changed or refused, the hold given back.</summary>
    private static Attempt Attempted(Copying copied, CopyPlan plan, string partial, StringComparison rule, SnapshotBudget.Hold hold) => copied switch
    {
        Copying.Done => new Attempt.Built(partial, Manifest(plan, rule), hold),
        Copying.Changed changed => Released(hold, new Attempt.Changed(changed.Path, changed.How)),
        Copying.Refused refused => Released(hold, new Attempt.Refused(refused.Reason)),
        _ => throw new InvalidOperationException("the union is closed"),
    };

    /// <summary>
    /// The tree listed and planned AGAIN after the last file, compared whole with what was copied (D6 as amended: per-file
    /// stamps alone can publish a mix of times). A plan that refuses now — a link that appeared, a ceiling the grown tree
    /// passes — is a refusal; a tree that moved anywhere is the retry's case.
    /// </summary>
    private async Task<Copying> SettledAsync(
        SnapshotSource source, CopyPlan copied, IReadOnlyDictionary<string, SnapshotStamp> stamps, string partial, StringComparison rule, Stopwatch clock, CancellationToken ct)
    {
        var again = await PlanAsync(source, partial, rule, ct);
        if (clock.Elapsed > _options.MaxElapsed)
        {
            return new Copying.Refused(SnapshotCopier.TookTooLong(clock.Elapsed, _options.MaxElapsed));
        }

        return again switch
        {
            Planning.Refused refused => new Copying.Refused(refused.Reason),
            Planning.Planned planned => SnapshotCopier.Settled(copied, stamps, planned.Plan),
            _ => throw new InvalidOperationException("the union is closed"),
        };
    }

    private static Attempt Released(SnapshotBudget.Hold hold, Attempt attempt)
    {
        hold.Dispose();

        return attempt;
    }

    private static SnapshotManifest Manifest(CopyPlan plan, StringComparison rule) =>
        new([.. plan.Files.Select(file => file.Relative)], plan.Excluded, plan.Unavailable, plan.Bytes, rule == StringComparison.OrdinalIgnoreCase);

    /// <summary>The one rename that makes a snapshot visible — or a refusal that leaves nothing.</summary>
    private SnapshotOutcome Publish(Attempt.Built built, string final)
    {
        if (!Move(built.Directory, final, out var why))
        {
            built.Hold.Dispose();
            TryDelete(built.Directory);

            return new SnapshotOutcome.Refused($"the snapshot could not be published at {final}: {why}");
        }

        return new SnapshotOutcome.Made(new ReviewSnapshot(this, final, built.Manifest, built.Hold, note));
    }

    /// <summary>
    /// The new tree under the old root's path: the old one aside, the new one in — and the old one back if the second
    /// rename fails. On Windows a directory cannot be renamed onto an existing one, so two renames it is.
    /// </summary>
    private SnapshotRefresh Swap(ReviewSnapshot snapshot, Attempt.Built built)
    {
        var aside = snapshot.Root + AsideSuffix + ShortId();
        if (!Move(snapshot.Root, aside, out var why) || !MovedIn(built.Directory, snapshot.Root, aside))
        {
            built.Hold.Dispose();
            TryDelete(built.Directory);

            return new SnapshotRefresh.Refused($"the refreshed copy could not take the place of {snapshot.Root} ({why}); the previous snapshot stands");
        }

        snapshot.Replace(built.Manifest, built.Hold);
        TryDelete(aside);

        return new SnapshotRefresh.Refreshed(built.Manifest);
    }

    /// <summary>The new tree moved in, or the old one restored — and said out loud when even that fails.</summary>
    private bool MovedIn(string fresh, string root, string aside)
    {
        if (Move(fresh, root, out _))
        {
            return true;
        }

        if (!Move(aside, root, out var why))
        {
            note($"the previous snapshot could not be moved back to {root}; it is at {aside} ({why})");
        }

        return false;
    }

    private static bool Move(string from, string to, out string why)
    {
        try
        {
            Directory.Move(from, to);
            why = string.Empty;

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            why = e.Message;

            return false;
        }
    }

    private void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                GitScratch.DeleteEvenIfReadOnly(directory);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            note($"{directory} could not be deleted ({e.Message}); the scratch sweep takes it");
        }
    }

    /// <summary>Eight hex characters, as the round trees are named: unique enough, short enough to read.</summary>
    private static string ShortId() => Guid.NewGuid().ToString("N")[..8];
}
