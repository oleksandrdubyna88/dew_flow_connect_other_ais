using System.Text;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiServer.Tests;

/// <summary>
/// What the gauge tests share: a DISPOSABLE codex token set (every value fake — a JWT whose only claim is its expiry,
/// signed by nobody), the measured app-server answer (research/RESULTS_vendor_quota_gauges.md §2.2, the account id and
/// the reset credit's id replaced by markers the tests look for), and the fake launchers that stand in for a reader.
/// </summary>
internal static class GaugeFixtures
{
    /// <summary>The account id planted in the fixture answer — a string a log line must never carry.</summary>
    public const string AccountMarker = "acct-SECRET-0000";

    /// <summary>The machine name a status notification carries — likewise.</summary>
    public const string MachineMarker = "MACHINE-SECRET-NAME";

    /// <summary>The measured answer, one line, as the reader's stdout carries it (the account id is <see cref="AccountMarker"/>).</summary>
    public const string Answer =
        """{"id":2,"result":{"ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","limitName":null,"normalModelSlug":null,"primary":{"usedPercent":40,"windowDurationMins":10080,"resetsAt":1791964091},"secondary":null,"credits":{"hasCredits":false,"unlimited":false,"balance":"0"},"individualLimit":null,"spendControlReached":false,"planType":"prolite","rateLimitReachedType":null},"rateLimitsByLimitId":{"codex":{"limitId":"codex","limitName":null,"normalModelSlug":null,"primary":{"usedPercent":40,"windowDurationMins":10080,"resetsAt":1791964091},"secondary":null,"credits":{"hasCredits":false,"unlimited":false,"balance":"0"},"individualLimit":null,"spendControlReached":false,"planType":"prolite","rateLimitReachedType":null}},"rateLimitResetCredits":{"availableCount":1,"credits":[{"id":"credit-opaque","resetType":"codexRateLimits","status":"available","grantedAt":1791417020,"expiresAt":1794009020,"title":"Full reset","description":"one free reset"}]},"accountId":"acct-SECRET-0000","rateLimitUpsell":null}}""";

    /// <summary>The notifications the real stream carries before the answer — the one that names the machine (<see cref="MachineMarker"/>) included.</summary>
    public const string Notifications =
        """{"method":"account/updated","params":{"authMode":"chatgpt","planType":"prolite"}}""" + "\n"
        + """{"method":"remoteControl/status/changed","params":{"machineName":"MACHINE-SECRET-NAME","installationId":"inst-SECRET"}}""" + "\n";

    /// <summary>The measured answer with a secondary (five-hour) window beside the primary — the shape another plan might report.</summary>
    public static readonly string AnswerWithSecondary =
        Answer.Replace("\"secondary\":null", "\"secondary\":{\"usedPercent\":12.5,\"windowDurationMins\":300,\"resetsAt\":1791700000}", StringComparison.Ordinal);

    /// <summary>An authentication error on the read, in the words the app-server uses.</summary>
    public const string AuthError =
        """{"id":2,"error":{"code":-32600,"message":"codex account authentication required to read rate limits"}}""";

    /// <summary>The server asking its client to refresh the tokens — the request a reader must never answer.</summary>
    public const string RefreshRequest =
        """{"id":11,"method":"chatgptAuthTokens/refresh","params":{"reason":"unauthorized","previousAccountId":null}}""";

    /// <summary>A disposable <c>auth.json</c> in codex's shape, whose access token expires at <paramref name="expiresUtc"/>.</summary>
    public static string AuthJson(DateTimeOffset expiresUtc, string refreshToken = "fake-refresh-token-0001") =>
        "{\"auth_mode\":\"chatgpt\",\"OPENAI_API_KEY\":null,\"tokens\":{\"id_token\":\"" + Jwt(expiresUtc) + "\",\"access_token\":\""
        + Jwt(expiresUtc) + "\",\"refresh_token\":\"" + refreshToken + "\",\"account_id\":\"" + AccountMarker + "\"},"
        + "\"last_refresh\":\"2026-10-11T00:00:00Z\"}";

    /// <summary>An unsigned JWT whose payload carries one claim — nothing a vendor would accept, and nothing a test keeps.</summary>
    public static string Jwt(DateTimeOffset expiresUtc) =>
        $"{Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{Base64Url($"{{\"exp\":{expiresUtc.ToUnixTimeSeconds()}}}")}.";

    private static string Base64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Writes a slot's codex home with a token set expiring at <paramref name="expiresUtc"/>.</summary>
    public static void WriteAuth(string slotDirectory, DateTimeOffset expiresUtc)
    {
        Directory.CreateDirectory(Path.Combine(slotDirectory, ".codex"));
        File.WriteAllText(CodexAuthExpiry.AuthFileOf(slotDirectory), AuthJson(expiresUtc));
    }

    /// <summary>A reader that answers with <paramref name="stdout"/> — recording what it was asked, and the tracker it was built with.</summary>
    public sealed class Reader(string stdout, Func<ProcessResult, ProcessResult>? shape = null) : IProcessLauncher
    {
        public List<ProcessRequest> Requests { get; } = [];

        public List<IProcessTracker> Trackers { get; } = [];

        /// <summary>What the reader does to the slot's home on its way, if anything — a refresh, as a reader would leave it.</summary>
        public Action<ProcessRequest> SideEffect { get; init; } = _ => { };

        public IProcessLauncher For(IProcessTracker tracker)
        {
            Trackers.Add(tracker);

            return this;
        }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            // The identity a real launcher reports the moment the child exists — a pid no process has, so a check
            // finds it gone, which is what a fake reader that returned at once amounts to.
            Trackers.Last().Record(999_999, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), request.TrackAs);
            SideEffect(request);
            var result = new ProcessResult(0, stdout, string.Empty, TimedOut: false, Ended: true);

            return Task.FromResult(shape is null ? result : shape(result));
        }
    }

    /// <summary>A launcher that counts how often it was asked to start anything, and starts nothing.</summary>
    public sealed class Counting : IProcessLauncher
    {
        public int Launches { get; private set; }

        public IProcessLauncher For(IProcessTracker tracker) => this;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            Launches++;

            return Task.FromResult(new ProcessResult(0, Answer, string.Empty, TimedOut: false, Ended: true));
        }
    }

    /// <summary>A launcher whose every start throws — the infrastructure failing under the capture.</summary>
    public sealed class Throwing : IProcessLauncher
    {
        public IProcessLauncher For(IProcessTracker tracker) => this;

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default) =>
            throw new IOException("the reader could not be started");
    }

    /// <summary>
    /// A launcher that records the root through the slot's REAL tracker — a pid that is no longer that process — and then
    /// fails as the launcher does when a descendant would not record: the tree ended, the identity on disk partial
    /// (epic 6's code round).
    /// </summary>
    public sealed class DescendantNotRecorded(Exception? inner = null) : IProcessLauncher
    {
        private IProcessTracker _tracker = NoProcessTracking.Instance;

        public IProcessLauncher For(IProcessTracker tracker)
        {
            _tracker = tracker;

            return this;
        }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct = default)
        {
            _tracker.Record(Environment.ProcessId, DateTime.UtcNow.AddHours(-5), request.TrackAs);

            throw new ProcessTreeNotRecordedException([], inner ?? new IOException("the slot's state file will not take the write"));
        }
    }

    /// <summary>A reviewer that answers a clean review — or, while told to, fails with a non-zero exit — recording each launch's environment.</summary>
    public sealed class Reviewer : IReviewLauncher
    {
        public List<IReadOnlyDictionary<string, string?>> Environments { get; } = [];

        /// <summary>While true, every launch exits 1 — a job that ran and failed, which is not a success on the token set.</summary>
        public bool Fails { get; set; }

        public Task<ReviewAttempt> RunAsync(
            VendorConfig vendor, AccountSlot slot, JobRecord job, IReadOnlyDictionary<string, string?> environment, CancellationToken ct)
        {
            Environments.Add(environment);

            return Task.FromResult<ReviewAttempt>(Fails
                ? new ReviewAttempt.Failed(new ReviewerOutcome.NonZeroExit(1, "the vendor exited 1"))
                : new ReviewAttempt.Answered("""{"findings":[]}""", 40, 20));
        }
    }
}
