using System.Text.RegularExpressions;
using CoaiMcp.Core.Notices;
using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;
using CoaiMcp.Server.Keys;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What one launch of one model of a route came to: a refusal before any launch, or the launch.</summary>
/// <param name="Launch">The finished launch — null when the CLI could not even be prepared.</param>
/// <param name="Refused">The classified failure of a preparation that refused (a CLI that cannot be started, or that cannot confine its reads); null otherwise.</param>
/// <param name="Runtime">The consultant runtime that built the launch — it reads the handle and classifies the stop.</param>
/// <param name="Invocation">The invocation the launch ran, for the ledger line; null when nothing was built.</param>
/// <param name="Handle">The vendor's own session id, read off the launch, for a role that continues the conversation (the BA's repair turns); empty when the vendor named none.</param>
/// <param name="OsStop">Set for a coder launch the OS layer stopped (<see cref="CoderOsStop"/>): a write it refused, or a launch it refused to start. The launch ends the job whatever the coder answered; null for every other launch.</param>
/// <param name="KeyId">The id of the claude key the launch ran on (<see cref="RoleKey.KeyId"/>): a pool key's id, <c>person-sign-in</c>, or empty. What the attempt, the ledger line and the resume handle carry.</param>
/// <param name="KeyStop">Set when no launch was made because the key of a RESUMED session is gone (<see cref="RoleKey.Gone"/>): the stop the loop reads in place of a launch. Null otherwise.</param>
/// <param name="KeyExposed">True when the exact text of the launch's pool key was found in what it printed (and was replaced there by the key's mark): the key is dead and the job ends <c>key-in-output</c>.</param>
internal sealed record RoleLaunched(ReviewerLaunch? Launch, ConsultFailure? Refused, IConsultantRuntime Runtime, ReviewerInvocation? Invocation, string Handle, CoderOsStop? OsStop = null, string KeyId = "", RoleStop? KeyStop = null, bool KeyExposed = false)
{
    /// <summary>The ids of OTHER pool keys whose exact text was found in what the launch printed (the pattern of a setup-token, whoever's it is): each is exposed too, and the job ends <c>key-in-output</c>.</summary>
    public IReadOnlyList<string> ExposedIds { get; init; } = [];

    /// <summary>The CLI release the preparation of the launch read (its own <c>--version</c>); empty when none was asked or none read. A RECORD of the attempt, never a condition.</summary>
    public string CliVersion { get; init; } = string.Empty;
}

/// <summary>
/// Launches ONE model of a role route through its vendor's own CLI (spec A, "Brief": through the consultant adapters, <c>IConsultantRuntime.Build</c> and <c>ReadHandle</c>) — read-only, in the repository, the prompt on
/// stdin, the CLI child recorded in <c>job.json</c> by the tracker the runner handed in, so a cancel or a sweep can stop it by identity.
/// </summary>
/// <remarks>
/// <para><b>A fresh session, unless the role asks to continue one.</b> A launch carries no handle by default: a role that fails over restarts from turn 0 on the next model (a read-only role has nothing to carry). The handle read
/// off the launch is returned, and a role that repairs its own answer (the BA) passes it back as <c>handle</c> to put the next turn in the SAME session of the SAME model — never across models.</para>
/// <para><b>Where each CLI is.</b> <c>COAI_ROLE_CLAUDE_CLI</c>, <c>COAI_ROLE_CODEX_CLI</c> and <c>COAI_ROLE_AGY_CLI</c> in the layered settings name an executable path; unset, the CLI is found on the PATH by its own name.
/// The three names carry the <c>COAI_ROLE_</c> prefix, so a start hands them to its runner through <c>env.json</c>.</para>
/// </remarks>
internal sealed partial class RoleModelLauncher(Func<string, string?> settings, IProcessTracker tracker)
{
    /// <summary>
    /// Set for a coder (epoch 3 E2 s3 t3): the launch is the WRITE launch of this job in its worktree, not a read-only consult. A handle passed to <see cref="LaunchAsync"/> is then bound to this job and worktree
    /// (<see cref="CoderHandle.Bind"/>) and travels as the coder's own resume, never as the launch's handle.
    /// </summary>
    public LaunchConfinement.Coder? Coder { get; init; }

    /// <summary>
    /// The OS layer a coder launch runs inside (<see cref="CoderOsLayer"/>) — the ONE road: with <see cref="Coder"/> set and no layer the launch is refused before anything starts, so a coder can never run unconfined by a missing argument.
    /// The capability probes (<c>--version</c>, <c>--help</c>) write nothing and run as they always did; the coder's own process is the layer's.
    /// </summary>
    public CoderOsLayer? Layer { get; init; }

    /// <summary>The vendors a coder launch may go to (epoch 3 E3 s7 t2): the seam of each, and the table that says which ship. The product's own set unless a test injects one.</summary>
    internal CoderVendorSet Vendors { get; init; } = CoderVendors.Shipped;

    /// <summary>The settings key naming the CLI of <paramref name="vendor"/>.</summary>
    public static string CliKey(string vendor) => $"COAI_ROLE_{vendor.ToUpperInvariant()}_CLI";

    /// <summary>The vendors a route may name: the three CLIs of the vendor-routing rule.</summary>
    public static bool Knows(string vendor) => vendor is "claude" or "codex" or "agy";

    /// <param name="key">What the launch runs on (epoch 3 E2 s7 t5): a pool key, whose text becomes the child's environment variable <c>CLAUDE_CODE_OAUTH_TOKEN</c> and nothing else; <see cref="RoleKey.Gone"/>, which makes the launch a stop with no process; or null / <see cref="RoleKey.None"/> /
    /// <see cref="RoleKey.PersonSignIn"/> for a launch with no key — today's, with the person's own sign-in.</param>
    public async Task<RoleLaunched> LaunchAsync(ModelRef model, string prompt, string repoPath, string outputDir, TimeSpan timeout, CancellationToken ct, string handle = "", RoleKey? key = null)
    {
        var (runtime, name) = RuntimeOf(model.Vendor);
        // A vendor whose sign-in lives for a limited time caps the deadline of every turn of it, so that a turn ends before its token does.
        if (Coder is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Launchable capped && capped.Vendor.TurnDeadline is { } cap && cap < timeout)
        {
            timeout = cap;
        }

        if (Coder is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Refused unshipped)
        {
            // A vendor the table does not ship is never launched as a coder: the refusal names the shipped ones, and the failover goes on to the next model.
            return new RoleLaunched(null, new ConsultFailure.LaunchRefused(unshipped.Sentence), runtime, null, string.Empty);
        }

        if (Coder is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Launchable launchable)
        {
            runtime = launchable.Vendor.Runtime;
        }

        if (key is RoleKey.Gone gone)
        {
            return new RoleLaunched(null, null, runtime, null, string.Empty, KeyStop: new RoleStop(RoleStopWords.Limit, Redaction.SafeText(gone.Sentence, Redaction.TitleLimit), "key-gone", SkipsVendor: false, Transient: false));
        }

        if (Coder is not null && Layer is null)
        {
            return new RoleLaunched(null, new ConsultFailure.LaunchRefused("a coder launch runs only inside the OS layer, and none was given; nothing was launched"), runtime, null, string.Empty);
        }

        var pooled = key as RoleKey.Pool;
        var keyId = key?.KeyId ?? string.Empty;
        var launcher = new ProcessLauncher(tracker);
        var launch = new ConsultantLaunch(repoPath, prompt, Coder is null ? handle : string.Empty, outputDir, new ReviewerSettings(model.Vendor)
        {
            ExecutablePath = settings(CliKey(model.Vendor)) ?? string.Empty,
            Model = model.Model,
            Timeout = timeout,
            McpServersToSwitchOff = NoMcpServers.CodexConfigured(settings),
        })
        {
            Confinement = Coder is null ? LaunchConfinement.AsShipped : Coder with { KeyId = keyId, Vendor = model.Vendor, WorkFolder = Layer is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Launchable found ? found.Vendor.WorkingDirectory(Layer.Job) : string.Empty, Resume = handle.Length == 0 ? CoderHandle.None : CoderHandle.Bind(Coder with { KeyId = keyId, Vendor = model.Vendor }, handle) },
            // The ONE place the text of a pool key is taken: it goes into the credential, which puts it in the child's environment and nowhere else.
            Credential = pooled is null ? null : new LaunchCredential(pooled.Secret.Id, pooled.Secret.Reveal()),
            OwnHome = pooled is null || Coder is not null ? string.Empty : Path.Combine(outputDir, "claude-home"),
        };
        Directory.CreateDirectory(outputDir);
        if (launch.OwnHome.Length > 0)
        {
            CreateHome(launch.OwnHome);
        }

        var prepared = await runtime.PrepareAsync(launch, launcher, ct);
        if (prepared is ConsultantPreparation.Refused refused)
        {
            return new RoleLaunched(null, refused.Failure, runtime, null, string.Empty, KeyId: keyId);
        }

        var invocation = runtime.Build(((ConsultantPreparation.Ready)prepared).Launch);
        if (Coder is null)
        {
            var finished = await new ReviewerExecutor(launcher).LaunchOnceAsync(invocation, ct);
            var scrubbed = pooled is null ? new Scrubbed(finished, false, []) : Scrub(finished, pooled.Secret);
            var inHome = pooled is not null && HoldsTheKey(pooled.Secret, launch.OwnHome);

            return new RoleLaunched(scrubbed.Launch, null, runtime, WithoutCredential(invocation), scrubbed.Launch.Process is { } process ? runtime.ReadHandle(process) : string.Empty, KeyId: keyId, KeyExposed: scrubbed.OwnFound || inHome) { ExposedIds = scrubbed.OtherIds };
        }

        return (await LaunchCoderAsync(runtime, invocation, pooled?.Secret, keyId, outputDir, (Vendors.Resolve(model.Vendor) as CoderVendorResolution.Launchable)?.Vendor ?? CoderVendors.Claude, ct)) with { CliVersion = ((ConsultantPreparation.Ready)prepared).CliVersion };
    }

    /// <summary>
    /// Reads the live list of models of the installed codex CLI (<c>codex debug models</c>: a network call, no model call; epoch 3 E3 s2 t5, SPEC_CHANGES Q1 item 2) with the SAME executable, home and environment as the launch that follows: the invocation is the one <see cref="LaunchAsync"/> would build for
    /// <paramref name="model"/>, with its arguments replaced. A coder's list is read in a unit with the property list of the launch and the job's own <c>CODEX_HOME</c> (<see cref="CoderOsLayer.ModelFreeAsync"/>), never with the real home. It has a deadline and a size limit
    /// (<see cref="CodexModelList"/>). The launch that comes back is read by the caller as any launch is, so a missing CLI, a lost sign-in and a spent quota keep their own words; no ledger line is made for it, because no model was called.
    /// </summary>
    public async Task<RoleLaunched> ListCodexModelsAsync(ModelRef model, string repoPath, string outputDir, CancellationToken ct)
    {
        var (runtime, _) = RuntimeOf(model.Vendor);
        ICoderVendor? vendor = null;
        if (Coder is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Refused unshipped)
        {
            return new RoleLaunched(null, new ConsultFailure.LaunchRefused(unshipped.Sentence), runtime, null, string.Empty);
        }

        if (Coder is not null && Vendors.Resolve(model.Vendor) is CoderVendorResolution.Launchable launchable)
        {
            runtime = launchable.Vendor.Runtime;
            vendor = launchable.Vendor;
        }

        if (Coder is not null && Layer is null)
        {
            return new RoleLaunched(null, new ConsultFailure.LaunchRefused("a coder launch runs only inside the OS layer, and none was given; nothing was launched"), runtime, null, string.Empty);
        }

        var launcher = new ProcessLauncher(tracker);
        var launch = new ConsultantLaunch(repoPath, string.Empty, string.Empty, outputDir, new ReviewerSettings(model.Vendor)
        {
            ExecutablePath = settings(CliKey(model.Vendor)) ?? string.Empty,
            Model = model.Model,
            Timeout = CodexModelList.Deadline,
            McpServersToSwitchOff = NoMcpServers.CodexConfigured(settings),
        })
        {
            Confinement = Coder is null ? LaunchConfinement.AsShipped : Coder with { KeyId = string.Empty, Vendor = model.Vendor, WorkFolder = vendor is not null ? vendor.WorkingDirectory(Layer!.Job) : string.Empty, Resume = CoderHandle.None },
        };
        Directory.CreateDirectory(outputDir);
        var prepared = await runtime.PrepareAsync(launch, launcher, ct);
        if (prepared is ConsultantPreparation.Refused refused)
        {
            return new RoleLaunched(null, refused.Failure, runtime, null, string.Empty);
        }

        var ready = (ConsultantPreparation.Ready)prepared;

        var built = runtime.Build(ready.Launch);
        var listing = built with { Request = built.Request with { Arguments = CodexModelList.Arguments, StdIn = string.Empty, Timeout = CodexModelList.Deadline, MaxOutputChars = CodexModelList.MostChars + 1 }, OutputFile = string.Empty };
        if (Coder is null)
        {
            return new RoleLaunched(await new ReviewerExecutor(launcher).LaunchOnceAsync(listing, ct), null, runtime, listing, string.Empty) { CliVersion = ready.CliVersion };
        }

        return (await LaunchCoderAsync(runtime, listing, null, string.Empty, outputDir, vendor!, ct, modelFree: true)) with { CliVersion = ready.CliVersion };
    }

    /// <summary>The folders a CLI with its own home expects to find (<c>HOME</c>, the four <c>XDG_*</c> folders, the CLI's own configuration folder), made before the launch so a CLI that does not create them still starts.</summary>
    private static void CreateHome(string home)
    {
        foreach (var folder in ClaudeConsultant.OwnHomeEnvironment(home).Where(pair => pair.Key != "HOME").Select(pair => pair.Value))
        {
            Directory.CreateDirectory(folder);
        }
    }

    /// <summary>The invocation without the explicit environment of the launch: the record the ledger and the result keep is about WHAT ran, and must never carry the text of a credential.</summary>
    private static ReviewerInvocation WithoutCredential(ReviewerInvocation invocation) =>
        invocation.Request.Environment.ContainsKey(ClaudeConsultant.KeyVariable)
            ? invocation with { Request = invocation.Request with { Environment = new Dictionary<string, string?>() } }
            : invocation;

    /// <summary>
    /// The coder's launch: the same executor and classification, with the process itself started by the layer. A write the layer stopped, or a launch it refused, is on the result as <see cref="RoleLaunched.OsStop"/>; a refused launch
    /// has no <see cref="RoleLaunched.Launch"/> — nothing ran, so nothing is read and no ledger line is made.
    /// </summary>
    private async Task<RoleLaunched> LaunchCoderAsync(IConsultantRuntime runtime, ReviewerInvocation invocation, ClaudeKeySecret? secret, string keyId, string outputDir, ICoderVendor vendor, CancellationToken ct, bool modelFree = false)
    {
        var turn = new CoderTurnLauncher(Layer!, modelFree ? string.Empty : outputDir, vendor) { ModelFree = modelFree };
        var finished = await new ReviewerExecutor(turn).LaunchOnceAsync(invocation, ct);
        var stop = turn.Outcome is { } outcome ? CoderOsLayer.StopOf(outcome) : null;
        var keyScrubbed = secret is null ? new Scrubbed(finished, false, []) : Scrub(finished, secret);
        // The tokens of the job's sign-in copies are scrubbed the way a pool key is (M9); a token printed ends the job, whatever else the turn did.
        var (tokenScrubbed, tokenHits) = ScrubTokens(keyScrubbed.Launch, Layer!.SignInTokens());
        var scrubbed = keyScrubbed with { Launch = tokenScrubbed };
        stop = tokenHits.Count > 0
            ? new CoderOsStop("sign-in-token-printed", $"the coder printed the token of the sign-in copy made for this job ({string.Join(", ", tokenHits)}), so nothing of the turn was kept and the job stops; the copy is deleted with the job and the vendor's own sign-in is the person's", Started: true)
            : stop;
        var handle = scrubbed.Launch.Process is { } process ? runtime.ReadHandle(process) : string.Empty;
        var job = Layer!.Job;
        var inHome = secret is not null && HoldsTheKey(secret, vendor.Home(job).Home, job.CoderTempOf(vendor.Id), job.CoderVarTempOf(vendor.Id));

        return new RoleLaunched(stop is { Started: false } ? null : scrubbed.Launch, null, runtime, WithoutCredential(invocation), handle, stop, KeyId: keyId, KeyExposed: scrubbed.OwnFound || inHome) { ExposedIds = scrubbed.OtherIds };
    }

    /// <summary>
    /// Whether the exact text of <paramref name="secret"/> is in any file of the folders a keyed launch had to itself — the CLI's own home (its transcripts and configuration) and its temp folders — searched AFTER the launch ended: a CLI that wrote its credential
    /// somewhere it keeps files is exposed even though it printed nothing (second judgement). A folder that is not there holds nothing.
    /// </summary>
    private static bool HoldsTheKey(ClaudeKeySecret secret, params string[] folders) =>
        folders.Any(folder => folder.Length > 0 && Directory.Exists(folder) && KeyScanner.Walk(folder, [secret], string.Empty).Hits.Count > 0);

    /// <summary>What a scrub made of a launch: the launch, whether the launch's OWN key was found in it, and the ids of any OTHER pool key whose text was.</summary>
    private sealed record Scrubbed(ReviewerLaunch Launch, bool OwnFound, IReadOnlyList<string> OtherIds);

    /// <summary>The shape of every setup-token (<c>sk-ant-oat…</c>, the CLI's own test): the text of ANY pool key, not only this launch's, is removed from what a launch printed.</summary>
    [GeneratedRegex("sk-ant-oat[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex SetupTokenShape();

    /// <summary>
    /// What the launch printed, with the exact text of its own key replaced by the key's mark (<see cref="ClaudeKeySecret.Marker"/>) — in the answer, the evidence, both streams and the words of the outcome — before anything is stored or read. The second
    /// value says whether the text was found at all: a CLI that printed its credential is exposed, whatever the scrub then made of it.
    /// </summary>
    private static Scrubbed Scrub(ReviewerLaunch launch, ClaudeKeySecret secret)
    {
        var own = false;
        var others = new List<string>();

        string Clean(string text)
        {
            var cleaned = secret.Scrub(text);
            own |= !string.Equals(cleaned, text, StringComparison.Ordinal);

            // Any other setup-token shape: its text is gone from the output too, and the pool key it belongs to (if it is one) is exposed.
            return SetupTokenShape().Replace(cleaned, match =>
            {
                var id = KeyIds.Of(match.Value);
                if (!others.Contains(id, StringComparer.Ordinal))
                {
                    others.Add(id);
                }

                return "[claude key text removed]";
            });
        }

        var rewritten = Rewrite(launch, Clean);

        return new Scrubbed(rewritten, own, [.. others.Where(id => !string.Equals(id, secret.Id, StringComparison.Ordinal))]);
    }

    /// <summary>The launch with <paramref name="clean"/> applied to every text of it a person or the record could read: the answer, the evidence, both streams, and the words of the outcome.</summary>
    private static ReviewerLaunch Rewrite(ReviewerLaunch launch, Func<string, string> clean)
    {
        var process = launch.Process is { } raw ? raw with { StdOut = clean(raw.StdOut), StdErr = clean(raw.StdErr) } : null;
        var terminal = launch.Terminal switch
        {
            ReviewerOutcome.NonZeroExit exit => exit with { StdErrTail = clean(exit.StdErrTail), FailureReason = clean(exit.FailureReason) },
            ReviewerOutcome.Unparseable unparseable => unparseable with { Reason = clean(unparseable.Reason) },
            ReviewerOutcome.RateLimited limited => limited with { Reason = clean(limited.Reason) },
            ReviewerOutcome.NotStarted notStarted => notStarted with { Reason = clean(notStarted.Reason) },
            ReviewerOutcome.StoodDown stoodDown => stoodDown with { Reason = clean(stoodDown.Reason) },
            var other => other,
        };

        return launch with
        {
            Answer = launch.Answer is null ? null : clean(launch.Answer),
            Evidence = clean(launch.Evidence),
            NotApplied = clean(launch.NotApplied),
            Warning = clean(launch.Warning),
            Process = process,
            Terminal = terminal,
        };
    }

    /// <summary>
    /// What the launch printed with the exact text of every token of the job's sign-in copies (<see cref="CoderOsLayer.SignInTokens"/>) replaced by a mark that names the copy's id, and the ids found (the fourth security verify of s7 t6, M9): the same scrub a pool key gets, applied before anything of the turn is
    /// stored, read or committed. A hit says a coder printed its copy's token; the job ends <c>coder-boundary</c>.
    /// </summary>
    private static (ReviewerLaunch Launch, IReadOnlyList<string> Hits) ScrubTokens(ReviewerLaunch launch, IReadOnlyList<ClaudeKeySecret> tokens)
    {
        if (tokens.Count == 0)
        {
            return (launch, []);
        }

        var hits = new List<string>();

        string Clean(string text)
        {
            foreach (var token in tokens)
            {
                var cleaned = token.ScrubAsSignInToken(text);
                if (!string.Equals(cleaned, text, StringComparison.Ordinal) && !hits.Contains(token.Id, StringComparer.Ordinal))
                {
                    hits.Add(token.Id);
                }

                text = cleaned;
            }

            return text;
        }

        return (Rewrite(launch, Clean), hits);
    }

    /// <summary>The consultant runtime of a vendor and the runtime name its install cure is read by.</summary>
    public static (IConsultantRuntime Runtime, string Name) RuntimeOf(string vendor) => vendor switch
    {
        "claude" => (new ClaudeConsultant(new ClaudeRuntime(), "claude"), "claude"),
        "codex" => (new CodexConsultant(new CodexRuntime(), "codex"), "codex"),
        _ => (new AntigravityConsultant(new AntigravityRuntime("agy"), "agy"), "antigravity"),
    };
}
