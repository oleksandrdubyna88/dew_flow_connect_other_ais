namespace CoaiMcp.Core.Collecting;

/// <summary>
/// The first line of a git-lfs pointer file — the ONE constant, read by the snapshot planner (bytes on disk) and by the
/// committed-plan reader (text from a blob).
/// </summary>
/// <remarks>
/// A pointer is what a repository holds INSTEAD of a large file when git-lfs is on: a three-line text beginning with this
/// line. Reviewing it as if it were the plan would hand every reviewer thirty bytes of bookkeeping and no plan. Only a
/// PREFIX counts — a plan that quotes the header further down is a plan.
/// </remarks>
public static class LfsPointer
{
    /// <summary>The line the git-lfs specification (v1) opens every pointer with.</summary>
    public const string Header = "version https://git-lfs.github.com/spec/v1";

    /// <summary>Whether this text OPENS with the header — ordinal, so no culture folds a lookalike into a match.</summary>
    public static bool IsPointer(string text) => text.StartsWith(Header, StringComparison.Ordinal);
}
