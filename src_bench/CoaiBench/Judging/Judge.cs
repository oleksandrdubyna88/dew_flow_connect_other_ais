using System.Diagnostics;
using System.Text.Json.Nodes;
using CoaiBench.Model;
using CoaiBench.Running;

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
/// <para>The judge is Fable through the Claude Code CLI — a model that did not write the findings and
/// cannot see the conversation that produced them. <b>It is handed the code, at the commit that was
/// reviewed.</b> The first version handed it a path and said "read the file before deciding", and
/// two things went wrong at once: every judgement became an agentic session — two minutes and more
/// per finding, four hours for one campaign — and the path was read from the working tree, which had
/// moved on since the round (the sidebar file one campaign's findings name was rewritten twice that
/// day). Now the file is taken from the reviewed commit with <c>git show</c>, windowed around the
/// cited line, and put INTO the prompt: one turn, no tools, a judgement about what the reviewer
/// actually saw.</para>
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

        The code is below, exactly as it was when the finding was made. Reply with JSON only:
        {"useful": "yes" | "no", "verdict": "<one sentence saying why>"}
        """;

    /// <summary>How many lines either side of the cited line the judge is shown.</summary>
    private const int Radius = 80;

    public async Task<RunRecord> JudgeAsync(RunRecord run, CancellationToken ct)
    {
        var stages = new List<StageResult>();
        foreach (var stage in run.Stages)
        {
            var findings = new List<Finding>();
            foreach (var finding in stage.Findings)
            {
                findings.Add(await OneAsync(run.Case, finding, ct));
            }

            stages.Add(stage with { Findings = findings });
        }

        return run with { Stages = stages, JudgedBy = model };
    }

    private async Task<Finding> OneAsync(Case work, Finding finding, CancellationToken ct)
    {
        var source = await SourceAsync(work, finding.File, ct);
        var answer = await AskAsync(PromptFor(finding, Window(source, finding.Line, Radius), work.Commit), ct);

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

    /// <summary>
    /// The file a finding names, as the reviewer saw it.
    /// </summary>
    /// <remarks>
    /// The plan text was read from the WORKING TREE when the round ran, so a finding about the plan
    /// file is judged against the working tree too; everything else comes from the reviewed commit.
    /// A file that cannot be produced — deleted since, or a path the model invented — gives an empty
    /// source, and the prompt says so rather than pretending.
    /// </remarks>
    private async Task<string> SourceAsync(Case work, string file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return string.Empty;
        }

        var normalised = file.Replace('\\', '/').TrimStart('.', '/');
        if (work.Commit.Length == 0 || normalised.Equals(work.PlanFile.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            var path = Path.Combine(repo, normalised);
            return File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : string.Empty;
        }

        var (exit, output) = await Git.RunAsync(repo, ["show", $"{work.Commit}:{normalised}"], ct);
        return exit == 0 ? output : string.Empty;
    }

    /// <summary>
    /// The lines around the cited one, numbered so the judge can point back — or the whole file when
    /// it is short or no line was cited, capped so a long file is never pasted entire.
    /// </summary>
    internal static string Window(string source, int line, int radius)
    {
        if (source.Length == 0)
        {
            return string.Empty;
        }

        var lines = source.Replace("\r\n", "\n").Split('\n');
        var (from, to) = line <= 0
            ? (1, Math.Min(lines.Length, 2 * radius + 1))
            : (Math.Max(1, line - radius), Math.Min(lines.Length, line + radius));

        // A reviewer may cite a line the file does not have - a stale number, a hallucinated one, a
        // path that resolved to a different file than the one it was reading. Then `from` runs past
        // `to`, the count below goes negative, and this threw - taking the whole judgement down with
        // it on its fourteenth run of fourteen. There is simply nothing to show; the judge gets no
        // window and reads the finding on its own words.
        if (from > to)
        {
            return string.Empty;
        }

        return string.Join("\n", Enumerable.Range(from, to - from + 1).Select(n => $"{n}: {lines[n - 1]}"));
    }

    /// <summary>The whole question: the rule, the code as reviewed, the finding.</summary>
    internal static string PromptFor(Finding finding, string code, string commit)
    {
        var where = finding.File.Length == 0
            ? "This finding names no file; judge it on its own words."
            : code.Length == 0
                ? $"The file `{finding.File}` could not be produced at the reviewed commit — judge the finding on its own words, and say so."
                : $"## The code, as reviewed{(commit.Length > 0 ? $" at commit {commit}" : "")} — `{finding.File}`\n\n```\n{code}\n```";

        return $"""
            {Question}

            {where}

            ## The finding

            severity: {finding.Severity}
            category: {finding.Category}
            file: {finding.File}:{finding.Line}
            title: {finding.Title}
            why: {finding.Why}
            fix: {finding.Fix}
            """;
    }

    /// <summary>One turn, no editing tools, the model asked for. The code is in the prompt; there is nothing to fetch.</summary>
    internal static string[] Arguments(string model) =>
    [
        "-p", "--output-format", "json", "--permission-mode", "plan", "--max-turns", "1",
        "--disallowedTools", "Edit", "Write", "NotebookEdit", "--model", model,
    ];

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
        foreach (var argument in Arguments(model))
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
