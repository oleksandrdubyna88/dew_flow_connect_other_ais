using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Files;

/// <summary>
/// What <see cref="AtomicFile"/> asks of the world — the pause between two tries of the move, the clock, the liveness question about an orphan temp file's writer and
/// the moment between the temp file and the move. A seam for the tests, so a held file is retried without a real sleep and a dead writer is
/// a fact a test states instead of one it has to wait for.
/// </summary>
internal sealed record AtomicSeams
{
    /// <summary>The seams production runs with.</summary>
    public static AtomicSeams Real { get; } = new();

    /// <summary>The clock the retry window is measured on (its monotonic timestamp) and an orphan temp file's age is read from.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Waits between two tries of the move; the argument is the number of tries made so far.</summary>
    public Action<int> Pause { get; init; } = static _ => Thread.Sleep(Random.Shared.Next(10, 40));

    /// <summary>The three-answer check an orphan is judged by: only <see cref="Liveness.Gone"/> is proof the writer left; a pid that could not be asked about (<c>Win32Exception</c>: not ours to open) proves nothing. A pid in another Linux pid namespace is invisible and answers Gone (see <see cref="ProcessLiveness.Check"/>).</summary>
    public Func<int, Liveness> LivenessOf { get; init; } = ProcessLiveness.Check;

    /// <summary>Writes the temp file: path, text. A seam so a test can fail the write half way (a full disk).</summary>
    public Action<string, string> WriteText { get; init; } = File.WriteAllText;

    /// <summary>Called with the temp file's path after it is written and before the move — the place a test pauses a writer.</summary>
    public Action<string> AfterTemp { get; init; } = static _ => { };
}
