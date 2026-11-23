namespace CoaiMcp.Core.Work;

/// <summary>
/// Validates a feature folder against the work-plan contract (<c>shared/work-method.md</c>): reads it through <see cref="IWorkFolder"/> and <see cref="IYamlReader"/>, checks every file,
/// then the rules of structure, dependencies, parallel work and tasks. Pure over its inputs and never writes. <b>The limits are an argument</b> — the device's work method — and no
/// number of the work method is written in this class or the rule classes it calls.
/// </summary>
public sealed class WorkPlanValidator(IYamlReader reader)
{
    /// <summary>The report for <paramref name="folder"/> under <paramref name="limits"/>.</summary>
    public WorkReport Validate(IWorkFolder folder, WorkLimits limits)
    {
        var errors = new List<WorkError>();
        if (new WorkPlanLoader(folder, reader, errors).Load() is not { } feature)
        {
            return Report(false, new WorkCounts(0, 0, 0, 0), errors, [], []);
        }

        var important = new List<WorkImportant>();
        WorkStructureRules.Check(feature, limits, errors);
        var edges = WorkDependencyRules.Check(feature, errors);
        WorkParallelRules.Check(feature, edges, errors);
        WorkTaskRules.Check(feature, errors, important);

        var counts = new WorkCounts(feature.Epochs.Count, feature.AllEpics.Count(), feature.AllStories.Count(), feature.AllTasks.Count());

        return Report(true, counts, errors, WorkTaskRules.Notes(feature, limits), important);
    }

    private static WorkReport Report(bool found, WorkCounts counts, List<WorkError> errors, IReadOnlyList<string> notes, List<WorkImportant> important)
    {
        var sorted = errors
            .OrderBy(error => error.Path, StringComparer.Ordinal)
            .ThenBy(error => error.Code, StringComparer.Ordinal)
            .ThenBy(error => error.Node, StringComparer.Ordinal)
            .ThenBy(error => error.Message, StringComparer.Ordinal)
            .ToList();
        var ordered = important.OrderBy(item => item.Node, StringComparer.Ordinal).ToList();
        var truncated = sorted.Count > WorkReport.MaxListed || notes.Count > WorkReport.MaxListed;

        return new WorkReport(found, counts, [.. sorted.Take(WorkReport.MaxListed)], [.. notes.Take(WorkReport.MaxListed)], ordered, truncated);
    }
}
