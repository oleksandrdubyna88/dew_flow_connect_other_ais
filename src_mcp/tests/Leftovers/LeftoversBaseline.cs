using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CoaiMcp.Tests.Leftovers;

/// <summary>
/// The C# guard's second pass for <c>raw-temp</c>: the repository's ONE baseline file (<c>raw-temp-baseline.json</c>, <c>common/test-leftovers-baseline.md</c>) applied to the
/// hits of <see cref="LeftoversGuard.Scan"/> (E5 S6, <c>todo/PLAN_test_leftovers_E5_S6_brief.md</c> § 7.2).
/// </summary>
/// <remarks>
/// <para><b>No port.</b> Two implementations of a ratchet drift apart, and the hash must be the shipped <c>siteSha256</c> (JavaScript's <c>trim</c> and C#'s <c>Trim</c> differ on U+FEFF and U+0085).
/// So this class starts <c>node</c> ONCE, as <see cref="GuardScripts"/> does, and calls the shipped <c>tools/lib/raw-temp-baseline.mjs</c> with the guard name <c>csharp</c>. Nothing is interpolated into the
/// program text: the module URL and the repository go in <c>process.argv</c>, and the request (more than a thousand paths) goes on stdin, because a Windows command line holds 32 KB.</para>
/// <para><b>Fail closed.</b> A missing <c>node</c>, a non-zero exit, a timeout or an answer that is not the expected JSON is ONE finding of class <c>baseline</c> naming the cause. Never an exception and never a pass.</para>
/// </remarks>
internal static class LeftoversBaseline
{
    private const string ModulePath = ".agents/conventions/tools/lib/raw-temp-baseline.mjs";

    private static readonly TimeSpan Bound = TimeSpan.FromMinutes(2);

    private const string Program = """
        import { readFileSync } from "node:fs";
        import { join } from "node:path";
        const [module, repo] = process.argv.slice(1);
        const { BASELINE_FILE, createApplier, loadBaselineAt, locateBaseline, siteSha256 } = await import(module);
        let input = "";
        for await (const chunk of process.stdin) input += chunk;
        const request = JSON.parse(input);
        const lines = new Map();
        const lineOf = (file, number) => {
          if (!lines.has(file)) lines.set(file, readFileSync(join(repo, file), "utf8").split(/\r?\n/));
          return lines.get(file)[number - 1];
        };
        let answer = { left: request.hits, findings: [] };
        if (!locateBaseline(repo, { gitRoot: true }).absent) {
          const applier = createApplier(loadBaselineAt(repo), repo, BASELINE_FILE, new Set(request.discovered), "csharp");
          const left = request.hits.filter((hit) => !applier.take(hit.file, siteSha256(lineOf(hit.file, hit.line))));
          answer = { left, findings: applier.finalFindings() };
        }
        answer.hashes = (request.sites ?? []).map((site) => siteSha256(lineOf(site.file, site.line) ?? ""));
        console.log(JSON.stringify(answer));
        """;

    private sealed record Place(string File, int Line);

    private sealed record Request(IReadOnlyList<string> Discovered, IReadOnlyList<Place> Hits, IReadOnlyList<Place> Sites);

    private sealed record Answer(IReadOnlyList<Place> Left, IReadOnlyList<Reported> Findings, IReadOnlyList<string> Hashes);

    private sealed record Reported(string File, int Line, string Text);

    private sealed record Outcome(Answer? Answer, string? Failure);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// The findings of <paramref name="report"/> after the baseline: every finding that is not <c>raw-temp</c>, every <c>raw-temp</c> hit the baseline did not pass (as the original finding), and one
    /// <c>baseline</c> finding for each thing the shipped second pass reports. With no baseline file every hit is left. <paramref name="repoRoot"/> must be the root of its git repository.
    /// </summary>
    internal static IReadOnlyList<Finding> Apply(GuardReport report, string repoRoot, string mountRoot, string node = "node")
    {
        var hits = report.Findings.Where(f => f.Class == "raw-temp").OrderBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line).ToList();
        var others = report.Findings.Where(f => f.Class != "raw-temp").ToList();
        var discovered = GuardFiles.Discover(repoRoot).Select(file => file.Relative).ToList();
        var outcome = Ask(repoRoot, mountRoot, node, new Request(discovered, [.. hits.Select(h => new Place(h.File, h.Line))], []));
        if (outcome.Answer is not { } answer)
        {
            return [.. others, .. hits, new Finding(RepoFile, 1, "baseline", outcome.Failure ?? "no answer")];
        }

        var left = answer.Left.Select(place => hits.First(h => h.File == place.File && h.Line == place.Line));

        return [.. others, .. left, .. answer.Findings.Select(f => new Finding(f.File, f.Line, "baseline", f.Text))];
    }

    private const string RepoFile = "raw-temp-baseline.json";

    /// <summary>The shipped <c>siteSha256</c> of each (file, line), in order. Never hashed in C#. A failure of <c>node</c> is an <see cref="InvalidOperationException"/>: the comparer that asks cannot judge.</summary>
    internal static IReadOnlyList<string> SiteHashes(string repoRoot, string mountRoot, IReadOnlyList<(string File, int Line)> sites, string node = "node")
    {
        if (sites.Count == 0)
        {
            return [];
        }

        var outcome = Ask(repoRoot, mountRoot, node, new Request([], [], [.. sites.Select(s => new Place(s.File, s.Line))]));

        return outcome.Answer is { } answer && answer.Hashes.Count == sites.Count
            ? answer.Hashes
            : throw new InvalidOperationException("the site hashes could not be read: " + (outcome.Failure ?? "the answer holds another number of hashes than sites"));
    }

    private static Outcome Ask(string repoRoot, string mountRoot, string node, Request request)
    {
        try
        {
            var module = new Uri(Path.GetFullPath(Path.Combine(mountRoot, ModulePath))).AbsoluteUri;
            var start = new ProcessStartInfo(node) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--input-type=module", "-e", Program, "--", module, repoRoot })
            {
                start.ArgumentList.Add(argument);
            }

            return Execute(start, node, JsonSerializer.Serialize(request, Json));
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or JsonException)
        {
            return new Outcome(null, $"{node} could not be started: {ex.Message}");
        }
    }

    private static Outcome Execute(ProcessStartInfo start, string node, string input)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("the process did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // node ended before it read the request: its exit code and error text below say why.
        }

        if (!process.WaitForExit(Bound))
        {
            process.Kill(entireProcessTree: true);

            return new Outcome(null, $"{node} did not finish in two minutes");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return new Outcome(null, $"{node} exited {process.ExitCode}: {GuardText.FirstLine(errors.GetAwaiter().GetResult())}");
        }

        return Parse(output.GetAwaiter().GetResult());
    }

    private static Outcome Parse(string text)
    {
        try
        {
            var answer = JsonSerializer.Deserialize<Answer>(text, Json);

            return answer is { Left: not null, Findings: not null } ? new Outcome(answer with { Hashes = answer.Hashes ?? [] }, null) : new Outcome(null, "node answered JSON that is not the expected shape");
        }
        catch (JsonException ex)
        {
            return new Outcome(null, $"node answered text that is not JSON: {ex.Message}");
        }
    }
}
