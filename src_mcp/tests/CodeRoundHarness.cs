using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>
/// The shared fixture of every test that runs a real orchestrated round against real git and the vendor-mode fake CLI: a repository on a branch with one
/// change, a data directory, the service built over them, the fake's answer, and a code round whose first answers need repairing (with a recorder that
/// notes where each repair and each review launched).
/// </summary>
/// <remarks>
/// A named unit rather than a base class or a <c>partial</c> class (the repo rule: no <c>partial</c> as a split): <see cref="PanelServiceTests"/> and
/// <see cref="TheRoundsEmptyFoldersAreRemovedTests"/> each OWN one and call it. Every class that holds one belongs to the <c>fakecli-env</c> collection,
/// because the fake is steered through process-wide environment variables.
/// </remarks>
[Collection("fakecli-env")]
internal sealed class CodeRoundHarness : IAsyncLifetime
{
    /// <summary>
    /// A scope, not a title. The code gate refuses a bare diff, so every code round in these tests
    /// carries what the change was supposed to achieve — which is what a real caller sends.
    /// </summary>
    internal const string Scope = """
        # SCOPE

        The reviewer's own words must survive a failure. A reviewer that falls over is recorded with
        an outcome and nothing else, so the round summary says "unparseable" without the text that
        would not parse, and the same answer replayed by hand goes through cleanly.

        When it is done: the raw answer is kept beside the session, the refusal names the file, and
        a failed reviewer still reports what it consumed. Constraint: no new dependency, and the
        launcher stays the one in v2.Shared.
        """;

    internal const string CleanReview = """{"findings": []}""";

    internal const string OneMajor = """
        {"findings": [{"severity": "major", "category": "security", "file": "app.cs", "line": 1,
          "title": "token compared with ==", "why": "timing side channel", "fix": "use FixedTimeEquals"}]}
        """;

    public ProcessLauncher Launcher { get; } = new();
    public string Repo { get; private set; } = string.Empty;
    public string Data { get; private set; } = string.Empty;

    internal static string FakeCliExe => Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    public async ValueTask InitializeAsync()
    {
        Repo = Directory.CreateTempSubdirectory("coai-panel-repo-").FullName;
        Data = Directory.CreateTempSubdirectory("coai-panel-data-").FullName;
        await Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(Repo, "app.cs"), "v1\n");
        await Git("add", ".");
        await Git("commit", "-m", "base");
        await Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(Repo, "app.cs"), "v2\n");
        await Git("add", ".");
        await Git("commit", "-m", "change");

        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        SetAnswer(CleanReview);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_RECORD_DIR", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        // Each in its own try: a folder that cannot be removed must not leave the other behind.
        Remove(Repo);
        Remove(Data);

        return ValueTask.CompletedTask;
    }

    private static void Remove(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static void SetAnswer(string json)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", json);       // the gemini path
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", json); // the codex path
    }

    public async Task Git(params string[] args)
    {
        var result = await Launcher.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], Repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    public PanelService Service(
        int threshold = 2,
        StagePolicy onExhausted = StagePolicy.Human,
        int maxRounds = 3,
        bool deal = false,
        IProcessLauncher? through = null,
        string workspace = "none",
        Serilog.ILogger? log = null) =>
        new(
            new PanelSettings
            {
                Providers =
                [
                    new("codex") { ExecutablePath = FakeCliExe },
                    new("gemini") { ExecutablePath = FakeCliExe },
                ],
                Rounds = PanelConfig.Uniform(maxRounds, threshold, onExhausted),
                DataDir = Data,
                ReviewerTimeout = TimeSpan.FromSeconds(30),
                RateLimitBackoff = TimeSpan.FromMilliseconds(5),
                DealPlanLenses = deal,
                DealCodeLenses = deal,
                CodeWorkspace = workspace,
            },
            VaultKeys.None("no vault in tests"),
            default,
            through ?? Launcher,
            log ?? Logger.None, Noticing.None);

    /// <summary>One launch the repair prompt rode in: where it ran, and what that folder held at the moment it started.</summary>
    internal sealed record RepairLaunch(string WorkingDirectory, IReadOnlyList<string> EntriesAtLaunch);

    /// <summary>What a repaired code round did: where each repair and each review launched, and the answer the round gave.</summary>
    internal sealed record RepairedRound(IReadOnlyList<RepairLaunch> Repairs, IReadOnlyList<RepairLaunch> Reviews, string Answer);

    /// <summary>
    /// A code round whose first answers are not the schema's JSON, so each reviewer is launched a second time with the repair prompt;
    /// the answers to the repair are clean. <paramref name="beforeARepair"/> is told the repair's folder just before the launch starts.
    /// </summary>
    public async Task<RepairedRound> RepairedCodeRoundAsync(string workspace, Action<string>? beforeARepair = null, Serilog.ILogger? log = null)
    {
        const string Marker = "YOUR PREVIOUS ATTEMPT";
        var seen = new SeenRepairs(Launcher, Marker) { BeforeARepair = beforeARepair ?? (static _ => { }) };
        try
        {
            // The review launch runs IN the checkout, so a repair that carried the workspace would be seen as not empty.
            var service = Service(through: seen, workspace: workspace, log: log);
            await service.OpenAsync(Repo, "feature");
            await service.ReviewPlanAsync(Repo, "feature", "the plan");
            await service.ResolveAsync(Repo, "feature", "[]");

            SetAnswer("this is prose, not the schema's JSON");
            Environment.SetEnvironmentVariable("FAKECLI_REPAIR_MARKER", Marker);
            Environment.SetEnvironmentVariable("FAKECLI_TURN1_REPAIR_STDOUT", CleanReview);
            Environment.SetEnvironmentVariable("FAKECLI_TURN1_REPAIR_OUTFILE_TEXT", CleanReview);

            seen.StartRecording();
            var answer = await service.ReviewCodeAsync(Repo, "feature", "main", Scope);

            return new([.. seen.Repairs], [.. seen.Reviews], answer);
        }
        finally
        {
            foreach (var name in (string[])["FAKECLI_REPAIR_MARKER", "FAKECLI_TURN1_REPAIR_STDOUT", "FAKECLI_TURN1_REPAIR_OUTFILE_TEXT"])
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }

    /// <summary>The real launcher, noting each launch that carries the repair prompt — and what its folder held before it started.</summary>
    internal sealed class SeenRepairs(IProcessLauncher real, string marker) : IProcessLauncher
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<RepairLaunch> _repairs = new();

        private readonly System.Collections.Concurrent.ConcurrentQueue<RepairLaunch> _reviews = new();

        private volatile bool _recording;

        /// <summary>Told the folder of each repair launch just before it starts — what a test does to that folder while the round is still running.</summary>
        public Action<string> BeforeARepair { get; init; } = static _ => { };

        /// <summary>From here on launches are noted: the plan round before it is not the code round the test is about.</summary>
        public void StartRecording() => _recording = true;

        public IReadOnlyCollection<RepairLaunch> Repairs => _repairs;

        /// <summary>The other launches of the fake CLI — the reviews — with the same two facts.</summary>
        public IReadOnlyCollection<RepairLaunch> Reviews => _reviews;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (_recording)
            {
                Note(request);
            }

            return real.RunAsync(request, ct);
        }

        private void Note(ProcessRequest request)
        {
            var carriesTheRepair = request.StdIn.Contains(marker, StringComparison.Ordinal)
                || request.Arguments.Any(argument => argument.Contains(marker, StringComparison.Ordinal));
            var launch = new RepairLaunch(request.WorkingDirectory, EntriesOf(request.WorkingDirectory));
            if (carriesTheRepair)
            {
                _repairs.Enqueue(launch);
                BeforeARepair(request.WorkingDirectory);
            }
            else if (IsAReview(request))
            {
                _reviews.Enqueue(launch);
            }
        }

        /// <summary>
        /// A launch of the fake CLI that carries a prompt. The code round also asks the fake `--version` from the round's checkout, which has
        /// no prompt and is no review — counted as one, it satisfied the positive control in a mode where every real review ran in an empty folder.
        /// </summary>
        private static bool IsAReview(ProcessRequest request) =>
            Path.GetFileNameWithoutExtension(request.Executable) == "FakeCli" && request.StdIn.Length > 0;

        /// <summary>What the folder holds — nothing for a folder that is not there: noting a launch must never be what fails it.</summary>
        private static IReadOnlyList<string> EntriesOf(string directory) =>
            Directory.Exists(directory) ? [.. Directory.EnumerateFileSystemEntries(directory)] : [];
    }

}
