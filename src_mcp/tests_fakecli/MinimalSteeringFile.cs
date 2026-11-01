namespace CoaiMcp.FakeCli;

/// <summary>
/// Where the fake CLI's minimal-environment steering file is: BESIDE ITS OWN BINARY, in the one place the test that writes it
/// and the child that reads it always agree on — whatever environment the child was started with.
/// </summary>
/// <remarks>
/// <para>It was <c>Path.GetTempPath()</c>, and that is one folder per Windows user: two test runs from two worktrees wrote and deleted
/// each other's file, so one run's fake answered as another run's agy (<c>todo/PLAN_the_suite_is_deterministic_and_leaves_no_temp.md</c>,
/// C1). The folder of the fake's binary is the test project's own output folder — one per checkout and configuration — and
/// <see cref="AppContext.BaseDirectory"/> of the child IS the <see cref="AppContext.BaseDirectory"/> of the test that started it.</para>
/// <para>Public, because the test project reads it too: ONE definition of the name and the place, in the assembly that reads the file.</para>
/// </remarks>
public static class MinimalSteeringFile
{
    /// <summary>The file's name.</summary>
    public const string Name = "fakecli-minimal.json";

    /// <summary>The file's full path: beside the fake's binary.</summary>
    public static string Location => Path.Combine(AppContext.BaseDirectory, Name);
}
