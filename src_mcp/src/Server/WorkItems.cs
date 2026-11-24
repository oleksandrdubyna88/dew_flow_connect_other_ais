namespace CoaiMcp.Server;

/// <summary>
/// The optional <c>workItem</c> argument of <c>review_plan</c>, <c>review_code</c>, <c>review_document</c>, <c>review_feature</c>, <c>cons</c> and
/// <c>consask</c>: the repository-relative path of the epoch, epic, story or task folder a call is FOR (coai v2 E3.S8, <c>v2/architecture/server.md</c> §21).
/// </summary>
/// <remarks>
/// <para><b>Checked as TEXT, and never against the disk.</b> The item folder may not exist yet when a plan gate runs over the plan that will create it, and the value is
/// only ever stored and compared; nothing here opens it, so a link in the repository can lead nowhere. What is refused is what could not name an item inside the
/// repository: an absolute or drive-lettered path, a <c>..</c> segment, a control character, a colon, an empty answer once the separators are tidied, or more than
/// <see cref="MaxLength"/> characters.</para>
/// <para><b>One spelling.</b> Backslashes become <c>/</c>, empty and <c>.</c> segments and a trailing separator go, so <c>v2\epoch-1\e3\</c> and
/// <c>v2/epoch-1/e3</c> are one item and the report matches on the string.</para>
/// <para><b>Empty means "named none".</b> A caller that does not send the argument is the old caller, unchanged: <see cref="Check"/> answers an empty item and no
/// refusal, and the call records nothing extra.</para>
/// </remarks>
internal static class WorkItems
{
    /// <summary>The longest item path accepted, in characters — a path a person could type, with room to spare.</summary>
    public const int MaxLength = 400;

    /// <summary>The item as stored — one spelling, or empty when none was named — and the refusal sentence, empty when the value is acceptable.</summary>
    public static (string Item, string Refusal) Check(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (string.Empty, string.Empty);
        }

        var text = raw.Trim().Replace('\\', '/');
        if (text.Length > MaxLength)
        {
            return Refused($"is {text.Length} characters; at most {MaxLength} are accepted");
        }

        if (text.Any(char.IsControl))
        {
            return Refused("holds a control character");
        }

        if (text.StartsWith('/') || text.Contains(':'))
        {
            return Refused("is not a relative path — give the item's folder as it is written from the repository's top level, such as v2/epoch-1/e3-x/stories/s8-y");
        }

        var segments = new List<string>();
        foreach (var segment in text.Split('/'))
        {
            if (segment == "..")
            {
                return Refused("has a `..` segment, so it could leave the repository");
            }

            if (segment.Length > 0 && segment != ".")
            {
                segments.Add(segment);
            }
        }

        return segments.Count == 0
            ? Refused("names the repository itself, not an item of a plan")
            : (string.Join('/', segments), string.Empty);
    }

    /// <summary>The refusal as the caller reads it, naming the argument and what was wrong — and the cure, which is to send a path or nothing.</summary>
    private static (string Item, string Refusal) Refused(string why) =>
        (string.Empty, $"workItem {why} — send the feature-folder path of the epoch, epic, story or task this call is for, or leave `workItem` out.");
}
