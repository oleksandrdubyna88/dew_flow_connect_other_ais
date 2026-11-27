using System.Runtime.CompilerServices;

namespace CoaiMcp.Tests;

/// <summary>
/// A run that has a per-user data folder of its own when the machine gives it none (epoch 3 E4 s5 t6 d2). The product asks for the person's application-data folder
/// (<c>Environment.GetFolderPath(LocalApplicationData)</c>) for the data folder, the coder worktrees, the agy launch folder and more; on Linux .NET answers an EMPTY string
/// when that folder (<c>$XDG_DATA_HOME</c>, else <c>$HOME/.local/share</c>) does not exist. A check unit of a coder job is such a place: its <c>HOME</c> is a new empty folder, so
/// 94 tests of the suite failed there ("agy was not launched: there is no per-user folder", "'coai-mcp' is not rooted") and passed on the host.
/// </summary>
/// <remarks>
/// <para>Where the answer is empty, the run makes <c>data</c> inside its own run folder (<see cref="RunTemp.Folder"/>, removed at the end of the run with the rest of it) and points
/// <c>XDG_DATA_HOME</c> at it, once, before any test. Where the machine has the folder, nothing is made and nothing is set: a run on the host reads exactly what it read before, and
/// never the person's real folder through a changed variable. The real <c>HOME</c> is never written.</para>
/// <para>It is a second writer of the process environment beside <see cref="RunTemp"/> and <c>FakeCliSteering</c>: it runs once in a module initializer, never again.</para>
/// </remarks>
public static class PerUserFolderForTheRun
{
    /// <summary>The variable .NET reads first for the application-data folder on Unix.</summary>
    public const string Variable = "XDG_DATA_HOME";

    [ModuleInitializer]
    internal static void Start()
    {
        try
        {
            RunTemp.Start();
            _ = Ensure(RunTemp.Folder, OperatingSystem.IsWindows(), () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), static (name, value) => Environment.SetEnvironmentVariable(name, value));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Never throw from a module initializer (it kills the host): a run that could not make the folder fails the tests that need it, with their own message.
        }
    }

    /// <summary>
    /// The folder made, or null when none was needed or none can be made: <paramref name="localApplicationData"/> answers non-empty (the machine has it), the host is Windows, or the run folder is unknown.
    /// </summary>
    internal static string? Ensure(string runFolder, bool windows, Func<string> localApplicationData, Action<string, string?> set)
    {
        if (windows || runFolder.Length == 0 || localApplicationData().Length > 0)
        {
            return null;
        }

        var data = Path.Combine(runFolder, "data");
        Directory.CreateDirectory(data);
        set(Variable, data);

        return data;
    }
}
