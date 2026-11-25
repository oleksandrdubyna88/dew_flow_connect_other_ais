namespace CoaiMcp.Server.RoleJobs;

/// <summary>
/// The arguments of <c>start_role</c>, checked as TEXT before anything is made: what could not name a place inside the repository, or would make a folder or a unit with a name nobody chose,
/// is refused with the cure. Nothing here opens a file.
/// </summary>
internal static class RoleRequestChecks
{
    /// <summary>The longest brief, in characters — 64 KiB, the same cap as the plan text a role is handed.</summary>
    public const int MaxBriefLength = 65_536;

    public const int MostTimeoutMinutes = 24 * 60;

    public const int MaxNameLength = 100;

    /// <summary>A repository-relative path of a task or epic file: one spelling, or the sentence that says why it is refused. The argument's own name is in the sentence.</summary>
    public static (string Path, string Refusal) RelativeFile(string? raw, string argument)
    {
        var text = (raw ?? string.Empty).Trim().Replace('\\', '/');
        if (text.Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        if (text.Length > WorkItems.MaxLength || text.Any(char.IsControl) || text.StartsWith('/') || text.Contains(':'))
        {
            return (string.Empty, $"{argument} must be a path inside the repository, written from its top level (not absolute, no drive, no control characters, at most {WorkItems.MaxLength} characters)");
        }

        var segments = text.Split('/').Where(segment => segment.Length > 0 && segment != ".").ToList();

        return segments.Contains("..") || segments.Count == 0
            ? (string.Empty, $"{argument} must name a file inside the repository: no `..` segment, and not the repository itself")
            : (string.Join('/', segments), string.Empty);
    }

    /// <summary>
    /// What a role needs of the pair <c>taskFile</c> and <c>brief</c>, as text: empty when it is fit, else the sentence that refuses it. The BA and the coder take exactly ONE of the two; a role that answers a question
    /// (<see cref="RoleDescriptor.NeedsBrief"/>: the scout, the judge) needs the brief and may name the file it is about. The brief's cap is the same for both.
    /// </summary>
    public static string InputRefusal(RoleDescriptor descriptor, string taskFile, string brief)
    {
        var blank = string.IsNullOrWhiteSpace(brief);
        if (descriptor.NeedsBrief)
        {
            return blank || brief.Length > MaxBriefLength
                ? $"the {descriptor.Role} answers a question: send brief, the question (text of at most {MaxBriefLength} characters, not empty); taskFile is optional and names the file the question is about"
                : string.Empty;
        }

        return (taskFile.Length == 0) == blank || brief.Length > MaxBriefLength
            ? $"send exactly one of taskFile (a path inside the repository) or brief (text of at most {MaxBriefLength} characters)"
            : string.Empty;
    }

    /// <summary>The model the caller named: empty, or a short plain name. Whether the router accepts it is the router's (story s3).</summary>
    public static string ModelRefusal(string? model) =>
        model is null or "" || (model.Length <= MaxNameLength && !model.Any(char.IsControl) && !model.Any(char.IsWhiteSpace))
            ? string.Empty
            : $"model must be one model name of at most {MaxNameLength} characters, with no spaces";
}
