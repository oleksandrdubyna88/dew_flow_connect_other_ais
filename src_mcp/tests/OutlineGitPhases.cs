using System.Collections.Concurrent;
using System.Diagnostics;
using CoaiMcp.Core.Outlining;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Tests;

/// <summary>
/// The real launcher, with every git process the outline build starts written down WITH ITS WALL TIME — so a build's cost can be reported by
/// phase instead of as one number (E5 S11, C36).
/// </summary>
/// <remarks>
/// The builder starts a fixed handful of git processes — the empty tree, ONE <c>diff --numstat</c> (the listing), ONE <c>cat-file --batch-check</c>
/// (the sizes), ONE <c>cat-file --batch</c> (the read) — and does the rest in-process (parse, redact, outline, compose). The decorator needs no hook in the
/// product: what the launcher was asked, and for how long, is the phase split. Sequential by construction (the builder awaits each process).
/// </remarks>
internal sealed class OutlineGitPhases(IProcessLauncher inner) : IProcessLauncher
{
    private readonly ConcurrentQueue<GitProcess> _processes = new();

    public IReadOnlyList<GitProcess> Processes => [.. _processes];

    /// <summary>The characters the ONE batch read answered — the content that was redacted and outlined, headers included.</summary>
    public int CharsRead => Processes.Where(p => p.Phase == "read").Sum(p => p.StdOutChars);

    /// <summary>How many files the ONE batch read was asked for — the count of names on its stdin.</summary>
    public int FilesRead => Processes.Where(p => p.Phase == "read").Sum(p => p.StdInLines);

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await inner.RunAsync(request, ct);
        _processes.Enqueue(new GitProcess(PhaseOf(request.Arguments), Stopwatch.GetElapsedTime(started), request.StdIn.Count(c => c == '\n'), result.StdOut.Length));

        return result;
    }

    /// <summary>The phase a git invocation belongs to, named by what it does rather than by its position.</summary>
    internal static string PhaseOf(IReadOnlyList<string> args) => args switch
    {
        ["hash-object", ..] => "empty-tree",
        ["diff", "--numstat", ..] => "list",
        ["cat-file", "--batch-check", ..] => "size",
        ["cat-file", "--batch", ..] => "read",
        _ => string.Join(' ', args.Take(2)),
    };

    /// <summary>
    /// One line: the whole build's wall time split into the git phases the builder launches and what is left — the in-process outlining and composing — and the
    /// number of git processes, which is what says whether anything here is per file.
    /// </summary>
    internal string Describe(TimeSpan total, TimedOutliner outliner)
    {
        var byPhase = Processes.GroupBy(p => p.Phase).Select(g => $"{g.Key} {(long)g.Sum(p => p.Took.TotalMilliseconds)} ms").ToList();
        var inGit = Processes.Sum(p => p.Took.TotalMilliseconds);
        var rest = Math.Max(0, total.TotalMilliseconds - inGit - outliner.Took.TotalMilliseconds);

        return $"phases: git [{string.Join(", ", byPhase)}] = {(long)inGit} ms over {Processes.Count} process(es), the read answering {CharsRead} chars; "
            + $"tree-sitter outlining {(long)outliner.Took.TotalMilliseconds} ms over {outliner.Calls} file(s); "
            + $"the rest in-process (numstat parse, plan, redaction, composing) {(long)rest} ms; whole {(long)total.TotalMilliseconds} ms";
    }
}

/// <summary>The real outliner, with the time of its <c>Outline</c> calls and their number written down — the in-process half of the phase split.</summary>
internal sealed class TimedOutliner(ISourceOutliner inner) : ISourceOutliner
{
    private long _ticks;
    private int _calls;

    public TimeSpan Took => Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _ticks));

    public int Calls => _calls;

    public OutlineLanguage LanguageOf(string path) => inner.LanguageOf(path);

    public SourceOutline Outline(OutlineLanguage language, string source)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return inner.Outline(language, source);
        }
        finally
        {
            Interlocked.Add(ref _ticks, Stopwatch.GetTimestamp() - started);
            Interlocked.Increment(ref _calls);
        }
    }
}

/// <summary>One git process the build started: its phase, its wall time, how many lines it was fed and how many characters it answered.</summary>
internal sealed record GitProcess(string Phase, TimeSpan Took, int StdInLines, int StdOutChars);
