namespace CoaiMcp.Tests;

/// <summary>
/// Where this repository's checkout is, for a test that reads its sources: the first folder above the test binary that
/// holds both halves, <c>src_mcp</c> and <c>src_vs_code</c>. The one helper every test uses — five tests used to spell their
/// own, looking for three different markers, and two of them quietly answered the test binary's own folder when nothing
/// was found, so a moved layout read the wrong files instead of failing.
/// </summary>
internal static class RepoPaths
{
    /// <exception cref="InvalidOperationException">No folder above the test binary holds both halves.</exception>
    public static string Root()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        while (here is not null && !HoldsBothHalves(here.FullName))
        {
            here = here.Parent;
        }

        return here?.FullName
            ?? throw new InvalidOperationException($"the repository root (a folder holding src_mcp and src_vs_code) was not found above {AppContext.BaseDirectory}");
    }

    private static bool HoldsBothHalves(string folder) =>
        Directory.Exists(Path.Combine(folder, "src_mcp")) && Directory.Exists(Path.Combine(folder, "src_vs_code"));
}
