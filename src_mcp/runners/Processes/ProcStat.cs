namespace CoaiMcp.Runners.Processes;

/// <summary>The four facts of one <c>/proc/&lt;pid&gt;/stat</c> line that anything here reads: the process, its name (<c>comm</c>), its
/// state letter and its parent.</summary>
/// <remarks>
/// <c>comm</c> is the KERNEL's name for the thread group — at most 15 characters, and not the program's file name: a running <c>kimi acp</c>
/// reads <c>MainThread</c> (L-K6 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>), which is why a caller that must recognise a program matches
/// the <c>exe</c> link instead. <see cref="State"/> is one letter; <c>Z</c> (a zombie) and <c>X</c> (being removed) mean the process has exited.
/// </remarks>
internal sealed record ProcStat(int Pid, string Comm, string State, int Parent);

/// <summary>What reading one process's <c>stat</c> produced — a closed union, so "it is gone" and "it could not be read" are never one answer.</summary>
/// <remarks>
/// A caller that kills treats both non-<see cref="Read"/> answers as "nothing to do"; a caller that CLEARS something because a process is gone
/// must not: only <see cref="Gone"/> is evidence of that (<c>ENOENT</c>), and an <c>EACCES</c>, an <c>EIO</c> or a line it cannot parse is
/// not evidence of anything (DL9 and DL10 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>).
/// </remarks>
internal abstract record ProcStatReading
{
    private ProcStatReading()
    {
    }

    /// <summary>The line was read and understood.</summary>
    public sealed record Read(ProcStat Stat) : ProcStatReading;

    /// <summary>No such process: <c>/proc/&lt;pid&gt;</c> is not there (the process ended before the read).</summary>
    public sealed record Gone : ProcStatReading;

    /// <summary>The file is there and could not be read or understood — and why.</summary>
    public sealed record Unreadable(string Reason) : ProcStatReading;
}

/// <summary>
/// The ONE reader of a Linux <c>/proc/&lt;pid&gt;/stat</c> line: the process tree's parent walk and its exited-entry rule read it, and the
/// foreign-Kimi listing reads it (DL10) — extracted from <c>ProcessTree</c> so a second parser is never written.
/// </summary>
internal static class ProcStatReader
{
    /// <summary>The <c>stat</c> of process <paramref name="pid"/> under <c>/proc</c>.</summary>
    public static ProcStatReading Read(int pid) => ReadFile($"/proc/{pid}/stat");

    /// <summary>The <c>stat</c> file at <paramref name="statPath"/>: gone when the file or its folder is not there, unreadable for any other failure.</summary>
    public static ProcStatReading ReadFile(string statPath)
    {
        try
        {
            return Parse(File.ReadAllText(statPath));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ProcStatReading.Gone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ProcStatReading.Unreadable($"{statPath} could not be read: {e.Message}");
        }
    }

    /// <summary>One line: <c>pid (comm) state parent …</c>.</summary>
    /// <remarks>
    /// The name may itself hold spaces and brackets — even a <c>) S 5 (</c> that looks like the fields after it — so it is the text between the
    /// FIRST opening bracket and the LAST closing one, and the fields are counted from that last bracket.
    /// </remarks>
    public static ProcStatReading Parse(string line)
    {
        var open = line.IndexOf('(');
        var close = line.LastIndexOf(')');

        return open >= 1 && close >= open && int.TryParse(line.AsSpan(0, open).Trim(), out var pid) && AfterTheName(line[(close + 1)..]) is [var state, var parent]
            ? new ProcStatReading.Read(new ProcStat(pid, line[(open + 1)..close], state, int.Parse(parent)))
            : Malformed(line);
    }

    /// <summary>The state letter and the parent pid that follow the bracketed name — or nothing when they are not there.</summary>
    private static string[] AfterTheName(string rest) =>
        rest.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [{ Length: 1 } state, var parent, ..] && int.TryParse(parent, out _) ? [state, parent] : [];

    private static ProcStatReading.Unreadable Malformed(string line) =>
        new($"not a /proc stat line: {(line.Length > 80 ? line[..80] + "…" : line)}");
}
