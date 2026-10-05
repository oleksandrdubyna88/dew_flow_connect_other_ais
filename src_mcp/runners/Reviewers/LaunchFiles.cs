namespace CoaiMcp.Runners.Reviewers;

/// <summary>
/// The files a launch wrote for its child (<see cref="ReviewerInvocation.TempFiles"/>) — deleted when the launch, or every
/// retry of it, is over (todo/PLAN_one_model_catalog.md, epic 2).
/// </summary>
public static class LaunchFiles
{
    /// <summary>
    /// Deletes what a launch wrote for its child. A file that cannot be deleted now is left to the answers-directory
    /// sweep (six hours), which is still there: failing a finished review over a locked file would lose its answer.
    /// </summary>
    public static void Forget(IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left for the sweep; see the summary.
            }
        }
    }
}
