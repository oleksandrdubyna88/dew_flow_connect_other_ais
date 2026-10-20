namespace CoaiMcp.Tests;

/// <summary>
/// Everything <see cref="RunTempFolder"/> touches outside itself, as functions — so a test hands it a private root and a dictionary instead of the
/// process's real environment, and the order of the steps can be recorded.
/// </summary>
/// <param name="SetVariable">Sets an environment variable; a <c>null</c> value removes it.</param>
/// <param name="GetVariable">Reads one; <c>null</c> when it is not set (that is the environment's own answer).</param>
public sealed record RunTempSeams(Action<string, string?> SetVariable, Func<string, string?> GetVariable)
{
    /// <summary>Called once the folder EXISTS and before any variable moves: the place the process-exit cleanup is registered.</summary>
    public Action<RunTempFolder> RegisterRestore { get; init; } = static _ => { };

    /// <summary>Writes the owner claim for the folder; called last.</summary>
    public Action<string> WriteClaim { get; init; } = static _ => { };

    /// <summary>The ONE deleter the suite uses: read-only files and folders go too.</summary>
    public Action<string> Delete { get; init; } = Runners.Files.GitScratch.DeleteEvenIfReadOnly;

    /// <summary>Deletes the claim FILE — its own seam, because <see cref="Delete"/> is for a directory; called only once the folder is gone.</summary>
    public Action<string> DeleteClaim { get; init; } = File.Delete;

    /// <summary>The random part of the folder's name: six lower-case hex characters. A seam so a test can force a collision.</summary>
    public Func<string> Suffix { get; init; } = static () => Guid.NewGuid().ToString("N")[..6];

    /// <summary>Where a leftover that could not be removed is reported.</summary>
    public Action<string> Warn { get; init; } = Console.Error.WriteLine;
}

/// <summary>
/// One test run's temp folder, and the three variables pointed at it: <see cref="Create"/> makes it, <see cref="Restore"/> takes it away again
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, D2).
/// </summary>
/// <remarks>
/// <para>An instance, one per call, so a test can make as many as it likes against a private root — the module initializer, which does it once for
/// the whole process, cannot be exercised twice. The ORDER is the contract and is tested: the folder exists, the restore is registered, the variables
/// move, the owner claim is written. A restore registered after the first variable moved could be skipped by a failure in between.</para>
/// <para><see cref="Restore"/> is idempotent and never throws: it runs from an assembly fixture's disposal AND from the process-exit handler, and
/// whichever comes first does the work. A folder that cannot be removed (a file still held) is reported with its path and left to the family janitor.</para>
/// </remarks>
public sealed class RunTempFolder
{
    /// <summary>The prefix every folder carries — the family rule: <c>testsToDelete-</c> marks a folder only a test run makes.</summary>
    public const string Prefix = "testsToDelete-coai-run-";

    private const int Tries = 5;

    private readonly RunTempSeams _seams;
    private readonly IReadOnlyDictionary<string, string?> _before;
    private int _restored;

    private RunTempFolder(string folder, RunTempSeams seams, IReadOnlyDictionary<string, string?> before)
    {
        Folder = folder;
        _seams = seams;
        _before = before;
    }

    /// <summary>
    /// What <see cref="RunTemp"/> holds before the initializer has made anything, and after a folder could not be made: already restored, so ending a run
    /// that has no folder does nothing.
    /// </summary>
    public static RunTempFolder None { get; } = new(string.Empty, new RunTempSeams(static (_, _) => { }, static _ => null), new Dictionary<string, string?>()) { _restored = 1 };

    /// <summary>The three variables <c>Path.GetTempPath()</c> and a child's temp path are read from, on every operating system.</summary>
    public static IReadOnlyList<string> Variables { get; } = ["TEMP", "TMP", "TMPDIR"];

    /// <summary>The folder, inside the root it was made in.</summary>
    public string Folder { get; }

    /// <summary>The owner claim: a sibling FILE, never inside the folder.</summary>
    public string ClaimFile => Folder + ".owner";

    /// <summary>
    /// Records the three variables, makes <c>testsToDelete-coai-run-&lt;6 hex&gt;</c> inside <paramref name="realRoot"/>, registers the restore, points the variables at
    /// the folder, and writes the claim — in that order.
    /// </summary>
    public static RunTempFolder Create(string realRoot, RunTempSeams seams)
    {
        var before = Variables.ToDictionary(name => name, seams.GetVariable);
        var folder = MakeFreeFolder(realRoot, seams.Suffix);
        var run = new RunTempFolder(folder, seams, before);

        seams.RegisterRestore(run);
        foreach (var name in Variables)
        {
            seams.SetVariable(name, folder);
        }

        seams.WriteClaim(folder);

        return run;
    }

    /// <summary>
    /// A folder that did not exist, made now: a name that is taken — a folder of another run, or a claim a killed run left — is skipped and a new suffix drawn, up to
    /// <see cref="Tries"/> times, and then <see cref="IOException"/>. <c>Directory.CreateDirectory</c> succeeds on an existing folder, so without this two runs that drew the
    /// same six characters would share one folder and each remove it. <b>Limit:</b> the check and the creation are two calls, so two runs drawing the same name in the same
    /// instant (1 in 16 million, then a microsecond window) could still meet; the claim file is written after, and a run never removes a folder it did not make by name.
    /// </summary>
    private static string MakeFreeFolder(string root, Func<string> suffix)
    {
        for (var draw = 0; draw < Tries; draw++)
        {
            var path = Path.Combine(root, Prefix + suffix());
            if (!Directory.Exists(path) && !File.Exists(path + ".owner"))
            {
                return Directory.CreateDirectory(path).FullName;
            }
        }

        throw new IOException($"no free name for a run folder in '{root}' after {Tries} draws");
    }

    /// <summary>
    /// Puts the old values back (a variable that was absent is removed), then removes the folder, and the claim ONLY once the folder is gone — a folder that could not be
    /// removed keeps the claim that says whose it was. Idempotent; never throws.
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

        if (!Directory.Exists(Folder) || Remove(() => _seams.Delete(Folder), Folder))
        {
            Remove(() => _seams.DeleteClaim(ClaimFile), ClaimFile);
        }
    }

    private bool Remove(Action delete, string what)
    {
        try
        {
            delete();

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _seams.Warn($"WARNING: the test run's temp leftover '{what}' could not be removed ({e.GetType().Name}: {e.Message}); the family janitor takes it later, or delete it by hand.");

            return false;
        }
    }
}
