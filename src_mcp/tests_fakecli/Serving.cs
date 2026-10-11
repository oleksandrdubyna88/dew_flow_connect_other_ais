using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// The stand-in for a CLI that is a SERVER: it answers one request over stdio and goes on living — the shape of
/// <c>codex app-server</c>, which the Team server's gauge reader starts and must end itself (story 6.1a of
/// todo/PLAN_every_round_is_counted.md).
/// </summary>
/// <remarks>
/// <para>Two doors into the same behaviour. The <c>serve</c> VERB takes everything on argv, for the launcher's own
/// tests. The <c>app-server</c> argv — what the real product starts — is steered by a JSON file in the temp directory
/// (<see cref="SteeringFile"/>), because a gauge reader is started CONFINED: no <c>FAKECLI_*</c> variable reaches it,
/// and <c>TMPDIR</c>/<c>TEMP</c> is on the allowlist that does.</para>
/// <para>It survives its parent on purpose. A real app-server exits when stdin closes; the case the reader's containment
/// exists for is the one where it does NOT — a child that outlives the server that started it, holding the account's
/// token set — so this one lives on after EOF for as long as it was told, and a write to a pipe nobody reads any more is
/// swallowed rather than fatal.</para>
/// </remarks>
static class Serving
{
    /// <summary>The steering file an <c>app-server</c> launch reads from the temp directory, as the minimal steering is.</summary>
    public const string SteeringFile = "fakecli-appserver.json";

    /// <summary>What the <c>app-server</c> argv does when nothing steers it: answer the measured fixture and exit on EOF.</summary>
    private const string DefaultAnswer =
        """{"id":2,"result":{"ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","limitName":null,"normalModelSlug":null,"primary":{"usedPercent":40,"windowDurationMins":10080,"resetsAt":1791964091},"secondary":null,"credits":{"hasCredits":false,"unlimited":false,"balance":"0"},"individualLimit":null,"spendControlReached":false,"planType":"prolite","rateLimitReachedType":null},"rateLimitsByLimitId":{"codex":{"limitId":"codex","limitName":null,"normalModelSlug":null,"primary":{"usedPercent":40,"windowDurationMins":10080,"resetsAt":1791964091},"secondary":null,"credits":{"hasCredits":false,"unlimited":false,"balance":"0"},"individualLimit":null,"spendControlReached":false,"planType":"prolite","rateLimitReachedType":null}},"rateLimitResetCredits":{"availableCount":1,"credits":[]},"accountId":"acct-fixture-0000","rateLimitUpsell":null}}""";

    /// <summary>The <c>serve</c> verb: everything on argv.</summary>
    public static int Run(string pidFile, int afterEofMs, bool grandchild, string answer) =>
        Serve(new Steering(answer, afterEofMs, grandchild, pidFile, string.Empty, 0, string.Empty, string.Empty, false));

    /// <summary>The <c>app-server</c> argv: steered by the file in the temp directory, or the defaults.</summary>
    public static int AppServer()
    {
        var path = Path.Combine(Path.GetTempPath(), SteeringFile);
        var steering = File.Exists(path) ? Steering.From(File.ReadAllText(path)) : Steering.Default;

        return Serve(steering);
    }

    private static int Serve(Steering steering)
    {
        var started = new List<string> { Environment.ProcessId.ToString() };
        Process? sleeper = null;
        if (steering.Grandchild)
        {
            // A child of our own that sleeps: the "surviving grandchild" the containment must reach.
            sleeper = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "sleep", "600000" }, UseShellExecute = false });
            started.Add(sleeper!.Id.ToString());
        }

        if (steering.PidFile.Length > 0)
        {
            File.WriteAllLines(steering.PidFile, started);
        }

        if (steering.AuthRewrite.Length > 0 && Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home)
        {
            // A refresh, as a reader would leave it: the token set rewritten in place, under the slot's own home.
            File.WriteAllText(Path.Combine(home, "auth.json"), steering.AuthRewrite);
        }

        if (steering.StartedFile.Length > 0)
        {
            File.WriteAllText(steering.StartedFile, Environment.ProcessId.ToString());
        }

        Answer(steering);
        Linger(steering.AfterEofMs);

        return 0;
    }

    /// <summary>Reads requests until stdin closes; the one carrying id 2 is answered (after the hold), unless told to stay silent.</summary>
    private static void Answer(Steering steering)
    {
        while (Console.In.ReadLine() is { } line)
        {
            if (!line.Contains("\"id\":2", StringComparison.Ordinal) || steering.Silent)
            {
                continue;
            }

            if (steering.HoldMs > 0)
            {
                Thread.Sleep(steering.HoldMs);
            }

            Say(steering.Request.Length > 0 ? steering.Request : steering.Answer);
        }
    }

    /// <summary>One line to stdout — and a closed pipe is not this server's problem.</summary>
    private static void Say(string line)
    {
        try
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
        catch (IOException)
        {
            // The reader of our stdout is gone. A server that survives its client keeps serving nobody.
        }
    }

    private static void Linger(int afterEofMs)
    {
        if (afterEofMs < 0)
        {
            Thread.Sleep(Timeout.Infinite);
        }
        else if (afterEofMs > 0)
        {
            Thread.Sleep(afterEofMs);
        }
    }

    /// <summary>What one launch is told: the answer, how long to outlive EOF, whether to spawn, and where to write pids.</summary>
    private sealed record Steering(
        string Answer, int AfterEofMs, bool Grandchild, string PidFile, string StartedFile, int HoldMs, string AuthRewrite, string Request, bool Silent)
    {
        public static readonly Steering Default = new(DefaultAnswer, 0, false, string.Empty, string.Empty, 0, string.Empty, string.Empty, false);

        public static Steering From(string json)
        {
            var map = JsonSerializer.Deserialize(json, FakeCliJsonContext.Default.DictionaryStringString) ?? [];
            string Text(string key) => map.GetValueOrDefault(key) ?? string.Empty;
            int Number(string key) => int.TryParse(Text(key), out var n) ? n : 0;

            return new Steering(
                Text("answer").Length > 0 ? Text("answer") : DefaultAnswer,
                Number("afterEofMs"), Text("grandchild") == "1", Text("pidFile"), Text("startedFile"), Number("holdMs"),
                Text("authRewrite"), Text("request"), Text("silent") == "1");
        }
    }
}
