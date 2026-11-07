using CoaiMcp.Tests.Leftovers;

namespace CoaiMcp.Tests;

/// <summary>
/// Everything <see cref="RunTempFolder"/> touches outside itself, as functions — so a test hands it a private root and a dictionary instead of the
/// process's real environment, and the order of the steps can be recorded.
/// </summary>
/// <param name="SetVariable">Sets an environment variable; a <c>null</c> value removes it.</param>
/// <param name="GetVariable">Reads one; <c>null</c> when it is not set (that is the environment's own answer).</param>
public sealed record RunTempSeams(Action<string, string?> SetVariable, Func<string, string?> GetVariable)
{
    /// <summary>Called once <see cref="OwnedDir.Create"/> returned — folder AND claim exist — and before any variable moves: the place the process-exit cleanup is registered.</summary>
    public Action<RunTempFolder> RegisterRestore { get; init; } = static _ => { };

    /// <summary>Pre-clear before the helper's removal: read-only files and folders go too, a link by its own name. A failure is swallowed — the helper's removal then warns.</summary>
    public Action<string> Delete { get; init; } = Runners.Files.GitScratch.DeleteEvenIfReadOnly;

    /// <summary>The names to draw (default: <see cref="Owned.NameFor"/> of <c>run</c>). A seam so a test can force a collision.</summary>
    public Func<string>? NextName { get; init; }

    /// <summary>Called with the claim path after the claim was created exclusively and before it is written (a seam for a failing write).</summary>
    public Action<string>? BeforeClaimWrite { get; init; }

    /// <summary>Called with the CLAIM path after the claim is written and flushed and before the folder is made — the hook of S2-B's claim-first race test.</summary>
    public Action<string>? AfterClaim { get; init; }

    /// <summary>The layout requirement (default <see cref="DarwinLayout.Require"/>; on macOS no folder is made on an identity read that is not trusted).</summary>
    public Action? RequireLayout { get; init; }

    /// <summary>Where a warning of the helper is reported: a squatted name, a folder that could not be removed.</summary>
    public Action<string> Warn { get; init; } = Console.Error.WriteLine;

    /// <summary>
    /// Held while the run folder is claimed: the inherited <c>TESTSTODELETE_RUN</c> is withheld, so the claim names THIS process (E5 S2-B, Opus review 1). The default withholds nothing (a test
    /// that hands <see cref="RunTempFolder"/> a dictionary must not move the real environment); <see cref="RunTemp"/> hands the real one.
    /// </summary>
    public Func<IDisposable> WithholdInheritedRun { get; init; } = static () => NothingWithheld.Instance;

    private sealed class NothingWithheld : IDisposable
    {
        internal static readonly NothingWithheld Instance = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// One test run's temp folder, and the three variables pointed at it: <see cref="Create"/> makes it, <see cref="Restore"/> takes it away again
/// (<c>todo/PLAN_test_leftovers_E5.md</c>, S3).
/// </summary>
/// <remarks>
/// <para>The folder is an <see cref="OwnedDir"/>, <c>testsToDelete-&lt;owner&gt;-coai-run-&lt;id&gt;</c>: the claim FIRST (an exclusive create, in the contract's grammar),
/// then the folder, made exclusively — a taken name is never adopted. The ORDER is the contract and is tested: claim, folder (one <see cref="OwnedDir.Create"/> call), the
/// restore registered, the variables move.</para>
/// <para><see cref="Restore"/> is idempotent and never throws: it runs from the assembly fixture's disposal AND from the process-exit handler, and whichever comes first
/// does the work. A folder that cannot be removed keeps its claim, with one warning, for the sweep to judge by its owner.</para>
/// </remarks>
public sealed class RunTempFolder
{
    private readonly RunTempSeams _seams;
    private readonly IReadOnlyDictionary<string, string?> _before;
    private int _restored;

    private RunTempFolder(OwnedDir? dir, RunTempSeams seams, IReadOnlyDictionary<string, string?> before)
    {
        Dir = dir;
        Folder = dir?.Path ?? string.Empty;
        _seams = seams;
        _before = before;
    }

    /// <summary>
    /// What <see cref="RunTemp"/> holds before anything was made, and after a folder could not be made: already restored, so ending a run that has no folder does nothing.
    /// </summary>
    public static RunTempFolder None { get; } = new(null, new RunTempSeams(static (_, _) => { }, static _ => null), new Dictionary<string, string?>()) { _restored = 1 };

    /// <summary>The three variables <c>Path.GetTempPath()</c> and a child's temp path are read from, on every operating system.</summary>
    public static IReadOnlyList<string> Variables { get; } = ["TEMP", "TMP", "TMPDIR"];

    /// <summary>The folder, inside the root it was made in.</summary>
    public string Folder { get; }

    /// <summary>The owner claim: a sibling FILE, never inside the folder.</summary>
    public string ClaimFile => Folder + ".owner";

    /// <summary>The helper's own folder object, for a test that must dispose it again once a removal that could not run now can.</summary>
    internal OwnedDir? Dir { get; }

    /// <summary>
    /// Records the three variables, makes the claimed folder inside <paramref name="realRoot"/> in ONE <see cref="OwnedDir.Create"/> call (claim first, then the exclusive
    /// <c>mkdir</c>; a taken claim or a squatted folder draws another name; any failure removes this run's own claim and throws <see cref="InvalidOperationException"/>),
    /// registers the restore, and points the variables at the folder — in that order.
    /// </summary>
    public static RunTempFolder Create(string realRoot, RunTempSeams seams)
    {
        var before = Variables.ToDictionary(name => name, seams.GetVariable);
        // The run folder belongs to ONE process: a record inherited in TESTSTODELETE_RUN (a wrapper's, which may die while this suite runs) is for the CHILDREN this run starts, not for the run's own folder.
        OwnedDir dir;
        using (seams.WithholdInheritedRun())
        {
            dir = OwnedDir.Create("run", realRoot, seams.Warn, seams.NextName, seams.BeforeClaimWrite, seams.AfterClaim, seams.RequireLayout);
        }

        var run = new RunTempFolder(dir, seams, before);

        // After Create returns, never between the claim and the folder: a restore registered earlier could, after a redraw past a squatter,
        // remove by a name this run never made.
        seams.RegisterRestore(run);
        foreach (var name in Variables)
        {
            seams.SetVariable(name, run.Folder);
        }

        return run;
    }

    /// <summary>
    /// Puts the old values back (a variable that was absent is removed), pre-clears read-only entries, then lets the helper remove the folder (five tries) and, LAST, the claim.
    /// A folder that stays keeps its claim, with one warning. Idempotent; never throws.
    /// </summary>
    public void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            return;
        }

        foreach (var (name, value) in _before)
        {
            _seams.SetVariable(name, value);
        }

        try
        {
            if (Directory.Exists(Folder))
            {
                _seams.Delete(Folder);
            }
        }
        catch (Exception)
        {
            // Read-only files, a held handle: the helper's removal below tries again and says so when it still cannot.
        }

        Dir?.Dispose();
    }
}
