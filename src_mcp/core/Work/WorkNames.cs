using System.Text.RegularExpressions;

namespace CoaiMcp.Core.Work;

/// <summary>
/// The folder names of a work plan, in one place: <c>epoch-N</c>, <c>eN-*</c>, <c>sN-*</c>, <c>tN-*</c>. A folder with one of these names IS an item of the plan, whether or not
/// its id is well formed (a malformed one is an error, not an invisible folder); every other folder (<c>architecture</c>, <c>spikes</c>) is not read.
/// </summary>
public static partial class WorkNames
{
    [GeneratedRegex(@"^epoch-\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex EpochFolder();

    [GeneratedRegex(@"^e\d+-.+$", RegexOptions.CultureInvariant)]
    private static partial Regex EpicFolder();

    [GeneratedRegex(@"^s\d+-.+$", RegexOptions.CultureInvariant)]
    private static partial Regex StoryFolder();

    [GeneratedRegex(@"^t\d+-.+$", RegexOptions.CultureInvariant)]
    private static partial Regex TaskFolder();

    /// <summary>The kind a folder name says (<c>epoch</c>, <c>epic</c>, <c>story</c>, <c>task</c>), or empty for a folder that is none.</summary>
    public static string KindOf(string name) =>
        EpochFolder().IsMatch(name) ? "epoch"
        : EpicFolder().IsMatch(name) ? "epic"
        : StoryFolder().IsMatch(name) ? "story"
        : TaskFolder().IsMatch(name) ? "task"
        : string.Empty;

    public static bool IsEpoch(string name) => EpochFolder().IsMatch(name);

    public static bool IsEpic(string name) => EpicFolder().IsMatch(name);

    public static bool IsStory(string name) => StoryFolder().IsMatch(name);

    public static bool IsTask(string name) => TaskFolder().IsMatch(name);

    /// <summary>The number in a folder's name (<c>epoch-2</c>, <c>e10-x</c>), so <c>e10</c> follows <c>e9</c>; a name with none sorts last.</summary>
    public static int Number(string name)
    {
        var digits = new string([.. name.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit)]);

        return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;
    }

    /// <summary>The folder names in the order a person numbers them.</summary>
    public static IReadOnlyList<string> InOrder(IEnumerable<string> names) =>
        [.. names.OrderBy(Number).ThenBy(name => name, StringComparer.Ordinal)];
}
