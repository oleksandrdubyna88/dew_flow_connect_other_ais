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
internal sealed record RoleLaunched(ReviewerLaunch? Launch, ConsultFailure? Refused, IConsultantRuntime Runtime, ReviewerInvocation? Invocation, string Handle, CoderOsStop? OsStop = null, string KeyId = "", RoleStop? KeyStop = null, bool KeyExposed = false);

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
internal sealed class RoleModelLauncher(Func<string, string?> settings, IProcessTracker tracker)
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

    /// <summary>The settings key naming the CLI of <paramref name="vendor"/>.</summary>
    public static string CliKey(string vendor) => $"COAI_ROLE_{vendor.ToUpperInvariant()}_CLI";

    /// <summary>The vendors a route may name: the three CLIs of the vendor-routing rule.</summary>
    public static bool Knows(string vendor) => vendor is "claude" or "codex" or "agy";

    /// <param name="key">What the launch runs on (epoch 3 E2 s7 t5): a pool key, whose text becomes the child's environment variable <c>CLAUDE_CODE_OAUTH_TOKEN</c> and nothing else; <see cref="RoleKey.Gone"/>, which makes the launch a stop with no process; or null / <see cref="RoleKey.None"/> /
    /// <see cref="RoleKey.PersonSignIn"/> for a launch with no key — today's, with the person's own sign-in.</param>
    public async Task<RoleLaunched> LaunchAsync(ModelRef model, string prompt, string repoPath, string outputDir, TimeSpan timeout, CancellationToken ct, string handle = "", RoleKey? key = null)
    {
        var (runtime, name) = RuntimeOf(model.Vendor);
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
            Confinement = Coder is null ? LaunchConfinement.AsShipped : Coder with { KeyId = keyId, Resume = handle.Length == 0 ? CoderHandle.None : CoderHandle.Bind(Coder with { KeyId = keyId }, handle) },
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
            var (shown, exposed) = pooled is null ? (finished, false) : Scrub(finished, pooled.Secret);

            return new RoleLaunched(shown, null, runtime, WithoutCredential(invocation), shown.Process is { } process ? runtime.ReadHandle(process) : string.Empty, KeyId: keyId, KeyExposed: exposed);
        }

        return await LaunchCoderAsync(runtime, invocation, pooled?.Secret, keyId, ct);
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
    private async Task<RoleLaunched> LaunchCoderAsync(IConsultantRuntime runtime, ReviewerInvocation invocation, ClaudeKeySecret? secret, string keyId, CancellationToken ct)
    {
        var turn = new CoderTurnLauncher(Layer!);
        var finished = await new ReviewerExecutor(turn).LaunchOnceAsync(invocation, ct);
        var stop = turn.Outcome is { } outcome ? CoderOsLayer.StopOf(outcome) : null;
        var (shown, exposed) = secret is null ? (finished, false) : Scrub(finished, secret);
        var handle = shown.Process is { } process ? runtime.ReadHandle(process) : string.Empty;

        return new RoleLaunched(stop is { Started: false } ? null : shown, null, runtime, WithoutCredential(invocation), handle, stop, KeyId: keyId, KeyExposed: exposed);
    }

    /// <summary>
    /// What the launch printed, with the exact text of its own key replaced by the key's mark (<see cref="ClaudeKeySecret.Marker"/>) — in the answer, the evidence, both streams and the words of the outcome — before anything is stored or read. The second
    /// value says whether the text was found at all: a CLI that printed its credential is exposed, whatever the scrub then made of it.
    /// </summary>
    private static (ReviewerLaunch Launch, bool Found) Scrub(ReviewerLaunch launch, ClaudeKeySecret secret)
    {
        var found = false;

        string Clean(string text)
        {
            var cleaned = secret.Scrub(text);
            found |= !ReferenceEquals(cleaned, text) && !string.Equals(cleaned, text, StringComparison.Ordinal);

            return cleaned;
        }

        var process = launch.Process is { } raw ? raw with { StdOut = Clean(raw.StdOut), StdErr = Clean(raw.StdErr) } : null;
        var terminal = launch.Terminal switch
        {
            ReviewerOutcome.NonZeroExit exit => exit with { StdErrTail = Clean(exit.StdErrTail), FailureReason = Clean(exit.FailureReason) },
            ReviewerOutcome.Unparseable unparseable => unparseable with { Reason = Clean(unparseable.Reason) },
            ReviewerOutcome.RateLimited limited => limited with { Reason = Clean(limited.Reason) },
            ReviewerOutcome.NotStarted notStarted => notStarted with { Reason = Clean(notStarted.Reason) },
            ReviewerOutcome.StoodDown stoodDown => stoodDown with { Reason = Clean(stoodDown.Reason) },
            var other => other,
        };
        var cleaned = launch with
        {
            Answer = launch.Answer is null ? null : Clean(launch.Answer),
            Evidence = Clean(launch.Evidence),
            NotApplied = Clean(launch.NotApplied),
            Warning = Clean(launch.Warning),
            Process = process,
            Terminal = terminal,
        };

        return (cleaned, found);
    }

    /// <summary>The consultant runtime of a vendor and the runtime name its install cure is read by.</summary>
    public static (IConsultantRuntime Runtime, string Name) RuntimeOf(string vendor) => vendor switch
    {
        "claude" => (new ClaudeConsultant(new ClaudeRuntime(), "claude"), "claude"),
        "codex" => (new CodexConsultant(new CodexRuntime(), "codex"), "codex"),
        _ => (new AntigravityConsultant(new AntigravityRuntime("agy"), "agy"), "antigravity"),
    };
}
