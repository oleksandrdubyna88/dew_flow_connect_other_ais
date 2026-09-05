using CoaiBench.Model;

namespace CoaiBench.Judging;

/// <summary>
/// A judgement that survives being interrupted.
/// </summary>
/// <remarks>
/// <para>The first version judged every run and wrote the file once, at the end. On 2026-09-05 a
/// judgement was stopped twelve runs in to change the model, and twelve runs of answers went with
/// it — each of them a paid CLI turn per finding, none of them anywhere but in memory.</para>
/// <para>Nothing about the pass needs that. Every run is judged independently, and the file it
/// writes is the file it reads. So the answer lands on disk as soon as it exists, and a pass
/// restarted over the same file judges only what THIS judge has not judged yet: the run carries the
/// model that marked it, so an interrupted pass resumes, and a different model re-judges everything
/// rather than leaving a file half in one opinion and half in another.</para>
/// </remarks>
public static class JudgePass
{
    public static async Task<IReadOnlyList<RunRecord>> RunAsync(
        IReadOnlyList<RunRecord> runs,
        string model,
        Func<RunRecord, CancellationToken, Task<RunRecord>> judge,
        Func<IReadOnlyList<RunRecord>, CancellationToken, Task> save,
        Action<RunRecord, bool> report,
        CancellationToken ct)
    {
        var judged = runs.ToList();
        for (var index = 0; index < judged.Count; index++)
        {
            if (!NeedsJudging(judged[index], model))
            {
                report(judged[index], false);
                continue;
            }

            judged[index] = await judge(judged[index], ct);

            // Before the next one starts, not after the last one finishes. The whole file is
            // rewritten each time: it is a few hundred kilobytes against a CLI turn per finding,
            // and it means the file on disk is never a partial record of a run.
            await save(judged, ct);
            report(judged[index], true);
        }

        return judged;
    }

    /// <summary>Whether this judge has an opinion on this run yet.</summary>
    public static bool NeedsJudging(RunRecord run, string model) => run.JudgedBy != model;
}
