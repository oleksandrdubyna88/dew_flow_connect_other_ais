using CoaiMcp.Core.Notices;

namespace CoaiMcp.Core.Work;

/// <summary>
/// The rules of one task: a <c>dod</c> with items and unique ids, and no <c>security: false</c> on a task of kind <c>security</c>. Also what the answer carries beside the errors:
/// the <c>important</c> tasks with their reason (<see cref="ImportantRule"/>), and the notes — a feature <c>workMethod</c> that differs from the device's, and a task text that holds a word of
/// <c>shared/credential-words.json</c> while the task is not <c>security: true</c>. Notes never fail a plan.
/// </summary>
internal static class WorkTaskRules
{
    internal static void Check(WorkFeature feature, List<WorkError> errors, List<WorkImportant> important)
    {
        foreach (var task in feature.AllTasks.Where(task => task.Loaded))
        {
            Dod(task, errors);
            if (task.Kind == "security" && task.Security == SecurityFlag.False)
            {
                errors.Add(new WorkError(WorkErrorCodes.SecurityContradiction, task.Path, task.Node, "the task is kind: security but says security: false; remove the flag or the kind"));
            }

            if (ImportantRule.Reason(task.Kind, task.Complexity, task.Security == SecurityFlag.True) is { Length: > 0 } reason)
            {
                important.Add(new WorkImportant(task.Node, reason));
            }
        }
    }

    private static void Dod(WorkTask task, List<WorkError> errors)
    {
        if (task.Dod.Count == 0)
        {
            errors.Add(new WorkError(WorkErrorCodes.EmptyDod, task.Path, task.Node, "a task needs at least one dod item: how anybody knows it is done"));
        }

        foreach (var id in task.Dod.GroupBy(item => item.Id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            errors.Add(new WorkError(WorkErrorCodes.DuplicateDodId, task.Path, task.Node, $"the dod id '{id}' is used by more than one item"));
        }
    }

    /// <summary>The notes of a plan, in a fixed order.</summary>
    internal static IReadOnlyList<string> Notes(WorkFeature feature, WorkLimits limits)
    {
        var notes = new List<string>();
        var device = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["maxEpicsPerEpoch"] = limits.MaxEpicsPerEpoch,
            ["maxStoriesPerEpic"] = limits.MaxStoriesPerEpic,
            ["maxTasksPerStory"] = limits.MaxTasksPerStory,
            ["mvpEpics.min"] = limits.MvpMin,
            ["mvpEpics.max"] = limits.MvpMax,
        };
        foreach (var (key, value) in feature.MethodNumbers.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (device.TryGetValue(key, out var limit) && limit != value)
            {
                notes.Add($"feature.yaml workMethod.{key} is {value}; this device's limit is {limit} and it is the one checked");
            }
        }

        foreach (var task in feature.AllTasks.Where(task => task.Loaded && task.Security != SecurityFlag.True))
        {
            if (CredentialWordIn(task) is { Length: > 0 } word)
            {
                notes.Add($"{task.Node}: the text holds the credential word '{word}' but the task has no security: true");
            }
        }

        return notes;
    }

    private static string CredentialWordIn(WorkTask task)
    {
        var text = string.Join(' ', new[] { task.Title }.Concat(task.Dod.Select(item => item.Item)));
        var words = text.Split(
            [.. text.Where(c => !char.IsLetterOrDigit(c)).Distinct()], StringSplitOptions.RemoveEmptyEntries);

        return words.FirstOrDefault(CredentialWords.NamesACredential) ?? string.Empty;
    }
}
