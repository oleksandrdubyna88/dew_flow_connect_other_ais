using System.Collections.Concurrent;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Vibe;

namespace CoaiMcp.Tests;

/// <summary>A Vibe on this "machine": its version, and what one review launch does — a scripted launcher that answers <c>--version</c> as the installed Vibe would and a review launch as Vibe does (shared by the launch-control tests).</summary>
internal sealed class FakeVibe(string version = "2.26.1") : IProcessLauncher
{
    /// <summary>What a Vibe answers on a finished review: one assistant message holding the schema's JSON.</summary>
    public const string Answer = """[{"type":"message","role":"assistant","content":[{"type":"text","text":"{\"findings\": []}"}]}]""";

    /// <summary>The attempt's export folder, as the launch's argv names it.</summary>
    public static string ExportOf(ProcessRequest request) => request.Arguments[request.Arguments.ToList().IndexOf("--output-dir") + 1];

    public ConcurrentQueue<ProcessRequest> Started { get; } = new();

    public Func<ProcessRequest, ProcessResult> Review { get; set; } = request => Answered(request);

    public TimeSpan ReviewTakes { get; set; } = TimeSpan.Zero;

    /// <summary>What the `--version` process does to the home while it runs — nothing, unless a test says so.</summary>
    public Action<ProcessRequest> WhileTheVersionIsAsked { get; set; } = _ => { };

    /// <summary>When set, `--version` cannot even be started: the OS refuses, with this exception.</summary>
    public Exception? VersionThrows { get; set; }

    /// <summary>When set, `--version` starts and prints this instead of a release.</summary>
    public string? VersionPrints { get; set; }

    private int _inReview;

    public int MostAtOnce { get; private set; }

    public IReadOnlyList<ProcessRequest> Reviews => [.. Started.Where(r => !r.Arguments.SequenceEqual(["--version"]))];

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        Started.Enqueue(request);
        if (request.Arguments.SequenceEqual(["--version"]))
        {
            WhileTheVersionIsAsked(request);
            if (VersionThrows is not null)
            {
                throw VersionThrows;
            }

            return new ProcessResult(0, VersionPrints ?? $"python.exe C:\\Users\\p\\.local\\bin\\vibe {version}\r\n", string.Empty, TimedOut: false);
        }

        MostAtOnce = Math.Max(MostAtOnce, Interlocked.Increment(ref _inReview));
        try
        {
            await Task.Delay(ReviewTakes, ct);
            return Review(request);
        }
        finally
        {
            Interlocked.Decrement(ref _inReview);
        }
    }

    /// <summary>What Vibe 2.26.1 does on a finished turn: the export, the model it ran on, the array on stdout.</summary>
    public static ProcessResult Answered(ProcessRequest request, string? ranOn = null, int exit = 0, string outcome = "finished", string usage = """{"input_tokens":6911,"output_tokens":38,"cached_input_tokens":0,"total_tokens":6949}""")
    {
        var export = ExportOf(request);
        Directory.CreateDirectory(export);
        var model = ranOn ?? request.Environment[VibeLaunch.ModelVariable];
        File.WriteAllText(Path.Combine(export, VibeExport.FileName),
            $$$"""{"outcome":"{{{outcome}}}","usage":{{{usage}}},"cost_usd":0.009,"config":{"active_model":"{{{model}}}"}}""");
        // Vibe makes its home when it is not there (§24: a bare start writes vibehistory into it).
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(request.Environment[VibeLaunch.HomeVariable]!).FullName, "vibehistory"), "Hello Vibe!\r\n");

        return new ProcessResult(exit, exit == 0 ? Answer : "[]", exit == 3 ? "Stopped: deadline\r\n" : string.Empty, TimedOut: false);
    }
}
