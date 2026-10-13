using System.Globalization;

namespace CoaiMcp.Tests;

/// <summary>
/// One arm of the security-lane effort measurement (epic 2 of research/PLAN_local_effort_reaches_every_local_launch.md): the
/// <c>reasoning_effort</c> and ceiling every local security reviewer of the arm is launched with, how many repeats, and
/// which fixture — the planted regression, or its safe twin. Read from the environment, refused out of range.
/// </summary>
/// <remarks>
/// The defaults are today's product (<c>none</c>, 8 192) on the unsafe fixture with three repeats, so a run that sets
/// nothing reproduces the recorded calibration (research/RESULTS_security_lane_local_llm_windows.md).
/// </remarks>
internal sealed record SecurityLaneEffortArm(string Effort, int MaxTokens, int Repeats, bool Safe)
{
    /// <summary>The levels the local engine's OpenAI route accepts as <c>reasoning_effort</c>.</summary>
    public static readonly IReadOnlyList<string> Efforts = ["none", "low", "medium", "high"];

    private const int MostTokens = 131072;
    private const int MostRepeats = 10;

    /// <summary>The arm's directory-safe name: effort, ceiling, fixture.</summary>
    public string Name => $"{Effort}-{MaxTokens.ToString(CultureInfo.InvariantCulture)}-{(Safe ? "safe" : "unsafe")}";

    /// <exception cref="ArgumentException">An input outside its range, naming the variable and what is legal.</exception>
    public static SecurityLaneEffortArm From(Func<string, string?> env) => new(
        OneOf(env, "COAI_SECURITY_CALIBRATION_EFFORT", "none", Efforts),
        Count(env, "COAI_SECURITY_CALIBRATION_MAX_TOKENS", 8192, MostTokens),
        Count(env, "COAI_SECURITY_CALIBRATION_REPEATS", 3, MostRepeats),
        OneOf(env, "COAI_SECURITY_CALIBRATION_FIXTURE", "unsafe", ["safe", "unsafe"]) == "safe");

    /// <summary>
    /// Each recorded <c>--ask-local</c> argv whose effort or ceiling is not this arm's, as a sentence — or none. No argv at
    /// all is a mismatch too: an arm with no cell measured nothing.
    /// </summary>
    public IReadOnlyList<string> Mismatches(IReadOnlyList<IReadOnlyList<string>> argvs) =>
        argvs.Count == 0
            ? ["no --ask-local launch was recorded"]
            : [.. argvs.SelectMany((argv, at) => CellMismatches(argv, at + 1))];

    /// <summary>
    /// Every recorded launch that failed for a reason other than a cut at the ceiling — the one failure a measurement of
    /// effort records as an observation. An unreachable engine, a timeout, a crash or an empty answer fails the cell, so it
    /// can never pass for an effort-related cut (the code round of epic 2).
    /// </summary>
    public static IReadOnlyList<string> UnexpectedFailures(IReadOnlyList<Runners.Processes.ProcessResult> launches) =>
        [.. launches.Select((launch, at) => Unexpected(launch, at + 1)).Where(said => said.Length > 0)];

    /// <summary>How many reviewers the round's own line says did not answer — or -1 when the line cannot be read.</summary>
    /// <remarks>A line naming a different number of reviewers than <paramref name="asked"/> is -1 too: a round that never
    /// asked one of them is not a clean cell, whatever it says about the ones it did ask (CodeRabbit on #800).</remarks>
    public static int NotAnswered(string reviewersLine, int asked) => Counted.Match(reviewersLine) switch
    {
        { Success: false } => -1,
        var line when int.Parse(line.Groups["asked"].Value, CultureInfo.InvariantCulture) != asked => -1,
        var all when all.Groups["all"].Success => 0,
        var some => asked - int.Parse(some.Groups["answered"].Value, CultureInfo.InvariantCulture),
    };

    /// <summary>The round's own reviewer line (<c>SessionState</c>): "all N reviewers answered" or "A of N reviewers answered".</summary>
    private static readonly System.Text.RegularExpressions.Regex Counted = new(
        @"^(?:(?<all>all) (?<asked>\d+)|(?<answered>\d+) of (?<asked>\d+)) reviewers answered",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>The sentence the shim writes for a cut (epic 1, <c>CompletionReader.CutSentence</c>).</summary>
    private const string CutMarker = "was cut at the token limit";

    private static string Unexpected(Runners.Processes.ProcessResult launch, int cell) => launch switch
    {
        { TimedOut: true } => $"cell {cell} timed out",
        { ExitCode: 0 } => string.Empty,
        { ExitCode: 70 } when launch.StdErr.Contains(CutMarker, StringComparison.Ordinal) => string.Empty,
        _ => $"cell {cell} exited {launch.ExitCode}: {launch.StdErr.Trim()}",
    };

    private IEnumerable<string> CellMismatches(IReadOnlyList<string> argv, int cell)
    {
        var effort = ValueAfter(argv, "--reasoning-effort");
        if (effort != Effort)
        {
            yield return effort is null
                ? $"cell {cell} sent no --reasoning-effort, the arm is {Effort}"
                : $"cell {cell} sent --reasoning-effort {effort}, the arm is {Effort}";
        }

        var ceiling = ValueAfter(argv, "--max-tokens");
        if (ceiling != MaxTokens.ToString(CultureInfo.InvariantCulture))
        {
            yield return $"cell {cell} sent --max-tokens {ceiling ?? "nothing"}, the arm is {MaxTokens}";
        }
    }

    private static string? ValueAfter(IReadOnlyList<string> argv, string flag)
    {
        var at = argv.ToList().IndexOf(flag);

        return at >= 0 && at + 1 < argv.Count ? argv[at + 1] : null;
    }

    private static string OneOf(Func<string, string?> env, string name, string fallback, IReadOnlyList<string> legal)
    {
        var value = env(name)?.Trim().ToLowerInvariant() is { Length: > 0 } set ? set : fallback;

        return legal.Contains(value)
            ? value
            : throw new ArgumentException($"{name}={value} is not one of {string.Join(", ", legal)}");
    }

    private static int Count(Func<string, string?> env, string name, int fallback, int most)
    {
        var raw = env(name)?.Trim() ?? string.Empty;
        var value = raw.Length == 0 ? fallback : int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

        return value is >= 1 && value <= most
            ? value
            : throw new ArgumentException($"{name}={raw} is not a whole number from 1 to {most}");
    }
}
