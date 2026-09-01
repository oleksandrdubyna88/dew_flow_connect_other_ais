namespace CoaiMcp.Core.Rounds;

/// <summary>
/// What a round was ABOUT, in a few words — the line a person reads to tell one round from
/// another.
/// </summary>
/// <remarks>
/// <para>A round used to be identified by its branch and its number: <c>main · PlanReview 4</c>.
/// That says which gate ran and nothing about what went through it, so a list of rounds over a
/// week is a list of numbers. The subject is derived from the plan the caller passed, because
/// that is the only description of the work anyone has bothered to write down.</para>
/// <para>Derived rather than asked for: adding a `title` argument to the tools would put the
/// burden on the caller and be forgotten by every caller that is in a hurry — which is all of
/// them.</para>
/// </remarks>
public static class RoundSubject
{
    private const int MaxLength = 60;

    /// <param name="fileExists">Injected so the rule is a unit test rather than a disk.</param>
    public static string From(string planText, Func<string, bool> fileExists)
    {
        var trimmed = (planText ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        // A caller that passed a PATH gets the file's name. The tools take the plan's text, but a
        // path is the honest mistake to make and the answer is more useful than the path itself.
        if (!trimmed.Contains('\n') && trimmed.Length < 260 && fileExists(trimmed))
        {
            return Path.GetFileName(trimmed);
        }

        return Shorten(Heading(trimmed) ?? FirstWords(trimmed));
    }

    /// <summary>The document's own title, which is what its author chose to call the work.</summary>
    private static string? Heading(string text)
    {
        foreach (var line in text.Split('\n').Take(20))
        {
            var candidate = line.Trim();
            if (candidate.StartsWith('#'))
            {
                return candidate.TrimStart('#').Trim();
            }
        }

        return null;
    }

    /// <summary>No heading: the first line that says something, which is usually a sentence.</summary>
    private static string FirstWords(string text) =>
        text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;

    /// <summary>
    /// Cut on a word boundary, with an ellipsis. A sidebar line has room for about sixty
    /// characters, and a title cut mid-word reads as corruption rather than as brevity.
    /// </summary>
    private static string Shorten(string text)
    {
        var single = string.Join(' ', text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (single.Length <= MaxLength)
        {
            return single;
        }

        var cut = single[..MaxLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > MaxLength / 2 ? cut[..lastSpace] : cut).TrimEnd(',', '.', ';', ':', '—', '-') + "…";
    }

    /// <summary>The stage as a person would say it, not as the enum spells it.</summary>
    public static string StageName(string stage) => stage switch
    {
        "PlanReview" => "plan review",
        "CodeReview" => "code review",
        _ => "done",
    };
}
