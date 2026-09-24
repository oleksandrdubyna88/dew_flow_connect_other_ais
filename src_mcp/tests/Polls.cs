using System.Diagnostics;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>Waiting for something another process is about to do.</summary>
/// <remarks>
/// <para><b>A busy file is "not yet" (issue #512).</b> A condition that reads a file a live server is
/// writing can be refused for a moment — <see cref="IOException"/> on Windows while the file is held,
/// <see cref="UnauthorizedAccessException"/> while it is being replaced — and the next poll finds it whole.
/// That is <c>SharedRead</c>'s own contract for every product reader in the data directory; a poll that
/// let the exception escape turned one unlucky read into a failed test, which is what failed the
/// mcp-v0.33.0 release on win-x64.</para>
/// <para>At the deadline the same refusal is a plain <c>false</c>, so the caller's assertion fails with its
/// own sentence rather than with an exception that says nothing about what was being waited for.</para>
/// </remarks>
internal static class Polls
{
    /// <summary>Whether <paramref name="condition"/> came true within <paramref name="patience"/>.</summary>
    public static async Task<bool> Until(Func<bool> condition, TimeSpan patience)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < patience)
        {
            if (Holds(condition))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return Holds(condition, atTheDeadline: true);
    }

    /// <remarks>
    /// Still busy at the DEADLINE is said in the test's output: the assertion fails with its own sentence,
    /// and without this line nobody could tell "never came true" from "was never readable". (The code round.)
    /// </remarks>
    private static bool Holds(Func<bool> condition, bool atTheDeadline = false)
    {
        try
        {
            return condition();
        }
        catch (Exception busy) when (busy is IOException or UnauthorizedAccessException)
        {
            if (atTheDeadline)
            {
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"the poll was still refused at its deadline: {busy.GetType().Name}: {busy.Message}");
            }

            return false;
        }
    }
}
