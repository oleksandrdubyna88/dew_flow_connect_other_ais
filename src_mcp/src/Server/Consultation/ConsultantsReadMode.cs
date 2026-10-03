using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Platform;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server;

/// <summary>
/// Gathers what <c>--consultants</c> reports: per caller kind, the consultant it resolves to, its CLI's probe, what its CLI
/// says about the flags its argv depends on, both health files and the check — the IO half, ending in
/// <see cref="ConsultantsReport.Row"/>.
/// </summary>
/// <remarks>
/// <para><b>Probed on the RESOLVED definition</b> — never a reviewer row by name — with the probe the reviewers'
/// <c>--providers</c> uses (<see cref="VendorProbe"/>), always as ENABLED: a consultant borrowing a disabled reviewer
/// row is still the consultant (<see cref="ConsultantResolver"/>, rule a), and "disabled in settings" would describe
/// the review gate, not it. Asked with the RESOLVED model too, as <c>--providers</c> asks a reviewer — a local or api
/// consultant asked with none read "no model" whatever its definition said. Asked once per distinct launch — runtime,
/// executable and model — and all at once, each under its own timeout and all under one ceiling, so a hung CLI costs
/// one bounded wait, not four.</para>
/// <para><b>The auth source is the TURN's</b> (the whole-branch review of PLAN_the_consultant_works_on_every_vendor.md,
/// 2026-10-03, B): a consultation never hands its CLI a vault key — <see cref="ConsultantTurnInputs.Settings"/> sets
/// none; only the reviewers' roster does — so every consultant is probed with no key, and its row never says
/// <c>vault key</c> for a key its turn does not read. Wiring keys into consultations is the operator's decision, not
/// this report's.</para>
/// <para><b>Only an available row is probed</b>: a consultant <c>consult</c> would refuse is reported with the
/// refusal, and nothing is run for it.</para>
/// </remarks>
internal sealed class ConsultantsSurvey(ConsultantParts parts)
{
    /// <summary>How long one CLI has to say its version here — the panel waits on this answer.</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The ceiling over every probe of one survey: a version probe, and a claude's two help asks.</summary>
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(60);

    /// <summary>A consultation sends its CLI no vault key, so its auth is probed as having none (see the remarks).</summary>
    private const bool TurnCarriesAVaultKey = false;

    /// <summary>The answer as of <paramref name="nowUtc"/> on <paramref name="host"/>.</summary>
    /// <exception cref="IOException">The data directory's health directory cannot be made or listed — exit 74.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled — the answer would be half of one.</exception>
    public async Task<ConsultantsAnswer> AnswerAsync(HostKind host, DateTime nowUtc, CancellationToken ct)
    {
        var health = new ConsultHealthStore(parts.Settings.DataDir, parts.Warn);
        Readable(health.Directory);
        var checks = new ConsultCheckStore(parts.Settings.DataDir);
        var surveyed = CallerIdentity.Kinds.Select(Surveyed).ToList();
        var probed = surveyed.OfType<KindSurvey.Probed>().ToList();
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        bounded.CancelAfter(Ceiling);
        var probes = probed.Select(row => row.Key).Distinct().ToDictionary(key => key, key => Probe(key, bounded.Token, ct));
        var capabilities = probed.Select(row => row.CapabilityExecutable).Where(exe => exe.Length > 0).Distinct(StringComparer.Ordinal)
            .ToDictionary(exe => exe, exe => Capability(exe, bounded.Token, ct), StringComparer.Ordinal);
        await Task.WhenAll([.. probes.Values, .. capabilities.Values]);

        return new ConsultantsAnswer
        {
            Utc = ConsultationStore.Stamp(nowUtc),
            Side = HostKinds.Word(host),
            Distro = host == HostKind.Wsl ? Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") ?? string.Empty : string.Empty,
            Consultants = [.. surveyed.Select(row => ConsultantsReport.Row(
                Finished(row, probes, capabilities),
                new ConsultantOutcomes(health.LastAnswer(row.Kind), health.LastFailure(row.Kind), checks.Judge(row.Kind)),
                host,
                ExpandHome))],
        };
    }

    /// <summary>One caller kind before anything is probed — a closed union, so "not resolved", "not probed" and "probed" are three cases, not nulls.</summary>
    private abstract record KindSurvey(string Kind, ConsultPreflight Preflight)
    {
        /// <summary>The entry resolves to no vendor (or the routing cannot be read).</summary>
        public sealed record Unresolved(string Kind, ConsultPreflight Preflight) : KindSurvey(Kind, Preflight);

        /// <summary>A vendor <c>consult</c> would refuse — reported with the refusal, nothing run for it.</summary>
        /// <param name="HasCapability">Its argv would depend on its CLI's help (claude) — reported <c>unknown</c>, since nobody asked.</param>
        public sealed record Refused(string Kind, ConsultPreflight Preflight, ProviderSettings Vendor, bool HasCapability) : KindSurvey(Kind, Preflight);

        /// <summary>An available vendor, the probe that asks its CLI, and the executable whose help its argv depends on (or empty).</summary>
        public sealed record Probed(string Kind, ConsultPreflight Preflight, ProviderSettings Vendor, ProbeKey Key, string CapabilityExecutable) : KindSurvey(Kind, Preflight);
    }

    /// <summary>What makes two probes the same question: the launch (runtime + executable) and its model.</summary>
    private sealed record ProbeKey(VendorIdentity Vendor, string Executable, string Model);

    private KindSurvey Surveyed(string kind)
    {
        var preflight = parts.Consultations.Preflight(kind);

        return (Resolve(kind), preflight.Available) switch
        {
            (ResolvedConsultant.Definition definition, true) => new KindSurvey.Probed(
                kind, preflight, definition.Vendor, KeyOf(definition.Vendor), CapabilityExecutableOf(definition.Vendor)),
            (ResolvedConsultant.Definition definition, false) => new KindSurvey.Refused(
                kind, preflight, definition.Vendor, CapabilityExecutableOf(definition.Vendor).Length > 0),
            _ => new KindSurvey.Unresolved(kind, preflight),
        };
    }

    /// <summary>The entry's meaning — none at all when the routing cannot be read: the shipped map is not what the person chose.</summary>
    private ResolvedConsultant Resolve(string kind) =>
        parts.Settings.ConsultantsUnreadable
            ? new ResolvedConsultant.Unavailable("the consultant routing cannot be read")
            : ConsultantResolver.Resolve(ConsultantRouting.For(parts.Settings.Consultants, kind), kind, parts.Settings.Providers);

    /// <summary>The kind as the report reads it, its probes answered.</summary>
    private static SurveyedConsultant Finished(KindSurvey row, Dictionary<ProbeKey, Task<VendorHealth>> probes, Dictionary<string, Task<ClaudeCapability>> capabilities) => row switch
    {
        KindSurvey.Probed probed => new SurveyedConsultant.Probed(row.Kind, row.Preflight, probed.Vendor, probes[probed.Key].Result,
            capabilities.TryGetValue(probed.CapabilityExecutable, out var said) ? new CliCapability.Claude(said.Result) : new CliCapability.NotApplicable()),
        KindSurvey.Refused refused => new SurveyedConsultant.Refused(row.Kind, row.Preflight, refused.Vendor,
            refused.HasCapability ? new CliCapability.NotAsked() : new CliCapability.NotApplicable()),
        _ => new SurveyedConsultant.Unresolved(row.Kind, row.Preflight),
    };

    /// <summary>The model the probe is asked about is the RESOLVED one, as <c>--providers</c> asks a reviewer's — a local or api
    /// consultant probed with none always read "no model" (epic 4's code round).</summary>
    private static ProbeKey KeyOf(ProviderSettings vendor) => new(vendor.Identity(), vendor.ExecutablePath, vendor.Model);

    /// <summary>
    /// The executable whose help this consultant's argv depends on — asked of its ADAPTER, the member its turn's
    /// <c>PrepareAsync</c> uses (claude) — or empty: every other vendor, and one with no consultant adapter.
    /// </summary>
    private static string CapabilityExecutableOf(ProviderSettings vendor) =>
        ConsultantResolution.For(vendor.Identity())?.CapabilityExecutable(vendor.ExecutablePath) ?? string.Empty;

    private Task<VendorHealth> Probe(ProbeKey key, CancellationToken bounded, CancellationToken outer) =>
        Guarded(
            () => VendorProbe.RunAsync(parts.Launcher, key.Vendor, enabled: true, key.Executable, key.Model, TurnCarriesAVaultKey, bounded, ProbeTimeout),
            why => new VendorHealth(true, false, string.Empty, string.Empty, $"the probe did not finish: {why}"),
            outer);

    private Task<ClaudeCapability> Capability(string executable, CancellationToken bounded, CancellationToken outer) =>
        Guarded(
            () => ClaudeCapability.ProbeAsync(parts.Launcher, executable, Environment.CurrentDirectory, bounded),
            why => ClaudeCapability.Unknown($"claude --help was not answered: {why}"),
            outer);

    /// <summary>
    /// A probe that ran out of the survey's ceiling is an answer saying so — never the reason the survey failed. A
    /// cancellation of the OUTER token (the server shutting down) is not: it flies, so nothing half-answered is written.
    /// </summary>
    private static async Task<T> Guarded<T>(Func<Task<T>> probe, Func<string, T> unanswered, CancellationToken outer)
    {
        try
        {
            return await probe();
        }
        catch (OperationCanceledException) when (!outer.IsCancellationRequested)
        {
            return unanswered($"the survey's {Ceiling.TotalSeconds:0}-second ceiling passed");
        }
    }

    /// <summary>The health directory exists and can be listed — or the data directory is unusable, which is exit 74.</summary>
    private static void Readable(string directory)
    {
        Directory.CreateDirectory(directory);
        _ = Directory.EnumerateFileSystemEntries(directory).Take(1).ToList();
    }

    /// <summary><c>%USERPROFILE%</c> or <c>~</c> in a limitation's path, filled in with THIS side's home.</summary>
    internal static string ExpandHome(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : Environment.ExpandEnvironmentVariables(path);
}

/// <summary>
/// <c>coai-mcp --consultants</c>: per caller kind, the consultant it resolves to and what is known of it, as JSON on
/// stdout — and the same answer written to <c>&lt;dataDir&gt;/consultations/health/consultants.json</c>.
/// </summary>
/// <remarks>
/// <para><b>Why a mode.</b> The Consultant tab (epic 5) cannot call an MCP tool; it spawns one-shot modes selected by
/// <c>args[0]</c> before any transport opens (<c>.agents/PROJECT.md</c>), and reads their stdout. No model is called:
/// the CLIs are asked their <c>--version</c> (and a claude its <c>--help</c>), which is a health probe, never a
/// sign-in check — only a Check and the last failure can show that.</para>
/// <para><b>Why the file too.</b> The OTHER side — a plain Windows window reading a WSL store through
/// <c>coai.alsoWatchDataDirectories</c> — cannot run this side's binary, and the outcome files alone do not say which
/// vendor a row resolves to or what its CLI is (cadence consultation 435b1b25). So the answer is written, atomically
/// and overwritten, with the time it was taken: by this mode, and once in the background after the stdio server
/// starts (<see cref="WriteInBackground"/>).</para>
/// <para><b>Exit codes.</b> 0 with the answer — an unavailable consultant is data, with its reason. 65 for any
/// argument (the mode takes none). 74 when the data directory cannot be read. Never 64.</para>
/// </remarks>
internal static class ConsultantsReadMode
{
    /// <param name="noticing">Where a notice goes — handed down from <c>Program</c>, which composes every one of them.</param>
    internal static async Task<int> RunAsync(string[] args, Noticing noticing)
    {
        var (code, answer, why) = await AnswerAsync(
            args, Environment.GetEnvironmentVariable, new ProcessLauncher(), HostKinds.Current, Program.Note, noticing, CancellationToken.None);
        if (answer.Length > 0)
        {
            await Console.Out.WriteLineAsync(answer);
        }

        if (why.Length > 0)
        {
            Program.Note(why);
        }

        return code;
    }

    /// <summary>The exit code, stdout and stderr — the whole mode, its settings read exactly as <c>--providers</c> reads them.</summary>
    internal static async Task<(int Code, string Out, string Err)> AnswerAsync(
        string[] args, Func<string, string?> env, IProcessLauncher launcher, HostKind host, Action<string> warn, Noticing noticing, CancellationToken ct)
    {
        if (args.Length > 1)
        {
            return (65, string.Empty, $"--consultants takes no arguments; it was given: {string.Join(' ', args[1..])}"); // EX_DATAERR — never 64
        }

        try
        {
            // Layered exactly as `--providers` is (Program.ProvidersJsonCoreAsync): the panel's own settings file under the
            // environment. The vault is NOT read: a consultation sends no key, so none can be its auth (the whole-branch review, B).
            var configuration = SettingsFile.Layer(SettingsFile.DataDirFrom(env).Path, env, warn);
            var settings = PanelSettings.FromEnvironment(configuration);
            var answer = await new ConsultantsSurvey(new ConsultantParts(settings, launcher, warn, noticing)).AnswerAsync(host, DateTime.UtcNow, ct);
            // What is printed IS what is written — its rows' outcomes re-read just before the move (ConsultantsFile.Written).
            var json = ConsultantsFile.Written(settings.DataDir, answer, warn);

            return (0, json, string.Empty);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (74, string.Empty, $"the consultants could not be read: {e.Message}"); // EX_IOERR
        }
    }

    /// <summary>
    /// Takes the same survey once, in the background, after the stdio server has started, and writes it to the file —
    /// fire-and-forget, never awaited by startup, never on the protocol path, every fault caught and logged.
    /// </summary>
    /// <remarks>
    /// Nothing is printed: stdout carries the protocol here. The probes run through the server's own launcher, so a
    /// probe still running when serving ends is killed by <paramref name="stop"/> like any child of this server.
    /// </remarks>
    internal static Task WriteInBackground(PanelSettings settings, IProcessLauncher launcher, Serilog.ILogger log, Noticing noticing, CancellationToken stop) =>
        Task.Run(async () =>
        {
            try
            {
                void Warn(string problem) => log.Warning("consultants: {Problem}", problem);
                var answer = await new ConsultantsSurvey(new ConsultantParts(settings, launcher, Warn, noticing)).AnswerAsync(HostKinds.Current, DateTime.UtcNow, stop);
                // A survey that finished while serving was ending is still not written: its probes may have been cut short
                // by the same stop, and a half-answer must not replace what the other side last saw (epic 4's code round).
                stop.ThrowIfCancellationRequested();
                ConsultantsFile.Written(settings.DataDir, answer, Warn);
                log.Information("consultants: wrote {Path}", ConsultHealthPaths.ConsultantsFile(settings.DataDir));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Serving ended first, which is how a short session goes.
            }
            catch (Exception failure)
            {
                // The detached task's catch-all (reliability.md): a survey that failed is a line in the log, never a
                // fault nobody observes, and never a server that stops serving over a file for the panel.
                log.Warning(failure, "consultants: the background survey failed, so consultants.json was not refreshed");
            }
        }, CancellationToken.None);
}
