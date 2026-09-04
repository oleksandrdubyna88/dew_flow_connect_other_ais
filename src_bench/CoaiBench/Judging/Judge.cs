using System.Diagnostics;
using System.Text.Json.Nodes;
using CoaiBench.Model;

namespace CoaiBench.Judging;

/// <summary>
/// The second pass: which findings were worth having.
/// </summary>
/// <remarks>
/// <para><b>Separate from the run on purpose.</b> Counting findings ranks noise above insight — read
/// one at a time against the code they name, the ranking by count inverted outright
/// (<c>research/RESULTS_findings_that_are_worth_something.md</c>). So the bench records everything
/// and says nothing about worth, and this decides afterwards, over data that is already on disk. A
/// judgement that changes can be re-run without paying for the rounds again.</para>
/// <para>The judge is Fable through the Claude Code CLI — the same way the product drives a Claude
/// reviewer, and a model that did not write the findings and cannot see the conversation that
/// produced them. It is given the finding and the FILE it names, because a judgement made without
/// reading the code is the counting this exists to replace.</para>
/// </remarks>
public sealed class Judge(string executable, string model, string repo)
{
    private const string Question = """
        You are judging review findings, one at a time. For this finding, answer whether it was WORTH
        HAVING for the person who has to act on it.

        Answer "yes" when it names something real that a careful engineer would want changed or would
        want to know: a defect, a risk, a rule this code actually breaks, a genuine simplification.

        Answer "no" when it is true but not worth anyone's attention, when it restates what the code
        already says, when it asks for machinery out of proportion to the risk, or when it is simply
        wrong about this code.

        Read the file before deciding. Reply with JSON only:
        {"useful": "yes" | "no", "verdict": "<one sentence saying why>"}
        """;

    public async Task<RunRecord> JudgeAsync(RunRecord run, CancellationToken ct)
    {
        var stages = new List<StageResult>();
        foreach (var stage in run.Stages)
        {
            var findings = new List<Finding>();
            foreach (var finding in stage.Findings)
            {
                findings.Add(await OneAsync(finding, ct));
            }

            stages.Add(stage with { Findings = findings });
        }

        return run with { Stages = stages };
    }

    private async Task<Finding> OneAsync(Finding finding, CancellationToken ct)
    {
        var asked = $"""
            {Question}

            ## The finding

            severity: {finding.Severity}
            category: {finding.Category}
            file: {finding.File}:{finding.Line}
            title: {finding.Title}
            why: {finding.Why}
            fix: {finding.Fix}
            """;

        var answer = await AskAsync(asked, ct);

        // An unreadable answer leaves the finding UNJUDGED rather than counting it either way. A
        // judge that failed is not a judge that said no.
        return answer is null
            ? finding
            : finding with
            {
                Useful = answer["useful"]?.GetValue<string>() ?? "unjudged",
                Verdict = answer["verdict"]?.GetValue<string>() ?? string.Empty,
            };
    }

    private async Task<JsonObject?> AskAsync(string prompt, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repo,
        };
        foreach (var argument in (string[])
            ["-p", "--output-format", "json", "--permission-mode", "plan",
             "--disallowedTools", "Edit", "Write", "NotebookEdit", "--model", model])
        {
            start.ArgumentList.Add(argument);
        }

        using var judge = Process.Start(start);
        if (judge is null)
        {
            return null;
        }

        // On stdin, because a prompt on a Windows command line is truncated by the shell — a trap
        // this family has already paid for once.
        await judge.StandardInput.WriteAsync(prompt.AsMemory(), ct);
        judge.StandardInput.Close();
        var text = await judge.StandardOutput.ReadToEndAsync(ct);
        await judge.WaitForExitAsync(ct);

        return judge.ExitCode == 0 ? Unwrap(text) : null;
    }

    /// <summary>The CLI wraps the answer in an envelope; the answer itself is in `result`.</summary>
    internal static JsonObject? Unwrap(string text)
    {
        try
        {
            var envelope = JsonNode.Parse(text) as JsonObject;
            var inner = envelope?["result"]?.GetValue<string>() ?? text;
            var start = inner.IndexOf('{', StringComparison.Ordinal);
            var end = inner.LastIndexOf('}');

            return start >= 0 && end > start
                ? JsonNode.Parse(inner[start..(end + 1)]) as JsonObject
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
