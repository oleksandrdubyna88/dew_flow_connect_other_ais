using System.Runtime.CompilerServices;

namespace CoaiMcp.Tests;

/// <summary>
/// The test process's temp folder: ONE fresh folder per run, made before any test code, that everything a run makes in "temp" lands in
/// (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>, D2/E2).
/// </summary>
/// <remarks>
/// <para><b>A module initializer, not the assembly fixture.</b> It runs when the module loads — before <c>Main</c>, before any type's static constructor and
/// before xUnit discovers a <c>MemberData</c> — so no class fixture, static field or theory row can have read <c>Path.GetTempPath()</c> first. A fixture is
/// built after discovery, which is too late for the last two. It points <c>TEMP</c>, <c>TMP</c> and <c>TMPDIR</c> at the folder ONCE and never again during the
/// run: the one class that moves them (<c>AVibeHomesNeverLiveUnderTempTests</c>, in <c>fakecli-env</c>) restores what it found, which is this folder.</para>
/// <para><b>Children inherit it</b> because the three names are on every launch's allowlist (<c>ProcessEnvironment</c>), so the product's
/// <c>coai-answers-</c> / <c>-repair-</c> / <c>-noworkspace-</c> folders and the loose <c>api-*.prompt</c> files land inside it too.</para>
/// <para><b>The end.</b> <see cref="EndOfRun"/> runs from <see cref="RunTempFixture"/>'s disposal AND from the process-exit handler registered here, so a run that never builds a fixture
/// (<c>--list-tests</c>, <c>--help</c>, a filter that matches nothing) leaves nothing either. A KILLED run leaves exactly one folder plus its <c>.owner</c> claim: the family
/// janitor takes it after an hour (<c>.agents/conventions/common/test-leftovers.md</c>) until the conventions' owner-liveness sweep replaces it.</para>
/// <para><b>Never throws.</b> A throw from a module initializer kills the process. If the folder cannot be made, ONE warning goes to stderr, the variables stay as they were
/// and <see cref="Folder"/> is <see cref="Real"/> — the run then behaves as it did before this existed.</para>
/// </remarks>
public static class RunTemp
{
    private static RunTempFolder _run = RunTempFolder.None;

    /// <summary>The machine's real temp folder, trailing separator trimmed — for the tests that mean IT, and for the start-of-run sweep.</summary>
    public static string Real { get; private set; } = string.Empty;

    /// <summary>This run's folder, inside <see cref="Real"/>; <see cref="Real"/> itself when it could not be made.</summary>
    public static string Folder { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Start()
    {
        Real = Trimmed(Path.GetTempPath());
        Folder = Real;
        try
        {
            _run = RunTempFolder.Create(Real, Seams());
            Folder = _run.Folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(CreationWarning(Real, e));
        }
    }

    /// <summary>
    /// The machine's temp path without its trailing separator — but never trimmed to nothing or to a bare drive letter: a temp folder that IS a root (<c>/</c>, <c>C:</c>) keeps it,
    /// or <c>Path.Combine("", name)</c> would make the run folder a RELATIVE path.
    /// </summary>
    internal static string Trimmed(string path)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;

        return path.Length <= root.Length ? path : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>The one warning of a run that could not make its folder: the path tried, that the run goes on WITHOUT isolation, and what to do.</summary>
    internal static string CreationWarning(string root, Exception e) =>
        $"WARNING: the test run's temp folder could not be made in '{root}' ({e.GetType().Name}: {e.Message}). The run CONTINUES in the machine's temp folder WITHOUT isolation: "
        + "its leftovers share the folder with every other run's. Make the folder writable, or point TMP / TMPDIR (TEMP on Windows) at a writable one, and run again.";

    /// <summary>The warning of a claim that could not be written: the consequence, plainly.</summary>
    internal static string ClaimWarning(string folder, Exception e) =>
        $"WARNING: the owner claim for '{folder}' could not be written ({e.GetType().Name}: {e.Message}). The run goes on, but if it is killed its folder is collected ONLY by the family "
        + "janitor's age rule — after one hour (.agents/conventions/common/test-leftovers.md) — not by owner liveness.";

    /// <summary>Restores the three variables and removes the folder and its claim. Idempotent: whichever of the fixture and the process exit comes first does the work.</summary>
    internal static void EndOfRun() => _run.Restore();

    private static RunTempSeams Seams() =>
        new(static (name, value) => Environment.SetEnvironmentVariable(name, value), static name => Environment.GetEnvironmentVariable(name))
        {
            RegisterRestore = static run => AppDomain.CurrentDomain.ProcessExit += (_, _) => run.Restore(),
            WriteClaim = WriteClaim,
        };

    /// <summary>The ONE writer of the owner claim: <c>&lt;folder&gt;.owner</c>, beside the folder. A claim that cannot be written leaves the folder to the janitor, and says so.</summary>
    internal static void WriteClaim(string folder)
    {
        try
        {
            File.WriteAllText(folder + ".owner", OwnerClaim.ForThisProcess());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ClaimWarning(folder, e));
        }
    }
}
