namespace CoaiMcp.Tests;

/// <summary>
/// Whatever a one-shot mode wrote to stdout, and the code it exited with — stdout being the mode's
/// entire interface.
/// </summary>
/// <remarks>
/// <para>Suites using it belong to the <c>console-out</c> collection: <c>Console.SetOut</c> is
/// process-wide, and two suites swapping it in parallel would read each other's output.</para>
/// <para>Extracted for the file-at suite (story 3.1 of the review-page plan) from
/// <c>TheRealMethodTests</c>; <c>ThePairModesTests</c>, <c>ABatchFindingsReadTests</c> and
/// <c>BugsQueryTests</c> carried their own copy of the same eight lines and were migrated here on
/// that story's code round — a second implementation is a defect from the moment it compiles,
/// because the two drift and nothing notices.</para>
/// <para>There are two, and the synchronous one is not an oversight: three of the four one-shots
/// this captures are synchronous, and wrapping them in a task to reuse one helper would add an
/// await to a test that has nothing to wait for.</para>
/// </remarks>
internal static class Stdout
{
    /// <summary>Whatever a synchronous one-shot wrote, and the code it exited with.</summary>
    public static (string Out, int Code) Of(Func<int> mode)
    {
        var stdout = new StringWriter();
        var was = Console.Out;
        try
        {
            Console.SetOut(stdout);
            var code = mode();

            return (stdout.ToString(), code);
        }
        finally
        {
            Console.SetOut(was);
        }
    }

    public static async Task<(string Out, int Code)> OfAsync(Func<Task<int>> mode)
    {
        var stdout = new StringWriter();
        var was = Console.Out;
        try
        {
            Console.SetOut(stdout);
            var code = await mode();

            return (stdout.ToString(), code);
        }
        finally
        {
            Console.SetOut(was);
        }
    }
}
