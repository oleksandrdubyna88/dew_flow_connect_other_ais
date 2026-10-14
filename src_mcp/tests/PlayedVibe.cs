using System.Collections.Concurrent;
using System.Text.Json;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Tests;

/// <summary>
/// The <c>vibe</c> CLI as a test plays it, in process: every launch of <see cref="Exe"/> is answered here — <c>--version</c>
/// with a measured version, a turn with an <c>--output json</c> array and an export in its <c>--output-dir</c> — and every
/// other process (git) runs for real. What each turn was given, and what its working folder held, is kept.
/// </summary>
/// <remarks>
/// Shared by the consult and the question-row scenarios of S3 (<c>todo/PLAN_vibe_and_kimi_runtimes.md</c>), so no turn is
/// paid for and both read the same shapes the reviewer's fake reads (research/RESULTS_vibe_confinement.md: the stdout array,
/// the export's <c>outcome</c>, <c>usage</c> and <c>config.active_model</c>).
/// </remarks>
internal sealed class PlayedVibe(IProcessLauncher inner, string answer) : IProcessLauncher
{
    public const string Exe = @"C:\played\vibe.exe";

    public const string Session = "5b1e4c2a-1111-2222-3333-444455556666";

    /// <summary>One turn: its request, and every file under its working folder (relative, forward slashes) with its text.</summary>
    public sealed record Seen(ProcessRequest Request, IReadOnlyDictionary<string, string> Files);

    public ConcurrentQueue<Seen> Turns { get; } = new();

    /// <summary>Every <c>--version</c> request — its environment names the home the turn would run on.</summary>
    public ConcurrentQueue<ProcessRequest> Versions { get; } = new();

    /// <summary>The exit code <c>--version</c> answers with — non-zero is a Vibe the launch control refuses.</summary>
    public int VersionExit { get; init; }

    /// <summary>The answer to a turn, from what it was given — by default the one answer the stand-in was made with.</summary>
    public Func<Seen, string> AnswerFor { get; init; } = _ => answer;

    /// <summary>Runs during a turn, after it is recorded — a test's seam for a cancellation in flight.</summary>
    public Func<CancellationToken, Task> During { get; init; } = static _ => Task.CompletedTask;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        if (!string.Equals(request.Executable, Exe, StringComparison.Ordinal))
        {
            return await inner.RunAsync(request, ct);
        }

        if (request.Arguments is ["--version"])
        {
            Versions.Enqueue(request);

            return new ProcessResult(VersionExit, VersionExit == 0 ? "python.exe C:\\Users\\p\\.local\\bin\\vibe 2.26.1\r\n" : string.Empty, string.Empty, TimedOut: false);
        }

        var seen = new Seen(request, FilesUnder(request.WorkingDirectory));
        Turns.Enqueue(seen);
        await During(ct);
        await ExportAsync(request, ct);

        return new ProcessResult(0, JsonSerializer.Serialize(new object[]
        {
            new Dictionary<string, object> { ["role"] = "user", ["content"] = "q", ["sessionId"] = Session },
            new Dictionary<string, object> { ["role"] = "assistant", ["content"] = AnswerFor(seen), ["sessionId"] = Session },
        }), string.Empty, TimedOut: false);
    }

    private static async Task ExportAsync(ProcessRequest request, CancellationToken ct)
    {
        var export = request.Arguments[request.Arguments.ToList().IndexOf("--output-dir") + 1];
        Directory.CreateDirectory(export);
        await File.WriteAllTextAsync(Path.Combine(export, VibeExport.FileName), JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["outcome"] = "finished",
            ["session_id"] = Session,
            ["usage"] = new Dictionary<string, long> { ["input_tokens"] = 1200, ["output_tokens"] = 34, ["cached_input_tokens"] = 0 },
            ["config"] = new Dictionary<string, string> { ["active_model"] = request.Environment[VibeLaunch.ModelVariable]! },
        }), ct);
    }

    private static Dictionary<string, string> FilesUnder(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllText, StringComparer.Ordinal);
}
