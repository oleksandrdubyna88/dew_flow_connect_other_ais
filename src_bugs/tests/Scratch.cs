using Microsoft.Data.Sqlite;
using Xunit;

namespace CoaiBugs.Tests;

/// <summary>
/// The throwaway directory a test worked in, and getting rid of it afterwards.
/// </summary>
/// <remarks>
/// <b>A cleanup that cannot be done is REPORTED, not swallowed.</b> Two fixtures each ended with an
/// empty <c>catch (IOException) { }</c>, which a code round called out under the rule against
/// silently discarding an error — and it is the right call for a reason the rule does not have to
/// spell out: temp directories that quietly fail to go away are how a machine ends up with eighty
/// thousand of them and a suite that looks hung. The failure must not FAIL the test — the assertions
/// have already passed and a locked file says nothing about the code — so it goes to the test's own
/// diagnostic channel, where it is visible with <c>--diagnostic</c> and counted by whoever looks.
/// </remarks>
internal static class Scratch
{
    /// <summary>Closes every pooled handle and removes the directory, saying so if it cannot.</summary>
    public static void Delete(string dir)
    {
        // The connections are what hold the file on Windows, and a test that disposed its `Corpus`
        // has still left the pool holding it.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TestContext.Current.SendDiagnosticMessage(
                "the scratch directory {0} could not be removed: {1}", dir, e.Message);
        }
    }
}
