namespace CoaiMcp.Core.Consultation;

/// <summary>
/// The anti-ping-pong rule's one structural check: a follow-up that merely repeats an earlier
/// turn's problem has verified nothing.
/// </summary>
/// <remarks>
/// Normalised before comparing — trimmed, whitespace collapsed, case folded — because the gate's plan
/// round predicted the alternative: a check that refuses a valid follow-up over a stray space, or
/// accepts a repeat over a capital letter, and a caller looping on a refusal it cannot explain.
/// </remarks>
public static class ProblemText
{
    public static string Normalise(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>The 1-based turn whose problem this one repeats, or null when it repeats none.</summary>
    public static int? RepeatsTurn(IReadOnlyList<string> earlierProblems, string problem)
    {
        var wanted = Normalise(problem);
        for (var i = 0; i < earlierProblems.Count; i++)
        {
            if (Normalise(earlierProblems[i]) == wanted)
            {
                return i + 1;
            }
        }

        return null;
    }
}
