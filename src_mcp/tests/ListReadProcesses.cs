namespace CoaiMcp.Tests;

/// <summary>One process seen in the unit of the list read, in one sample: the sample's number (in the order they were taken), the pid, the name the kernel gives it (<c>comm</c>), its parent pid and the real path of its executable (<c>?</c> when they could not be read: a process may end between the looks).</summary>
internal sealed record SeenProcess(int Sample, int Pid, string Comm, string Parent, string Exe);

/// <summary>
/// The rule of the real list read about the processes of its unit (epoch 3 E3 s2 t5, A4-M3 item 4, as the judge read it in part 15.12 and then ruled on <c>sd-userns</c>): beside the CLI the unit may hold the helper programs of the CLI's own OS probe (<c>sh</c>, <c>lsb_release</c>, <c>cut</c>, <c>sed</c>, <c>tr</c>), and a name in brackets is
/// the same name. Two names belong to the START of the unit and to nothing else, so they are allowed ONLY in samples taken before the first sample that shows the unit's main process as the wrapper <c>sh</c> or as <c>codex</c>: <c>(sh)</c>, which is the main process before it execs, and
/// <c>sd-userns</c> (also <c>(sd-userns)</c>), the manager's own helper that sets up the unit's namespace. At or after that sample they fail the run and are named. There is no parent rule (<c>(sh)</c>, <c>sh</c> and <c>codex</c> are one pid that changes by exec). When the executable or the parent of <c>sd-userns</c>
/// could be read they are held too: the executable is a program of systemd (under a <c>systemd</c> folder; not the release binary, not <c>dash</c>, not a file of the job) and the parent is the unit's main pid; when they could not be read, the time rule alone decides.
/// </summary>
internal static class ListReadProcesses
{
    /// <summary>The names the unit may hold at any time of the read.</summary>
    internal static readonly string[] Allowed = ["codex", "sh", "lsb_release", "cut", "sed", "tr"];

    /// <summary>The names of the START of the unit: allowed only before the main process ran as <c>sh</c> or <c>codex</c>.</summary>
    internal static readonly string[] OfTheStart = ["sd-userns"];

    /// <summary>The number of the first sample that shows the unit's main process as the wrapper <c>sh</c> or as <c>codex</c> (the name without brackets), or null when no sample does.</summary>
    internal static int? Cutoff(IReadOnlyList<SeenProcess> seen) =>
        seen.Where(process => process.Comm is "sh" or "codex").Select(process => (int?)process.Sample).Min();

    /// <summary>Each violation, as a sentence that names the process; empty when the rule holds. <paramref name="mainPid"/> is the unit's main pid as the manager says it, or empty.</summary>
    internal static IReadOnlyList<string> Violations(IReadOnlyList<SeenProcess> seen, string mainPid)
    {
        var cutoff = Cutoff(seen);
        var violations = new List<string>();
        foreach (var process in seen)
        {
            var name = process.Comm.Trim('(', ')');
            var late = cutoff is { } at && process.Sample >= at;
            if (name == "sh" && process.Comm.StartsWith('(') && late)
            {
                violations.Add($"{process.Comm} (pid {process.Pid}) in sample {process.Sample}, at or after sample {cutoff} where the main process ran as sh or codex");
            }
            else if (OfTheStart.Contains(name, StringComparer.Ordinal))
            {
                if (late)
                {
                    violations.Add($"{process.Comm} (pid {process.Pid}) in sample {process.Sample}, at or after sample {cutoff} where the main process ran as sh or codex");
                }
                else if (process.Exe != "?" && !process.Exe.Contains("/systemd/", StringComparison.Ordinal))
                {
                    violations.Add($"{process.Comm} (pid {process.Pid}) runs {process.Exe}, which is not a program of systemd");
                }
                else if (process.Parent != "?" && mainPid.Length > 0 && process.Parent != mainPid)
                {
                    violations.Add($"{process.Comm} (pid {process.Pid}) has the parent {process.Parent}, which is not the unit's main pid {mainPid}");
                }
            }
            else if (!Allowed.Contains(name, StringComparer.Ordinal))
            {
                violations.Add($"{process.Comm} (pid {process.Pid}) in sample {process.Sample} is not on the list ({string.Join(", ", Allowed)})");
            }
        }

        return violations;
    }
}
