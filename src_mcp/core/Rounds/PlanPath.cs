using CoaiMcp.Core.Feature;

namespace CoaiMcp.Core.Rounds;

/// <summary>A committed plan's path made canonical, or the one sentence that says why it cannot be one.</summary>
/// <param name="Path">The repository-relative path with <c>/</c> separators; what was asked when refused.</param>
/// <param name="Refusal">Empty when the path is usable.</param>
public sealed record PlanPathCheck(string Path, string Refusal);

/// <summary>
/// What a caller's <c>committedPlan</c> may say — decided on the string, before any process: the shape of a path inside
/// the repository, and a name that is never read.
/// </summary>
/// <remarks>
/// <para>Trimmed, and <c>\</c> becomes <c>/</c> first, as the cadence's <c>plan</c> path does: a Windows caller's
/// <c>todo\PLAN_x.md</c> is the committed <c>todo/PLAN_x.md</c>. Then, in this order: not repository-relative
/// (<see cref="RepoPaths.WhyNotRelative"/>: empty, a NUL, absolute, a drive, <c>..</c>), a trailing <c>/</c> (that names
/// what is UNDER a directory, never one file), a credential-shaped name (<see cref="CredentialFiles"/>, the same table
/// <c>review_feature</c>'s <c>planPath</c> refuses by).</para>
/// <para>Every sentence ends in the path that would have worked, so a caller never has to guess the shape.</para>
/// </remarks>
public static class PlanPath
{
    private const string Example = "todo/PLAN_x.md";

    public static PlanPathCheck Check(string raw)
    {
        var path = RepoPaths.Canonical(raw);
        var why = RepoPaths.WhyNotRelative(path);
        if (why.Length > 0)
        {
            return Refused(raw, $"'{raw.Trim()}' {why} — pass a path relative to the repository root, as committed ({Example})");
        }

        return path.EndsWith('/')
            ? Refused(raw, $"'{path}' ends with '/' — name ONE committed file, not a directory ({Example})")
            : Credential(path);
    }

    private static PlanPathCheck Credential(string path) =>
        CredentialFiles.WhichPattern(path) is { Length: > 0 } shape
            ? Refused(path, CredentialFiles.NamedSentence(path, shape))
            : new PlanPathCheck(path, string.Empty);

    private static PlanPathCheck Refused(string path, string sentence) => new(path, sentence);
}
