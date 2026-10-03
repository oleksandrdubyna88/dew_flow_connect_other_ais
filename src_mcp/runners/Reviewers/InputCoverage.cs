namespace CoaiMcp.Runners.Reviewers;

/// <summary>A usage ratio can detect suspicious truncation, never prove that an engine read everything.</summary>
public static class InputCoverage
{
    public static string Of(ReviewerWork work, ReviewerOutcome.Ok answer)
    {
        if (!work.Invocation.IsOnEngine) return string.Empty;
        if (answer.Usage.NotCaptured || answer.Usage.TokensIn <= 0) return "Input coverage unverified: the engine reported no prompt usage.";
        var estimate = work.PromptBytes.GetValueOrDefault() / 4d;
        return estimate > 0 && answer.Usage.TokensIn < estimate / 2
            ? "Input coverage incomplete: reported prompt usage is less than half the UTF-8 / 4 estimate; possible input truncation."
            : "Input coverage unverified: reported usage passes the heuristic, but no tokenizer or explicit non-truncation evidence is available.";
    }
}
