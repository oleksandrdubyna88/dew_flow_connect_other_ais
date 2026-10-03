namespace CoaiMcp.Server;

/// <summary>
/// Where a consultation's panel-facing files live under a data directory, and how long a move over one a reader holds
/// is retried — ONE copy for every writer and reader of them.
/// </summary>
/// <remarks>
/// <para>The whole-branch review of PLAN_the_consultant_works_on_every_vendor.md (2026-10-03), finding H: the health
/// directory was spelled out in five files and the answers directory in two, and each health writer kept its own
/// 500 ms — a rename of the directory or a change of the retry would have reached some of them.</para>
/// <para><c>health/</c> is a directory of its own beneath <c>consultations/</c>: <c>ConsultationStore.All</c> and the
/// panel's <c>consultations/*.json</c> watcher list only that directory's own files, so neither sees these.</para>
/// </remarks>
public static class ConsultHealthPaths
{
    /// <summary>
    /// How long a move over a file a reader holds is retried: Windows refuses a move over a file open without delete
    /// sharing, and the panel polls these files — a reader holds a small one for milliseconds.
    /// </summary>
    public static readonly TimeSpan SharingRetry = TimeSpan.FromMilliseconds(500);

    /// <summary><c>&lt;dataDir&gt;/consultations/health</c> — the outcome files, the check state and lock, <c>consultants.json</c>.</summary>
    public static string HealthDirectory(string dataDir) => Path.Combine(dataDir, "consultations", "health");

    /// <summary><c>&lt;dataDir&gt;/consultations/answers</c> — where a consultant's CLI may write its answer file.</summary>
    public static string AnswersDirectory(string dataDir) => Path.Combine(dataDir, "consultations", "answers");

    /// <summary>The survey <c>--consultants</c> writes for the other side, and every recorded outcome refreshes.</summary>
    public static string ConsultantsFile(string dataDir) => Path.Combine(HealthDirectory(dataDir), "consultants.json");
}
