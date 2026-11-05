namespace CoaiMcp.FakeCli;

/// <summary>
/// <c>unreaped-child</c> (C21 and C29 of <c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp_E5.md</c>, E5 checkpoint 2, F0): the parent that never reaps, as an executable with an
/// argv instead of a shell script. It starts a <see cref="PreExecChild"/>, prints its pid on one line and then waits without ever <c>wait</c>ing for it — so when the test kills the child, the
/// child stays a zombie for as long as this process lives. This process is never a shell, so there is no moment at which a parent could still collect the dead child itself.
/// </summary>
internal static class UnreapedChildVerb
{
    internal static int Run()
    {
        if (!OperatingSystem.IsLinux())
        {
            return 64;
        }

        var child = PreExecChild.Start();
        Console.Out.WriteLine(child);
        Console.Out.Flush();
        Thread.Sleep(120_000);

        return 0;
    }
}
