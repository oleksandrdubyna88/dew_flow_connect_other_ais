namespace CoaiMcp.Runners.Reviewers;

/// <summary>A usage ratio can detect suspicious truncation, never prove that an engine read everything.</summary>
public static class InputCoverage
{
    public static string Of(ReviewerWork work, ReviewerOutcome.Ok answer)
    {
        if (!work.Invocation.IsOnEngine) return string.Empty;
        if (ReportedNoUsage(answer.Usage)) return "Input coverage unverified: the engine reported no prompt usage.";
        return LooksTruncated(work, answer.Usage)
            ? "Input coverage incomplete: reported prompt usage is less than half the UTF-8 / 4 estimate; possible input truncation."
            : "Input coverage unverified: reported usage passes the heuristic, but no tokenizer or explicit non-truncation evidence is available.";
    }

    private static bool ReportedNoUsage(Core.Findings.Usage usage) => usage.NotCaptured || usage.TokensIn <= 0;

    /// <summary>Under half the UTF-8 / 4 estimate of the prompt; a prompt of unknown size has no estimate to fall short of.</summary>
    private static bool LooksTruncated(ReviewerWork work, Core.Findings.Usage usage)
    {
        var estimate = work.PromptBytes.GetValueOrDefault() / 4d;
        return estimate > 0 && usage.TokensIn < estimate / 2;
    }
}
