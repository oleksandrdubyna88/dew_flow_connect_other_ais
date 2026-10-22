using System.Diagnostics;
using System.Text.Json;
using CoaiMcp.Core.Rounds;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server;
using FluentAssertions;
using Serilog.Core;
using Xunit;

namespace CoaiMcp.Tests;

/// <summary>How the played reviewer answers a launch.</summary>
internal enum Play
{
    /// <summary>Holds until the token it was handed fires, then reports the kill as a caller's cancellation.</summary>
    BlockUntilCancelled,

    /// <summary>Holds until its token fires, then keeps the answer back until the test releases it.</summary>
    BlockThenHoldTheAnswer,

    /// <summary>The real fake CLI answers a clean review.</summary>
    Answer,

    /// <summary>
    /// Answers a clean review, then holds the RETURN until the test releases it — so the cancel can fire while the answer is already paid
    /// for and no site of the scheduler has anything to cut.
    /// </summary>
    AnswerThenHold,

    /// <summary>A rate limit the ladder RETRIES (not the "usage limit" the ladder gives up on): the launch is refused every time.</summary>
    Throttled,
}

/// <summary>
/// A repository, a data directory and a reviewer process played by the test — the shape <c>ALocalCancellationCarriesItsSourceTests</c> built,
/// with one play PER PROVIDER so a round can have one reviewer answer while the other is cut. Driven through the real
/// <see cref="PanelService"/> and the real git; only the vendor CLI is played, so "blocks until its token fires" costs no child process and no
/// sleep. A test class using it is in the <c>fakecli-env</c> collection: it sets the process environment the fake CLI reads.
/// </summary>
[Collection("fakecli-env")]
internal sealed class WithdrawalRig : IAsyncDisposable
{
    public const string Clean = """{"findings": []}""";

    public const string Scope = """
        # SCOPE

        Withdrawing a call must stop the work it started. A reviewer cut by the cancel is recorded with what it spent, the round says
        it was withdrawn, and nobody is asked to decide a verdict that was never reached.

        When it is done: the session file, the database and the ledger agree on the ending, and the next call begins from the
        state the withdrawn one began from. Constraint: no new dependency.
        """;

    private readonly ProcessLauncher _git = new();

    private WithdrawalRig() { }

    public string Repo { get; private set; } = string.Empty;

    public string Data { get; private set; } = string.Empty;

    public static string FakeCliExe => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "FakeCli.exe" : "FakeCli");

    /// <summary>A repository on branch <c>feature</c> with one commit past <c>main</c>, so the code stage has a diff.</summary>
    public static async Task<WithdrawalRig> CreateAsync()
    {
        var rig = new WithdrawalRig
        {
            Repo = Directory.CreateTempSubdirectory("coai-withdraw-repo-").FullName,
            Data = Directory.CreateTempSubdirectory("coai-withdraw-data-").FullName,
        };
        await rig.Git("init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(rig.Repo, "app.cs"), "v1\n");
        await rig.Git("add", ".");
        await rig.Git("commit", "-m", "base");
        await rig.Git("checkout", "-b", "feature");
        await File.WriteAllTextAsync(Path.Combine(rig.Repo, "app.cs"), string.Concat(Enumerable.Repeat("line\n", 100)));
        await rig.Git("add", ".");
        await rig.Git("commit", "-m", "the feature");
        Environment.SetEnvironmentVariable("FAKECLI_MODE", "vendor");
        Script(Clean);

        return rig;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var name in (string[])["FAKECLI_MODE", "FAKECLI_STDOUT", "FAKECLI_OUTFILE_TEXT", "FAKECLI_EXIT", "FAKECLI_STDERR"])
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var dir in (string[])[Repo, Data])
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A temp directory that outlives one run is litter, not a failed test.
            }
        }

        await Task.CompletedTask;
    }

    public async Task Git(params string[] args)
    {
        var result = await _git.RunAsync(new ProcessRequest(
            "git", ["-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", .. args], Repo));
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', args)}: {result.StdErr}");
    }

    /// <summary>A second commit on the feature branch — what <c>again: true</c> reviews.</summary>
    public async Task CommitAgainAsync(string text)
    {
        await File.AppendAllTextAsync(Path.Combine(Repo, "app.cs"), text);
        await Git("add", ".");
        await Git("commit", "-m", "after the checkpoint");
    }

    /// <summary>What the real fake CLI answers when a play lets it through.</summary>
    public static void Script(string answer)
    {
        Environment.SetEnvironmentVariable("FAKECLI_STDOUT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_OUTFILE_TEXT", answer);
        Environment.SetEnvironmentVariable("FAKECLI_EXIT", "0");
    }

    /// <summary>The reviewers of one provider, as the test signals them.</summary>
    internal sealed class Signals
    {
        /// <summary>Completed when a launch of the provider is under way — the moment a test may cancel.</summary>
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completed when a held launch has seen its token fire.</summary>
        public TaskCompletionSource Killed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The test completes this to let a held launch report.</summary>
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completed when a launch of the provider has produced its answer.</summary>
        public TaskCompletionSource Landed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>How many launches the provider has been asked for.</summary>
        public int Launches => Volatile.Read(ref _launches);

        private int _launches;

        public void Launched() => Interlocked.Increment(ref _launches);
    }

    /// <summary>git through the real launcher; each vendor launch played as <see cref="Play"/> says for ITS provider.</summary>
    internal sealed class Reviewers(IProcessLauncher inner, Func<string, Play> playOf) : IProcessLauncher
    {
        private readonly Dictionary<string, Signals> _signals = new(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _abort = new();

        /// <summary>Ends every blocked launch as a cancellation — a failing test must not leave the round it started holding the host up.</summary>
        public void Abort() => _abort.Cancel();

        /// <summary>The signals of one provider (created on first ask, so a test can await a provider before it launches).</summary>
        public Signals Of(string provider)
        {
            lock (_signals)
            {
                if (!_signals.TryGetValue(provider, out var signals))
                {
                    _signals[provider] = signals = new Signals();
                }

                return signals;
            }
        }

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            if (!string.Equals(request.Executable, FakeCliExe, StringComparison.OrdinalIgnoreCase))
            {
                return await inner.RunAsync(request, ct);
            }

            if (request.Arguments is ["--version"])
            {
                return new ProcessResult(0, "codex-cli 0.131.0\n", string.Empty, TimedOut: false);
            }

            var provider = request.TrackAs.Split('/')[0];
            var signals = Of(provider);
            signals.Launched();
            signals.Arrived.TrySetResult();

            return playOf(provider) switch
            {
                Play.Answer => await Landed(signals, await inner.RunAsync(request, ct)),
                Play.AnswerThenHold => await HeldAfterAnswerAsync(signals, request),
                Play.Throttled => new ProcessResult(1, string.Empty, "429 Too Many Requests", TimedOut: false),
                Play.BlockThenHoldTheAnswer => await HeldAsync(signals, Linked(ct)),
                _ => await BlockedAsync(Linked(ct)),
            };
        }

        private CancellationToken Linked(CancellationToken ct) => CancellationTokenSource.CreateLinkedTokenSource(ct, _abort.Token).Token;

        private static Task<ProcessResult> Landed(Signals signals, ProcessResult result)
        {
            signals.Landed.TrySetResult();

            return Task.FromResult(result);
        }

        private async Task<ProcessResult> HeldAfterAnswerAsync(Signals signals, ProcessRequest request)
        {
            // Run with NO token: the answer is complete before the test cancels, which is the shape being played.
            var result = await inner.RunAsync(request, CancellationToken.None);
            signals.Landed.TrySetResult();
            await signals.Release.Task;

            return result;
        }

        private static async Task<ProcessResult> HeldAsync(Signals signals, CancellationToken ct)
        {
            var result = await BlockedAsync(ct);
            signals.Killed.TrySetResult();
            await signals.Release.Task;

            return result;
        }

        private static async Task<ProcessResult> BlockedAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                // The launcher's own answer for a kill the caller's token ordered.
            }

            return new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: true);
        }
    }

    /// <summary>The launcher the service runs on: git is real, the vendors are played.</summary>
    public Reviewers Playing(Func<string, Play> playOf) => new(_git, playOf);

    /// <summary>The same play for every provider.</summary>
    public Reviewers PlayAll(Play play) => Playing(_ => play);

    /// <summary>Every log event the service wrote, rendered — what a test reads to see a line that is said and not recorded.</summary>
    public sealed class LogSink : Serilog.Core.ILogEventSink
    {
        private readonly List<string> _lines = [];

        public List<string> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public void Emit(Serilog.Events.LogEvent logEvent)
        {
            lock (_lines)
            {
                _lines.Add(logEvent.RenderMessage());
            }
        }
    }

    /// <param name="providers">The reviewer vendors, one reviewer each on the plan stage; codex alone by default.</param>
    /// <param name="roundTimeout">The round's own clock; zero asks for the derived default.</param>
    /// <param name="globalConcurrency">1 queues every reviewer but the first behind it.</param>
    /// <param name="ladder">The rate-limit ladder's waits; empty keeps the shipped one.</param>
    public PanelService Service(
        IProcessLauncher launcher,
        string[]? providers = null,
        TimeSpan roundTimeout = default,
        CancellationToken hostStopping = default,
        int globalConcurrency = 3,
        TimeSpan[]? ladder = null,
        Serilog.ILogger? log = null,
        ProviderSettings[]? also = null,
        int withdrawalsBeforeHold = PanelSettings.DefaultWithdrawalsBeforeHold) => new(
        new PanelSettings
        {
            Providers = [.. (providers ?? ["codex"]).Select(p => new ProviderSettings(p) { ExecutablePath = FakeCliExe }), .. also ?? []],
            Rounds = PanelConfig.Uniform(3, 2, StagePolicy.Human),
            DataDir = Data,
            ReviewerTimeout = TimeSpan.FromMinutes(10),
            RateLimitBackoff = TimeSpan.FromMilliseconds(5),
            RoundTimeout = roundTimeout,
            GlobalConcurrency = globalConcurrency,
            RetryLadder = ladder ?? Runners.Reviewers.RetryLadder.Default,
            WithdrawalsBeforeHold = withdrawalsBeforeHold,
        },
        VaultKeys.None("no vault in tests"), default, launcher, log ?? Logger.None, Noticing.None, hostStopping: hostStopping);

    /// <summary>The saved session, read from disk as another process would read it.</summary>
    public PersistedSession Session(PanelService service) =>
        service.Store.Load(Repo, "feature") ?? throw new InvalidOperationException("no session was saved");

    /// <summary>The last round of the saved session.</summary>
    public RoundRecord LastRound(PanelService service) => Session(service).Rounds[^1];

    /// <summary>One value per row of a query on <c>coai.db</c> — the projection, read the way <c>ARoundIsMintedAKeyTests</c> reads it.</summary>
    public List<string[]> Db(string sql, params (string Name, object Value)[] parameters)
    {
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(Data, Store.RoundsDb.FileName)};Mode=ReadOnly");
        db.Open();
        using var read = db.CreateCommand();
        read.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            read.Parameters.AddWithValue(name, value);
        }

        using var rows = read.ExecuteReader();
        var found = new List<string[]>();
        while (rows.Read())
        {
            found.Add([.. Enumerable.Range(0, rows.FieldCount).Select(i => rows.IsDBNull(i) ? string.Empty : Convert.ToString(rows.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)]);
        }

        return found;
    }

    /// <summary>The ledger's lines, in the order they were written.</summary>
    public List<JsonElement> Lines() =>
        [.. File.ReadAllLines(Path.Combine(Data, "usage.jsonl")).Select(l => JsonDocument.Parse(l).RootElement.Clone())];

    /// <summary>A string member of a ledger line, or empty.</summary>
    public static string Text(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    /// <summary>The LAST ledger line of one provider's reviewer.</summary>
    public JsonElement LastLineOf(string provider) =>
        Lines().Where(l => Text(l, "provider") == provider).Last();

    /// <summary>The files in the hold-notice directory, or none when it was never created.</summary>
    public string[] HoldNotices()
    {
        var directory = Path.Combine(Data, "escalations");

        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    public static long Now() => Stopwatch.GetTimestamp();

    /// <summary>Waits until the saved session satisfies <paramref name="ready"/> — progress is persisted as it happens.</summary>
    public async Task<bool> SessionAsync(PanelService service, Func<PersistedSession, bool> ready, TimeSpan within)
    {
        var until = DateTime.UtcNow + within;
        do
        {
            if (service.Store.Load(Repo, "feature") is { } session && ready(session))
            {
                return true;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < until);

        return false;
    }
}
