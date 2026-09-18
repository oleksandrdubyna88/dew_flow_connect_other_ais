namespace CoaiMcp.Tests;

/// <summary>
/// Whatever a one-shot mode wrote to stdout, and the code it exited with — stdout being the mode's
/// entire interface.
/// </summary>
/// <remarks>
/// <para>Suites using it belong to the <c>console-out</c> collection: <c>Console.SetOut</c> is
/// process-wide, and two suites swapping it in parallel would read each other's output.</para>
/// <para>Extracted for the file-at suite (story 3.1 of the review-page plan) from
/// <c>TheRealMethodTests</c>, which now delegates here. Three older suites — <c>ThePairModesTests</c>,
/// <c>ABatchFindingsReadTests</c>, <c>BugsQueryTests</c> — carry their own copy of the same eight
/// lines; they are named in the story's record rather than rewritten as a side effect of it.</para>
/// </remarks>
internal static class Stdout
{
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
