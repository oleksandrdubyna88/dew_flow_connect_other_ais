using CoaiMcp.Runners.Processes;

namespace CoaiMcp.Runners.Detach;

/// <summary>
/// Starts a program that must outlive the process that started it — the role runner of epoch 3 E1 — by the first road that works.
/// </summary>
/// <remarks>
/// <para><b>Three properties, all measured.</b> <c>MemoryMax</c> is the ceiling; <c>MemorySwapMax=0</c> makes it a kill (the swap absorbs the excess otherwise); and <c>OOMPolicy=continue</c> makes the kill
/// land on the over-limit CHILD only. With systemd's default (<c>stop</c>) the kernel's OOM kill of a runner's CLI child stopped the whole unit — the runner with it, no result, the job
/// <c>lost</c>; with <c>continue</c> the runner survives, sees its child die by SIGKILL and can fail over (<c>v2/spikes/RESULTS_detach.md</c> §4).</para>
/// <para><b>The order is the measured one</b> (<c>v2/spikes/RESULTS_detach.md</c>, 2026-11-23): (1) Linux with a user manager, <c>systemd-run --user --collect</c> with
/// <c>MemoryMax</c> AND <c>MemorySwapMax=0</c> — the runner gets a cgroup of its own, so it survives a kill of the client's process group AND of the client's whole cgroup; (2) else
/// <c>setsid -f</c> — survives a group or session kill, NOT a kill of the client's cgroup, and enforces no memory limit; (3) a POSIX host with no <c>setsid</c> (macOS):
/// <c>nohup</c> from a shell — survives the parent's exit only, not measured; (4) Windows: <see cref="WindowsDetach"/>, not measured here.</para>
/// <para><b>Chosen by trying, not by probing.</b> A missing user manager is a fast non-zero exit of <c>systemd-run</c> itself (rc 1 in under 10 ms), so the code runs it and
/// falls to the next road on any non-zero exit; the road recorded is the one that worked. A road that is NAMED (<see cref="LauncherChoice"/>) is not left for another.</para>
/// <para><b>No pipe of ours reaches the runner.</b> The shell roads redirect the runner's three streams to the null device before it starts: a launcher waiting for the end
/// of its child's output would otherwise wait for the runner's whole life, and the caller's answer is owed in two seconds. The unit has no such problem — systemd gives it its own.</para>
/// <para>Each road is given a short deadline (<see cref="RoadBudget"/>): a user manager that hangs must cost the caller a second, not a minute. A road that timed out may still have
/// started its runner; the next one then starts a second, and the runner's lease makes the second exit at once — never two writers.</para>
/// </remarks>
/// <param name="processes">Runs the POSIX roads' programs.</param>
/// <param name="windowsRoad">The Windows road, a seam like <paramref name="processes"/>: <see langword="null"/> is <see cref="WindowsDetach"/>, which calls <c>CreateProcess</c> and never asks <paramref name="processes"/> — so a test that wants
/// a launcher that starts nothing, or one that fails, must be able to say so on Windows too, or it starts a real runner there.</param>
public sealed class DetachedLauncher(IProcessLauncher processes, Func<DetachSpec, DetachResult>? windowsRoad = null)
{
    /// <summary>How long one road may take to answer. A healthy <c>systemd-run</c> answers in about 15 ms.</summary>
    public static readonly TimeSpan RoadBudget = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The <c>systemd-run</c> arguments every road shares — a user unit that is collected, quiet, named, started in a folder, and started with <c>--expand-environment=no</c>: the user manager would otherwise replace <c>${NAME}</c> and a whole-word <c>$NAME</c>
    /// in EVERY argument from its own environment (an unset name becomes an empty argument), and the job's values — a toolchain argv, a file name, a path — are on that line (the security verify, M4). The option needs systemd 254; an older client refuses it and starts
    /// nothing, which is the closed side (<see cref="ExpandEnvironmentHint"/>). The check boundary (<c>CheckBoundary</c>) starts from these too and adds <c>--wait --pipe</c>.
    /// </summary>
    public static IReadOnlyList<string> UnitPrefix(string unitName, string workingDirectory) =>
        ["--user", "--collect", "--quiet", "--expand-environment=no", "--unit", unitName, "--working-directory", workingDirectory];

    /// <summary>
    /// The sentence to add to a refusal of <c>systemd-run</c> when its own error says it does not know <c>--expand-environment=no</c> (systemd before 254): such a client starts nothing, which is the closed side — no unit of this family
    /// ever runs without the option, because the manager would otherwise replace <c>${NAME}</c> in every argument from its own environment. Empty for any other fault.
    /// </summary>
    public static string ExpandEnvironmentHint(string stderr) =>
        stderr.Contains("expand-environment", StringComparison.Ordinal) ? "this systemd-run does not know --expand-environment=no, which needs systemd 254 or newer; no unit is started without it" : string.Empty;

    /// <summary>The two memory properties, as <c>Name=Value</c>: the ceiling and <c>MemorySwapMax=0</c>, which makes it a kill — without it the swap absorbs the excess and nothing is killed. One place, two callers.</summary>
    public static IReadOnlyList<string> MemoryProperties(int megabytes) => [$"MemoryMax={megabytes}M", "MemorySwapMax=0"];

    /// <summary>The arguments of the <c>systemd-run</c> road: the unit, the working folder and BOTH memory properties — without <c>MemorySwapMax=0</c> the swap absorbs the excess and nothing is killed.</summary>
    public static IReadOnlyList<string> SystemdRunArguments(DetachSpec spec) =>
    [
        .. UnitPrefix(spec.UnitName, spec.WorkingDirectory),
        .. MemoryProperties(spec.MemoryMegabytes).SelectMany(property => new[] { "-p", property }),
        "-p", "OOMPolicy=continue",
        // A runner holds the environment of a keyed job: it never dumps core (the second judgement).
        "-p", "LimitCORE=0",
        "--", spec.Executable, .. spec.Arguments,
    ];

    /// <summary>The arguments of the <c>sh</c> that starts <c>setsid -f</c>: <c>$0</c> is the runner, <c>"$@"</c> its arguments, the three streams the null device.</summary>
    public static IReadOnlyList<string> SetsidShellArguments(DetachSpec spec) =>
        ["-c", "exec setsid -f \"$0\" \"$@\" </dev/null >/dev/null 2>&1", spec.Executable, .. spec.Arguments];

    /// <summary>The arguments of the last POSIX road, for a host with no <c>setsid</c>.</summary>
    public static IReadOnlyList<string> NohupShellArguments(DetachSpec spec) =>
        ["-c", "nohup \"$0\" \"$@\" </dev/null >/dev/null 2>&1 &", spec.Executable, .. spec.Arguments];

    /// <summary>Starts the program by the first road that works (or the named one) and says which.</summary>
    public async Task<DetachResult> StartAsync(DetachSpec spec, LauncherChoice choice = LauncherChoice.Auto, CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return (windowsRoad ?? WindowsDetach.Start)(spec);
        }

        var said = new List<string>();
        var roads = Roads(choice);
        foreach (var road in roads)
        {
            var (ok, detail) = await TryAsync(road, spec, ct);
            if (ok)
            {
                return new DetachResult(true, road, road == "systemd-run" ? spec.UnitName : string.Empty, detail);
            }

            said.Add($"{road}: {detail}");
        }

        return new DetachResult(false, string.Empty, string.Empty, string.Join("; ", said));
    }

    private static string[] Roads(LauncherChoice choice) => choice switch
    {
        LauncherChoice.SystemdRun => ["systemd-run"],
        LauncherChoice.Setsid => ["setsid", "sh-nohup"],
        _ when OperatingSystem.IsLinux() => ["systemd-run", "setsid", "sh-nohup"],
        _ => ["setsid", "sh-nohup"],
    };

    private async Task<(bool Ok, string Detail)> TryAsync(string road, DetachSpec spec, CancellationToken ct)
    {
        var request = road switch
        {
            "systemd-run" => new ProcessRequest("systemd-run", SystemdRunArguments(spec), spec.WorkingDirectory),
            "setsid" => new ProcessRequest("/bin/sh", SetsidShellArguments(spec), spec.WorkingDirectory),
            _ => new ProcessRequest("/bin/sh", NohupShellArguments(spec), spec.WorkingDirectory),
        };
        try
        {
            var result = await processes.RunAsync(request with { Timeout = RoadBudget }, ct);

            return result switch
            {
                { TimedOut: true } => (false, $"no answer in {RoadBudget.TotalMilliseconds:0} ms"),
                { ExitCode: 0 } => (true, string.Empty),
                _ => (false, $"exit {result.ExitCode}: {Short(result.StdErr)}"),
            };
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // The program is not there (no systemd-run on this host, no setsid): the next road is the answer, not a fault.
            return (false, e.Message);
        }
    }

    private static string Short(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ");

        return line.Length <= 300 ? line : line[..300] + "…";
    }
}
