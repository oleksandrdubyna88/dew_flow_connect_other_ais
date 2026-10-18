using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Kimi;
using CoaiMcp.Runners.Processes;

namespace CoaiMcp.FakeCli;

/// <summary>
/// The Linux teardown of <c>todo/PLAN_kimi_and_vibe_on_linux.md</c> DL9 (A3), played in REAL processes: a test host must never become a child subreaper (it would collect every other test's
/// orphans), so the shim's part is a child of the test — <c>supervise-tree</c> drives the product's <see cref="SupervisedTeardown"/> directly, <c>shim-acp</c> is the whole ACP client run the way
/// <c>--ask-acp</c> runs it (subreaper, <c>SIGTERM</c>/<c>SIGINT</c> into a token, the Kimi supervisor and its marker). Children here redirect all three streams: they hold none of ours.
/// </summary>
internal static class SupervisionVerbs
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary><c>setsid()</c>: the process leaves its parent's session and group — what <c>rg</c> does in a Grep turn (L-K4).</summary>
    internal static void OwnSession()
    {
        if (OperatingSystem.IsLinux())
        {
            _ = SetSid();
        }
    }

    [DllImport("libc", EntryPoint = "setsid")]
    private static extern int SetSid();

    /// <summary>A FakeCli child with every stream redirected — it holds nothing of this process's.</summary>
    internal static Process StartChild(params string[] arguments)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info)!;
    }

    internal static void WritePid(string file, Process child) => File.WriteAllText(file, child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Waits until <paramref name="done"/> holds, polling every 20 ms, for as long as <see cref="Patience"/> allows.</summary>
    private static bool Waited(Func<bool> done)
    {
        var until = DateTime.UtcNow + Patience;
        while (!done() && DateTime.UtcNow < until)
        {
            Thread.Sleep(20);
        }

        return done();
    }

    // ---------- the agents of supervise-tree ----------

    /// <summary><c>orphan-maker &lt;dir&gt;</c>: a same-group sleeper and a <c>setsid</c> sleeper, their pids in <c>c1.pid</c> / <c>c2.pid</c>; then it exits — both are orphans of the shim.</summary>
    internal static int OrphanMaker(string dir)
    {
        WritePid(Path.Combine(dir, "c1.pid"), StartChild("sleep", "600000"));
        WritePid(Path.Combine(dir, "c2.pid"), StartChild("sleep", "--own-session", "600000"));

        return 0;
    }

    /// <summary><c>spawn-then-exit &lt;pidFile&gt; &lt;ms&gt;</c>: a child that exits by itself after <c>ms</c>, its pid in the file; then this process exits at once — the child is the shim's orphan, and a zombie of the shim after <c>ms</c>.</summary>
    internal static int SpawnThenExit(string pidFile, string ms)
    {
        WritePid(pidFile, StartChild("exit-after", ms, "0"));

        return 0;
    }

    /// <summary><c>late-spawn &lt;trigger&gt; &lt;pidFile&gt;</c>: waits for the file <c>trigger</c>, and only THEN starts a <c>setsid</c> sleeper (pid in the file); it lives on — a grandchild born late.</summary>
    internal static int LateSpawn(string trigger, string pidFile)
    {
        while (!File.Exists(trigger))
        {
            Thread.Sleep(20);
        }

        WritePid(pidFile, StartChild("sleep", "--own-session", "600000"));
        Thread.Sleep(Timeout.Infinite);

        return 0;
    }

    // ---------- supervise-tree: the product's teardown, driven directly ----------

    /// <summary>
    /// <c>supervise-tree &lt;mode&gt; &lt;dir&gt;</c> — modes <c>orphans</c> (the agent exits first, leaving a same-group and a <c>setsid</c> child), <c>late</c> (a grandchild is born AFTER the first
    /// listing — the member list is handed to <c>onListed</c>, which waits for the late spawn), <c>zombie</c> (a child that exits by itself while the shim is its parent, a zombie before the teardown),
    /// <c>managed</c> (a second child this process started that exits with 17 on its own). Prints one JSON object on stdout.
    /// </summary>
    internal static async Task<int> SuperviseTree(string mode, string dir)
    {
        if (!OperatingSystem.IsLinux() || LinuxSubreaper.Become() is not SubreaperResult.Became)
        {
            Console.Error.WriteLine("fake-cli: supervise-tree needs Linux and a subreaper");

            return 65;
        }

        var started = new HashSet<int>();
        var agent = StartChild(AgentArguments(mode, dir));
        _ = started.Add(agent.Id);
        var managed = mode == "managed" ? StartManaged(started) : null;
        var zombieBefore = Prepared(mode, dir, agent);
        var end = await SupervisedTeardown.EndAsync(agent, Environment.ProcessId, started, PidfdLayer.Calls, members => Listed(mode, dir, members));
        managed?.WaitForExit();
        Console.Out.WriteLine(JsonSerializer.Serialize(new
        {
            survivors = end.Survivors.Select(survivor => survivor.Pid).ToArray(),
            agentExit = agent.ExitCode,
            managedExit = managed?.ExitCode ?? -1,
            zombieBefore,
            states = StatesOf(dir),
        }));

        return 0;
    }

    /// <summary>Every pid file in the folder, and what /proc says of it NOW: its state letter, or <c>gone</c> when no entry is left — a zombie would read <c>Z</c>.</summary>
    private static Dictionary<string, string> StatesOf(string dir) =>
        Directory.GetFiles(dir, "*.pid").ToDictionary(
            file => Path.GetFileName(file),
            file => ProcStat.ReadFor(int.Parse(File.ReadAllText(file)), ProcReads.Real) is ProcStatRead.Read { Line: var line } ? line.State : "gone");

    private static string[] AgentArguments(string mode, string dir) => mode switch
    {
        "late" => ["late-spawn", Path.Combine(dir, "trigger"), Path.Combine(dir, "late.pid")],
        "zombie" => ["spawn-then-exit", Path.Combine(dir, "g.pid"), "300"],
        "orphans" or "managed" => ["orphan-maker", dir],
        _ => ["sleep", "600000"],
    };

    private static Process StartManaged(HashSet<int> started)
    {
        var child = StartChild("exit-after", "60", "17");
        _ = started.Add(child.Id);

        return child;
    }

    /// <summary>What must be true before the teardown starts, per mode — and whether a zombie is really there (<c>zombie</c>).</summary>
    private static bool Prepared(string mode, string dir, Process agent)
    {
        switch (mode)
        {
            case "orphans":
            case "managed":
                _ = Waited(() => File.Exists(Path.Combine(dir, "c1.pid")) && File.Exists(Path.Combine(dir, "c2.pid")) && agent.HasExited);
                return false;
            case "zombie":
                _ = Waited(() => File.Exists(Path.Combine(dir, "g.pid")) && agent.HasExited);
                Thread.Sleep(900);
                return int.TryParse(File.ReadAllText(Path.Combine(dir, "g.pid")), out var pid) && ProcStat.ReadFor(pid, ProcReads.Real) is ProcStatRead.Read { Line.State: "Z" };
            default:
                return false;
        }
    }

    /// <summary>The first listing was just taken; in <c>late</c> mode a grandchild is born NOW — and the call waits for it, so it exists before the first signal.</summary>
    private static void Listed(string mode, string dir, IReadOnlyList<ProcessIdentity> members)
    {
        if (mode != "late")
        {
            return;
        }

        File.WriteAllText(Path.Combine(dir, "trigger"), "go");
        _ = Waited(() => File.Exists(Path.Combine(dir, "late.pid")));
        File.WriteAllText(Path.Combine(dir, "listed.txt"), string.Join(",", members.Select(member => member.Pid)));
    }

    // ---------- shim-acp: the ACP client as --ask-acp runs it ----------

    /// <summary>
    /// <c>shim-acp &lt;script&gt; &lt;markerPath&gt; &lt;cwd&gt; [deadlineSeconds]</c> — becomes a subreaper, takes <c>SIGTERM</c>/<c>SIGINT</c> into the turn's token, writes the
    /// <c>spawning</c> marker, runs the scripted agent through <see cref="AcpClient"/> with <see cref="KimiTreeSupervisor"/>, prints one JSON object. Exits 0 answered, 65 refused, 69 any other end.
    /// </summary>
    internal static async Task<int> ShimAcp(string script, string markerPath, string cwd, int deadlineSeconds)
    {
        if (LinuxSubreaper.Become() is SubreaperResult.Failed failed)
        {
            Console.Error.WriteLine(LinuxSubreaper.Sentence(failed.Errno));

            return 65;
        }

        using var stopping = new CancellationTokenSource();
        using var signals = StopSignals.Into(stopping);
        if (KimiTreeSupervisor.Begin(markerPath, cwd, "fake-sign-in") is not SupervisorBegin.Ready ready)
        {
            Console.Error.WriteLine("fake-cli: the spawning marker could not be written");

            return 65;
        }

        var ask = new AcpAsk(Environment.ProcessPath!, cwd, "m-1", "say hello", TimeSpan.FromSeconds(deadlineSeconds));
        var outcome = await new AcpClient(new ShimProfile(script), new FolderReadBoundary(cwd, []), AcpClient.DefaultUsageBound, ready.Supervisor).AskAsync(ask, stopping.Token);
        Console.Out.WriteLine(JsonSerializer.Serialize(new { end = outcome.End.ToString(), agentPid = outcome.AgentPid, survivors = outcome.Survivors.Select(survivor => survivor.Pid).ToArray() }));

        return outcome.End == AcpEnd.Answered ? 0 : 69;
    }

    private sealed class ShimProfile(string script) : IAcpProfile
    {
        public string Name => "fake";

        public string DefaultExecutable => Environment.ProcessPath!;

        public IReadOnlyList<string> Arguments => ["acp-agent", script];

        public string ModelOptionId => "model";

        public IReadOnlyList<string> ExcludedNames => [];

        public IReadOnlyList<AcpKnownFailure> KnownFailures => [];
    }
}
