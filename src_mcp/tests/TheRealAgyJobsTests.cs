using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoaiMcp.Core.Roles;
using CoaiMcp.Core.Work;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Runners.Toolchain;
using CoaiMcp.Server.RoleJobs;
using FluentAssertions;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// Real agy jobs through the product's own road, with the build of the flip (epoch 3 E3 s8 t6): the coder job runs as a product job does — the worktree, the checks, the sign-in copy, the preflight, the guard, the unit, the turn check, the fix turn — with the REAL <c>agy</c>, the real person's sign-in as the source of the copy,
/// and the product's own program (<c>coai-mcp</c>, built from this checkout) as the hook handler. A PAID run: it runs only when asked (<c>COAI_REAL_AGY_JOBS_RUN=1</c>, with a results file and a made-up jobs folder named); a skip is not a pass.
/// </summary>
/// <remarks>
/// <para>At most <see cref="MostTurns"/> paid turns in all: the launcher every unit goes through counts them and REFUSES the next one before any process starts. Each job is a small made-up repository with one file that says <c>state: red</c>, a task that asks for <c>state: green</c>, and checks that also want a second line the task does
/// not name — so turn 0 is red and the fix turn is the one that learns it from the checks. One agy model in the route, <c>COAI_FIX_ROUNDS=1</c>.</para>
/// <para>What is written: for each turn the exit, the time, the tool events by name and state, the guard's records (tool and decision), the answer of the turn check, whether the fix turn went on in the same conversation, the refreshes, and how many tool events were outside the three. No token, environment, prompt or file content: the real sign-in file is only read, and its sha256 before and after is compared inside this test.</para>
/// </remarks>
[Collection("fakecli-env")]
public sealed class TheRealAgyJobsTests
{
    private const int MostTurns = 6;

    private const string Model = "gemini-4-argon-medium";

    private static readonly string[] Three = ["read_file", "write_file", "edit_file"];

    private static string HashOf(string file) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    private sealed record Turn(int Job, int Index, int Exit, bool TimedOut, double Seconds, IReadOnlyList<string> Events, IReadOnlyList<string> Records, string Check, string Conversation, bool Resumed, bool SameConversation, int Outside);

    /// <summary>Counts the coder turns of every job (the units named <c>coai-coder-…-cN</c>), refuses the one past the cap before any process is started, and keeps what each turn showed.</summary>
    private sealed class Counting(IProcessLauncher inner, int job, string recordsOf, Func<int> turnsSoFar, Action bump, List<Turn> turns) : IProcessLauncher
    {
        private static readonly System.Text.RegularExpressions.Regex TurnUnit = new("^coai-coder-.+-c[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private string _first = string.Empty;

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            var args = request.Arguments;
            var at = args.ToList().IndexOf("--unit");
            if (at < 0 || at + 1 >= args.Count || !TurnUnit.IsMatch(args[at + 1]))
            {
                return await inner.RunAsync(request, ct);
            }

            if (turnsSoFar() >= MostTurns)
            {
                throw new InvalidOperationException($"the harness refuses a paid turn past {MostTurns}: nothing was started");
            }

            bump();
            var conversationArg = args.ToList().IndexOf("--conversation");
            var resumedWith = conversationArg >= 0 && conversationArg + 1 < args.Count ? args[conversationArg + 1] : string.Empty;
            var watch = Stopwatch.StartNew();
            var result = await inner.RunAsync(request, ct);
            watch.Stop();
            var (events, conversation, outside) = Parse(result.StdOut);
            var records = ReadRecords(recordsOf);
            var check = Check(result, resumedWith, recordsOf);
            if (_first.Length == 0)
            {
                _first = conversation;
            }

            turns.Add(new Turn(job, turns.Count(turn => turn.Job == job), result.ExitCode, result.TimedOut, Math.Round(watch.Elapsed.TotalSeconds, 1), events, records, check, conversation.Length > 0 ? "seen" : "none", resumedWith.Length > 0, resumedWith.Length > 0 && resumedWith == _first, outside));

            return result;
        }
    }

    private static (IReadOnlyList<string> Events, string Conversation, int Outside) Parse(string stdout)
    {
        var last = new Dictionary<(string, long), (string Tool, string State)>();
        var order = new List<(string, long)>();
        var conversation = string.Empty;
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.TryGetProperty("conversation_id", out var top) && top.ValueKind == JsonValueKind.String)
                {
                    conversation = top.GetString() ?? conversation;
                }

                if (root.TryGetProperty("step_update", out var update) && update.ValueKind == JsonValueKind.Object && update.TryGetProperty("tool_name", out var tool) && tool.ValueKind == JsonValueKind.String
                    && update.TryGetProperty("step_index", out var step) && step.TryGetInt64(out var index) && update.TryGetProperty("state", out var state))
                {
                    var key = (update.TryGetProperty("conversation_id", out var id) ? id.GetString() ?? string.Empty : string.Empty, index);
                    if (!last.ContainsKey(key))
                    {
                        order.Add(key);
                    }

                    last[key] = (tool.GetString() ?? string.Empty, state.GetString() ?? string.Empty);
                }
            }
            catch (JsonException)
            {
                // A line that is not an event is not a tool event.
            }
        }

        var events = order.Select(key => $"step {key.Item2.ToString(CultureInfo.InvariantCulture)}: {last[key].Tool} {last[key].State}").ToList();

        return (events, conversation, order.Count(key => !Three.Contains(last[key].Tool, StringComparer.Ordinal)));
    }

    private static IReadOnlyList<string> ReadRecords(string file)
    {
        if (UnitWrittenFile.Read(file, UnitWrittenFile.MostRecordsBytes) is not FileReadResult.Content content)
        {
            return ["(no records file)"];
        }

        var lines = new List<string>();
        foreach (var line in Encoding.UTF8.GetString(content.Bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                lines.Add($"step {(root.TryGetProperty("stepIdx", out var step) ? step.GetRawText() : "?")}: {(root.TryGetProperty("tool", out var tool) ? tool.GetString() : "?")} {(root.TryGetProperty("decision", out var decision) ? decision.GetString() : "?")}");
            }
            catch (JsonException)
            {
                lines.Add("(a line that is not a record)");
            }
        }

        return lines;
    }

    private static string Check(ProcessResult result, string expected, string recordsFile) =>
        AgyTurnCheck.Check(result, expected, () => AgyGuardRecords.Read(recordsFile)) switch
        {
            CoderTurnVerdict.Fine => "Fine",
            CoderTurnVerdict.Boundary boundary => "Boundary: " + boundary.Sentence,
            CoderTurnVerdict.Crash crash => "Crash: " + crash.Sentence,
            _ => "?",
        };

    [Fact]
    public async Task ThreeRealAgyJobs_ThroughTheProductsRoad_AtMostSixPaidTurns()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("COAI_REAL_AGY_JOBS_RUN") == "1", "the real agy jobs are PAID and run only with COAI_REAL_AGY_JOBS_RUN=1 — this is a skip, not a pass");
        var resultsFile = Environment.GetEnvironmentVariable("COAI_REAL_AGY_JOBS_RESULTS");
        var jobsFolder = Environment.GetEnvironmentVariable("COAI_REAL_AGY_JOBS");
        Assert.SkipWhen(string.IsNullOrEmpty(resultsFile) || string.IsNullOrEmpty(jobsFolder), "COAI_REAL_AGY_JOBS_RESULTS and COAI_REAL_AGY_JOBS name the results file and a made-up folder for the job folders");
        var realHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var agy = Environment.GetEnvironmentVariable("COAI_REAL_AGY_CLI") is { Length: > 0 } named ? named : Path.Combine(realHome, ".local", "bin", "agy");
        var realFile = Path.Combine(realHome, AgyCoderUnit.SignInRelative);
        Assert.SkipUnless(File.Exists(agy) && File.Exists(realFile), "needs the real agy and a real agy sign-in file");
        var handler = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "bin", "Debug", "net10.0", "coai-mcp");
        handler = Path.GetFullPath(handler);
        Assert.SkipUnless(File.Exists(handler), "needs the product's own program as the hook handler (built from this checkout)");

        var before = HashOf(realFile);
        var report = new StringBuilder();
        var turns = new List<Turn>();
        var used = 0;
        var jobsRoot = Path.Combine(jobsFolder!, "run-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            for (var number = 1; number <= 3; number++)
            {
                if (used >= MostTurns)
                {
                    report.AppendLine($"job {number.ToString(CultureInfo.InvariantCulture)}: NOT RUN — the {MostTurns}-turn cap was reached");

                    continue;
                }

                var toolchain = ToolchainFixture.Shell(
                    "echo built",
                    "if ! grep -q 'state: green' src/thing.cs; then echo 'FAILED thing: src/thing.cs does not say state: green'; exit 1; fi; if ! grep -q '^checked: yes$' src/thing.cs; then echo 'FAILED thing: src/thing.cs must also hold the line checked: yes on a line of its own'; exit 1; fi; echo tested");
                await using var rig = await CoderRig.CreateAsync(toolchain: toolchain, extra: new Dictionary<string, string>
                {
                    ["src/thing.cs"] = "state: red\n",
                    [CoderRig.TaskFile] = WorkPlanYaml.Task(new BaTask(CoderRig.TaskId, "Make src/thing.cs say green", "code", 4, [], false, ["src/thing.cs"],
                        [new BaDod("d1", "src/thing.cs holds the line state: green and no longer state: red"), new BaDod("d2", "the tests of the repository pass")], null)),
                });
                rig.UseJobsFolder(jobsRoot);
                rig.Rig.Setting("COAI_ROLE_AGY_CLI", agy);
                rig.Rig.Setting("COAI_FIX_ROUNDS", "1");
                rig.OnHost = host => host with { RealHome = realHome };
                rig.Vendors = CoderVendorSet.Of(CoderBoundaryTable.Shipped, new AgyCoderVendor(new HookHandler(handler, [])));
                rig.AttemptTimeout = TimeSpan.FromMinutes(12);
                var job = rig.NewJob(route: [$"agy/{Model}"], timeoutMinutes: 30);
                var recordsFile = Path.Combine(rig.JobFolder(job), "coder", "agy", "guard-records.jsonl");
                var thisJob = number;
                rig.OnLauncher = launcher => new Counting(launcher, thisJob, recordsFile, () => used, () => used++, turns);

                var watch = Stopwatch.StartNew();
                var (_, ended) = await rig.RunAsync(job).WaitAsync(TimeSpan.FromMinutes(45), TestContext.Current.CancellationToken);
                watch.Stop();

                report.AppendLine($"## Job {number.ToString(CultureInfo.InvariantCulture)}");
                report.AppendLine();
                report.AppendLine($"- end: state `{ended.State}`, stop `{(ended.Stop.Length == 0 ? "none" : ended.Stop)}`, {watch.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} s in all");
                report.AppendLine($"- reason: {ended.Reason}");
                report.AppendLine($"- attempts: {string.Join("; ", ended.Attempts.Select(attempt => $"{attempt.Vendor}/{attempt.Model} stop `{(attempt.Stop.Length == 0 ? "none" : attempt.Stop)}`"))}");
                var resultFile = Path.Combine(rig.World.Store.JobsRoot, job.JobId, "result.json");
                if (File.Exists(resultFile))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(resultFile));
                    report.AppendLine($"- envelope: green {(document.RootElement.TryGetProperty("green", out var green) ? green.GetRawText() : "?")}, refreshes recorded {string.Join(",", Refreshes(document.RootElement))}");
                }

                foreach (var turn in turns.Where(turn => turn.Job == number))
                {
                    report.AppendLine();
                    report.AppendLine($"### Job {number.ToString(CultureInfo.InvariantCulture)}, turn {turn.Index.ToString(CultureInfo.InvariantCulture)} ({(turn.Index == 0 ? "turn 0" : "fix turn")})");
                    report.AppendLine();
                    report.AppendLine($"- exit {turn.Exit.ToString(CultureInfo.InvariantCulture)}{(turn.TimedOut ? " (timed out)" : string.Empty)}, {turn.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
                    report.AppendLine($"- tool events: {(turn.Events.Count == 0 ? "none" : string.Join("; ", turn.Events))}");
                    report.AppendLine($"- tool events outside read_file, write_file, edit_file: {turn.Outside.ToString(CultureInfo.InvariantCulture)}");
                    report.AppendLine($"- guard records: {(turn.Records.Count == 0 ? "none" : string.Join("; ", turn.Records))}");
                    report.AppendLine($"- turn check: {turn.Check}");
                    report.AppendLine($"- resumed with --conversation: {(turn.Resumed ? "yes" : "no")}{(turn.Resumed ? $", same conversation as turn 0: {(turn.SameConversation ? "yes" : "NO")}" : string.Empty)}");
                }

                report.AppendLine();
            }
        }
        finally
        {
            try
            {
                Directory.Delete(jobsRoot, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
                report.AppendLine($"the made-up jobs folder could not be removed: {e.GetType().Name}");
            }
        }

        report.AppendLine($"paid agy turns started in all: {used.ToString(CultureInfo.InvariantCulture)} of at most {MostTurns.ToString(CultureInfo.InvariantCulture)}");
        report.AppendLine($"the made-up jobs folder (with the sign-in copies) is removed: {(!Directory.Exists(jobsRoot)).ToString().ToLowerInvariant()}");
        report.AppendLine($"real sign-in file, measured in the test: {(HashOf(realFile) == before ? "equal" : "changed")}");
        await File.WriteAllTextAsync(resultsFile!, report.ToString(), TestContext.Current.CancellationToken);
        used.Should().BeLessThanOrEqualTo(MostTurns);
    }

    private static IEnumerable<string> Refreshes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "refreshes")
                {
                    yield return property.Value.GetRawText();
                }
                else
                {
                    foreach (var inner in Refreshes(property.Value))
                    {
                        yield return inner;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var inner in Refreshes(item))
                {
                    yield return inner;
                }
            }
        }
    }
}
