namespace CoaiMcp.Tests;

/// <summary>
/// Where this repository's checkout is, for a test that reads its sources. The folder holding <c>.agents</c>, walked up
/// from the test binary — the same rule three older tests each spell privately (<c>ARequestFaultIsNotAnOldBinaryTests</c>,
/// <c>GoodEnoughPolicyTests</c>, <c>NothingReadsAnotherProgramsSourceTests</c>); new tests use this one.
/// </summary>
internal static class RepoPaths
{
    public static string Root()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !Directory.Exists(Path.Combine(here.FullName, ".agents")))
        {
            here = here.Parent;
        }

        return here?.FullName ?? AppContext.BaseDirectory;
    }
}
