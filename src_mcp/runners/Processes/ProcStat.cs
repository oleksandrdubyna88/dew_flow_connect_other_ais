namespace CoaiMcp.Runners.Processes;

/// <summary>The four facts of one <c>/proc/&lt;pid&gt;/stat</c> line that anything here reads: the process, its name (<c>comm</c>), its state letter and its parent.</summary>
/// <remarks>
/// <c>comm</c> is the KERNEL's name for the thread group — at most 15 characters, and not the program's file name: a running <c>kimi acp</c> reads
/// <c>MainThread</c> (L-K6 of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c>), which is why a caller that must recognise a program matches the <c>exe</c> link
/// instead. <see cref="State"/> is one letter; <c>Z</c> (a zombie) and <c>X</c> (being removed) mean the process has exited.
/// </remarks>
public sealed record ProcStatLine(int Pid, string Comm, string State, int ParentPid);

/// <summary>What reading one process's <c>stat</c> produced — a closed union, so "it is gone" and "it could not be read" are never one answer.</summary>
/// <remarks>
/// A caller that kills treats both non-<see cref="Read"/> answers as "nothing to do"; a caller that CLEARS something because a process is gone must not:
/// only <see cref="Gone"/> is evidence of that, and an <c>EACCES</c>, an <c>EIO</c> or a line it cannot parse is not evidence of anything (DL9 and DL10).
/// </remarks>
public abstract record ProcStatRead
{
    private ProcStatRead()
    {
    }

    /// <summary>The line was read and understood.</summary>
    public sealed record Read(ProcStatLine Line) : ProcStatRead;

    /// <summary><c>ENOENT</c>: the process left between the listing and the read — never evidence of anything.</summary>
    public sealed record Gone : ProcStatRead;

    /// <summary>Any other failure, its type named: <c>EACCES</c>, <c>EIO</c>, an unparseable line.</summary>
    public sealed record Unreadable(string Reason) : ProcStatRead;
}

/// <summary>
/// The ONE reader of a Linux <c>/proc/&lt;pid&gt;/stat</c> line: the process tree's parent walk and its exited-entry rule read it, and the foreign-Kimi
/// listing and the liveness check will — extracted from <c>ProcessTree</c> so a second parser is never written.
/// </summary>
public static class ProcStat
{
    /// <summary>Pure: one line, <c>pid (comm) state parent …</c>.</summary>
    /// <remarks>
    /// The name may itself hold spaces and brackets — even a <c>) S 5 (</c> that looks like the fields after it — so <c>comm</c> is the text between the FIRST
    /// opening bracket and the LAST closing one, and the fields are counted from that last bracket. The pid is the caller's: it names the directory the line was read from.
    /// </remarks>
    public static ProcStatRead Parse(int pid, string text)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');

        return open >= 1 && close >= open && AfterTheName(text[(close + 1)..]) is [var state, var parent]
            ? new ProcStatRead.Read(new ProcStatLine(pid, text[(open + 1)..close], state, int.Parse(parent)))
            : Malformed(text);
    }

    /// <summary>The <c>stat</c> of process <paramref name="pid"/> through <paramref name="reads"/>: gone when the file or its folder is not there, unreadable for any other failure.</summary>
    public static ProcStatRead ReadFor(int pid, IProcReads reads)
    {
        try
        {
            return Parse(pid, reads.StatText(pid));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ProcStatRead.Gone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new ProcStatRead.Unreadable($"{e.GetType().Name} reading /proc/{pid}/stat: {e.Message}");
        }
    }

    /// <summary>The state letter and the parent pid that follow the bracketed name — or nothing when they are not there.</summary>
    private static string[] AfterTheName(string rest) =>
        rest.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [{ Length: 1 } state, var parent, ..] && int.TryParse(parent, out _) ? [state, parent] : [];

    private static ProcStatRead.Unreadable Malformed(string text) =>
        new($"not a /proc stat line: {(text.Length > 80 ? text[..80] + "…" : text)}");
}
