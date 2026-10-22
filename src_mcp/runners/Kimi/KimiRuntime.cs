using System.Globalization;
using CoaiMcp.Core.Findings;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// What a Kimi adapter reads from the machine — the person's places and the shim's own path — so a test can hand it a fake person.
/// </summary>
/// <param name="PlacesFor">The places of one launch: the person's folders resolved ONCE from the real profile and environment, and coai's owned <c>root</c>.</param>
/// <param name="Shim">This binary, however it was started (<c>coai-mcp --ask-acp</c> is the process the executor launches).</param>
public sealed record KimiSeams(Func<string, KimiPlaces> PlacesFor, Func<(string Executable, IReadOnlyList<string> Prefix)> Shim)
{
    /// <summary>This machine: the real profile and environment, and this binary — or the seams a scenario's flow gave it (<see cref="Using"/>).</summary>
    public static KimiSeams Real(HostKind host) => Overridden.Value ?? new(
        root => KimiPlaces.Resolve(root, host, Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ExecutableResolver.IsInstalledFile),
        LocalRuntime.SelfInvocation);

    private static readonly AsyncLocal<KimiSeams?> Overridden = new();

    /// <summary>
    /// A scenario's seam, per logical flow (the shape of <see cref="KimiHost.Using"/>): the real service builds its <see cref="KimiRuntime"/> itself (<c>new KimiRuntime(vendorId)</c>), which reads the machine through
    /// <see cref="Real"/>, so a flow that must find a FAKE person's <c>~/.kimi-code</c> and start THIS repository's <c>coai-mcp</c> as the shim gives it here. Dispose restores the process's.
    /// </summary>
    internal static IDisposable Using(KimiSeams seams)
    {
        var before = Overridden.Value;
        Overridden.Value = seams;

        return new Restore(() => Overridden.Value = before);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}

/// <summary>
/// Kimi Code (<c>kimi</c> on the person's own Kimi Code plan) as a reviewer — S4's code half (E4.4 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes_E4.md</c>; D1–D20 of <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>). The measurements are
/// <c>research/RESULTS_kimi_confinement.md</c>'s; all of them Windows, Kimi 2.1.1.
/// </summary>
/// <remarks>
/// <para><b>The adapter starts THIS binary.</b> The executor launches <c>coai-mcp --ask-acp --profile kimi</c> like any CLI, times it out and kills its
/// tree; the shim is the ACP client, with its three blocks (every permission refused, reads only through the boundary, nothing else offered — D15) and
/// its own deadline under the executor's. The prompt rides on stdin and never on the argv. The shim is started in the BUILT environment too — D19 builds
/// "both hops" — and is handed the four places it cannot find again from inside the redirected <c>USERPROFILE</c>: <c>--home</c> (the owned ROOT),
/// <c>--persons-bin</c>, <c>--locks</c> and <c>--executable</c> (the absolute <c>kimi</c>, resolved HERE in the unredirected parent).</para>
/// <para><b>Everything about whether a launch may start is <see cref="KimiLaunch"/>'s</b> (<see cref="TakeTurnAsync"/>): the host, the tools, the model, the
/// environment, the person's sign-in, the home's shape, the gate on the sign-in, the installed version. This class builds, reads and classifies.</para>
/// <para><b>Classified from the report, never from text</b> (<see cref="KimiReport"/>): a spent balance is exit 75 with the report's
/// <c>failure: balance_spent</c> and nothing else; every other exit is an ordinary failure whatever words its trace holds.</para>
/// </remarks>
public sealed class KimiRuntime(string id, KimiHost host, KimiSeams seams, Serilog.ILogger log, bool allowNetworkSignIn = false) : IReviewerRuntime
{
    private const string DeadlineFlag = "--deadline-seconds";

    /// <summary>The adapter with the process's own logger (the host configures it before anything is built).</summary>
    public KimiRuntime(string id, KimiHost host, KimiSeams seams)
        : this(id, host, seams, Serilog.Log.ForContext<KimiRuntime>())
    {
    }

    /// <summary>A test's way in with a bare kind of host (<see cref="KimiHost.Of"/>: a WSL kind is judged by THIS machine's kernel unless the test names one).</summary>
    internal KimiRuntime(string id, HostKind host, KimiSeams seams)
        : this(id, KimiHost.Of(host), seams)
    {
    }

    /// <summary>A test's way in with a bare kind of host, and a logger it can read.</summary>
    internal KimiRuntime(string id, HostKind host, KimiSeams seams, Serilog.ILogger log)
        : this(id, KimiHost.Of(host), seams, log)
    {
    }

    /// <summary>The adapter for this machine's host — or the host a scenario's flow gave it (<see cref="KimiHost.Using"/>).</summary>
    public KimiRuntime(string id = "kimi")
        : this(id, KimiHost.Current)
    {
    }

    /// <summary>The adapter for a given host, reading the real machine.</summary>
    public KimiRuntime(string id, KimiHost host)
        : this(id, host, KimiSeams.Real(host.Kind))
    {
    }

    public string Provider => id;

    /// <summary>Whether THIS adapter admits a sign-in folder on network storage (<c>coai.kimiAllowNetworkSignIn</c>): the answer its launches are judged by, so a test can read what a roster bound to a row.</summary>
    internal bool AllowsNetworkSignIn => allowNetworkSignIn;

    /// <summary>
    /// The <c>kimi</c> CLI — what <c>providers</c> probes and what the card's version and install state are about — and NOT the shim this adapter
    /// launches: the same resolution a launch uses (<see cref="KimiPlaces.Resolve"/>), so the card and the launch cannot name two programs.
    /// </summary>
    public string DefaultExecutable => seams.PlacesFor(string.Empty).Executable;

    /// <summary>D6: Kimi loads its configuration from its working folder (a project <c>.kimi-code</c>, <c>AGENTS.md</c>, <c>.mcp.json</c>), so it reviews a sanitised copy.</summary>
    public bool NeedsASanitisedSnapshot => true;

    /// <summary>D19, until M-K14: the refresh token rotates, so one Kimi launch of a round at a time (the in-process half; the sign-in's gate is the cross-process half).</summary>
    public bool LaunchesOneAtATime => true;

    public ReviewerInvocation Build(string role, string prompt, string worktreePath, string schemaFilePath, string outputDir, ReviewerSettings settings)
    {
        var model = settings.Model.Length > 0 ? settings.Model : KimiModels.Default;
        var root = KimiHome.HomeFor(settings.OwnedScratch.Length > 0 ? settings.OwnedScratch : outputDir, Provider, role);
        var places = PlacesOf(root, settings.ExecutablePath);
        var (shim, prefix) = seams.Shim();
        var request = new ProcessRequest(shim, Arguments(prefix, model, worktreePath, places, settings.Timeout), worktreePath)
        {
            StdIn = prompt,
            Timeout = settings.Timeout,
        };

        // No effort and no tier: neither is measured taking effect on Kimi (shared/feature-availability.json), so none is sent.
        return new ReviewerInvocation(Provider, role, KimiEnvironment.Built(request, places), string.Empty, Bound(settings), Model: model);
    }

    /// <summary>
    /// The adapter THIS launch is judged by: <c>this</c>, or — when the row's settings carry a different answer to "may a sign-in folder on network storage be used" (DL6, <c>coai.kimiAllowNetworkSignIn</c>, which the server
    /// reads from the panel's settings file) — a copy that carries it. Immutable: the setting travels with the launch, never in a field somebody has to reason about across threads.
    /// </summary>
    private KimiRuntime Bound(ReviewerSettings settings) =>
        settings.KimiAllowNetworkSignIn == allowNetworkSignIn ? this : new KimiRuntime(id, host, seams, log, settings.KimiAllowNetworkSignIn);

    /// <summary>
    /// The shim's argv (D15, D19): the profile, the model the session must offer, the folder it works in, the three places and the absolute
    /// <c>kimi</c>, and the shim's own deadline under the executor's. Never the prompt.
    /// </summary>
    internal static IReadOnlyList<string> Arguments(IReadOnlyList<string> prefix, string model, string cwd, KimiPlaces places, TimeSpan timeout) =>
    [
        .. prefix,
        "--ask-acp",
        "--profile", KimiAcpProfile.ProfileName,
        AcpWords.ModelFlag, model,
        "--cwd", cwd,
        AcpWords.HomeFlag, places.OwnedRoot,
        AcpWords.PersonsBinFlag, places.PersonsBin,
        AcpWords.LocksFlag, KimiCredentialsLock.Root,
        DeadlineFlag, DeadlineOf(timeout),
        AcpWords.ExecutableFlag, places.Executable,
    ];

    private static string DeadlineOf(TimeSpan timeout) => LocalAsk.ShimDeadlineSeconds(timeout).ToString(CultureInfo.InvariantCulture);

    /// <summary>The places of one launch: resolved by the seam, with the executable a row configured (when it did) in place of the one found.</summary>
    private KimiPlaces PlacesOf(string root, string configuredExecutable)
    {
        var found = seams.PlacesFor(root);

        return configuredExecutable.Length > 0 ? found with { Executable = configuredExecutable } : found;
    }

    /// <summary>
    /// The launch as it starts: the shim's own deadline from the timeout THIS launch has (a retry's and a repair's are shorter), so its report arrives
    /// before the executor's kill would leave the round guessing.
    /// </summary>
    public ReviewerInvocation ForLaunch(ReviewerInvocation invocation)
    {
        var args = invocation.Request.Arguments.ToArray();
        var at = Array.IndexOf(args, DeadlineFlag);
        if (at < 0 || at + 1 >= args.Length)
        {
            return invocation;
        }

        args[at + 1] = DeadlineOf(invocation.Request.Timeout);

        return invocation with { Request = invocation.Request with { Arguments = args } };
    }

    /// <summary>The one launch control (D8b, D10, D17, D18, D19) — <see cref="KimiLaunch.TakeAsync"/> — over the places this launch's argv names.</summary>
    public Task<LaunchTurn> TakeTurnAsync(ReviewerInvocation invocation, IProcessLauncher launcher, CancellationToken ct)
    {
        var request = invocation.Request;
        var root = KimiEnvironment.FlagOf(request, AcpWords.HomeFlag);
        if (root.Length == 0)
        {
            return Task.FromResult(LaunchTurn.Refused($"this Kimi launch names no owned home ({AcpWords.HomeFlag}) and is refused (D19)", RefusalCause.LaunchControl));
        }

        return KimiLaunch.TakeAsync(host, request, PlacesOf(root, KimiEnvironment.FlagOf(request, AcpWords.ExecutableFlag)), launcher, ct, allowNetworkSignIn);
    }

    /// <summary>
    /// The <c>providers</c> probe's <c>--version</c>, in the BUILT environment on a throwaway owned folder made for it and deleted after it —
    /// no junction and no config (<c>kimi --version</c> writes nothing and needs no sign-in, RESULTS section 18) — never the inherited environment
    /// and never the person's home (D19, the Vibe precedent).
    /// </summary>
    public async Task<ProcessResult> ProbeVersionAsync(IProcessLauncher launcher, string executable, TimeSpan timeout, CancellationToken ct)
    {
        var root = Directory.CreateTempSubdirectory("coai-answers-").FullName;
        try
        {
            var places = PlacesOf(root, executable);
            Directory.CreateDirectory(places.Home);
            Directory.CreateDirectory(places.Os);
            KimiHome.MakeCache(places);

            return await launcher.RunAsync(KimiEnvironment.Built(new ProcessRequest(executable, ["--version"], places.Home) { Timeout = timeout }, places), ct);
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    /// <summary>Best effort: a probe folder left behind is a <c>coai-answers-*</c> folder in temp, which the product's six-hour scratch sweep removes.</summary>
    private static void DeleteQuietly(string folder)
    {
        try
        {
            GitScratch.DeleteEvenIfReadOnly(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left for the sweep; the probe's answer is what the caller asked for.
        }
    }

    /// <summary>The card's warning for the version <c>providers</c> read — the launch control's own sentence (empty for a measured version).</summary>
    public string VersionWarning(string printedVersion) => KimiLaunch.VersionWarning(printedVersion);

    /// <summary>
    /// The card's one line when the sign-in folder is on network storage — what <c>coai.kimiAllowNetworkSignIn</c> (DL6) turns the refusal into. The CALLER asks only when the override is on: with it off, the launch's refusal says
    /// the same fact with its cure. Empty on a local disk, on a host with no mount table, and when the folder cannot be named.
    /// </summary>
    public string NetworkSignInWarning() => Junctions.NetworkWarning(host.Kind, seams.PlacesFor(string.Empty).Credentials);

    /// <summary>
    /// The survivor marker standing for the person's sign-in — zero or one — for the providers answer's <c>survivorMarker</c> (DL9, F16): the marker file of THIS sign-in folder only, in the Kimi lock root the one-shot
    /// <c>--kimi-clear-marker</c> accepts paths from. Nothing when the sign-in folder is not known or cannot be keyed.
    /// </summary>
    public IReadOnlyList<KimiMarkerReport> StandingMarker() =>
        seams.PlacesFor(string.Empty).Credentials is { Length: > 0 } credentials
            ? KimiSurvivorMarker.StandingReportFor(KimiCredentialsLock.MarkerPathFor(credentials), LivenessCheck.Of)
            : [];

    /// <summary>The report's answer when the turn was answered; null — "no answer where this vendor puts one" — otherwise.</summary>
    public string? ReadAnswer(ReviewerInvocation invocation, ProcessResult result) =>
        KimiReport.Parse(result.StdOut) is { Answered: true } report ? report.Answer : null;

    /// <summary>The report's session totals; unknown, never zero, when it is unreadable or the shim could not capture them — and never a cost (D9).</summary>
    public Usage ReadUsage(ReviewerInvocation invocation, ProcessResult result) => KimiReport.Parse(result.StdOut).Usage;

    /// <summary>
    /// Why the run failed, in the shim's own words: its failure name (or the end that is itself a verdict) and its reason — led by the cure when the
    /// name is one a person can act on. An unreadable stdout adds nothing; the stderr tail says it.
    /// </summary>
    public string WhyItFailed(ReviewerInvocation invocation, ProcessResult result) =>
        KimiReport.Parse(result.StdOut) is { Readable: true } report ? Said(report) : string.Empty;

    /// <summary>The shim's own words for the failure: <c>name: reason</c>, or the reason alone for an end that has no name.</summary>
    private static string Words(KimiReport report) => report.Label.Length > 0 ? $"{report.Label}: {report.Reason}" : report.Reason;

    private static string Said(KimiReport report)
    {
        var text = Quoted(Words(report), report);

        // The cure FIRST: the summary cuts a line at 160 characters, and the shim's reason (a list of offered models) can be longer than that.
        return VendorDiagnosis.For(text) is { } cure ? $"{cure} (the shim said: {text})" : text;
    }

    /// <summary>
    /// Rate limited ONLY when the shim exited with its spent-allowance code AND its report names one of the table's Spent rows
    /// (<see cref="KimiAcpProfile.SpentNames"/> — <c>balance_spent</c> (D14) or the plan quota <c>kimi_quota_spent</c>) — the reason is the shim's own words
    /// (<see cref="SpentReason"/>) so the ladder stops, and the verdict carries the report's usage. Every other exit is an ordinary failure, whatever
    /// text its trace holds.
    /// </summary>
    public RateLimitVerdict RateLimited(ReviewerInvocation invocation, ProcessResult result)
    {
        var report = KimiReport.Parse(result.StdOut);
        Said(invocation, result, report);

        return result.ExitCode == AcpWords.SpentExit && KimiAcpProfile.SpentNames.Contains(report.Failure)
            ? RateLimitVerdict.Because(SpentReason(report), report.Usage)
            : RateLimitVerdict.No;
    }

    /// <summary>
    /// The one Debug line a Kimi launch leaves (decision 75 of the E4 plan): the shim's exit, the report's `end`, its failure name if any, and whether the usage was captured —
    /// what a live check needs to confirm exit 0 and `answered`, which no other record of a SUCCESS carries. Written from here because the executor asks this adapter
    /// to classify every launch exactly once, before judging the exit. Never the answer, the prompt or the reason: those are vendor and person text.
    /// </summary>
    private void Said(ReviewerInvocation invocation, ProcessResult result, KimiReport report)
    {
        if (!report.Readable)
        {
            log.Debug("Kimi launch {Provider}/{Role} on {Model}: exit {Exit}, no report (stdout was not the shim's)", invocation.Provider, invocation.Role, invocation.Model, result.ExitCode);

            return;
        }

        log.Debug(
            "Kimi launch {Provider}/{Role} on {Model}: exit {Exit}, end {End}{FailureText}, usage {UsageText}",
            invocation.Provider, invocation.Role, invocation.Model, result.ExitCode, report.End,
            report.Failure.Length > 0 ? ", failure " + report.Failure : string.Empty,
            report.Usage.NotCaptured ? "not captured" : "captured");
    }

    /// <summary>
    /// The reason a spent allowance carries: the shim's own words, with NO cure in front. The round's summary, the ledger and the log diagnose a reason by its marker and put the
    /// table's cure before the vendor's line themselves (<c>BoundedScheduler.Because</c>); a reason that already led with the cure was said twice on every line (S7,
    /// <c>ASpentKimiQuotaReadsAsAQuotaTests</c>). When the vendor's words alone would not stop the ladder (the sentence reached the shim on stderr only), the report's MATCHED LINE
    /// — the vendor's own line, quoted — is added; only a report with no line falls back to the table's phrases for the name.
    /// </summary>
    private static string SpentReason(KimiReport report)
    {
        var said = Words(report);

        return RateLimit.Hopeless(said) ? said : report.FailureLine.Length > 0 ? Quoted(said, report) : $"{said} ({KimiAcpProfile.SpentPhrasesOf(report.Failure)})";
    }

    /// <summary><paramref name="said"/> followed by the line of the agent's stderr the shim matched, quoted, when the reason itself did not hold the sentence; unchanged otherwise.</summary>
    private static string Quoted(string said, KimiReport report) =>
        report.FailureLine.Length > 0 ? $"{said} (Kimi said: {report.FailureLine})" : said;
}
