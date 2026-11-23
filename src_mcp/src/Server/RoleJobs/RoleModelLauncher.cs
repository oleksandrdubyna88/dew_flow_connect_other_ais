using CoaiMcp.Core.Roles;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Server.RoleJobs;

/// <summary>What one launch of one model of a route came to: a refusal before any launch, or the launch.</summary>
/// <param name="Launch">The finished launch — null when the CLI could not even be prepared.</param>
/// <param name="Refused">The classified failure of a preparation that refused (a CLI that cannot be started, or that cannot confine its reads); null otherwise.</param>
/// <param name="Runtime">The consultant runtime that built the launch — it reads the handle and classifies the stop.</param>
/// <param name="Invocation">The invocation the launch ran, for the ledger line; null when nothing was built.</param>
/// <param name="Handle">The vendor's own session id, read off the launch, for a role that continues the conversation (the BA's repair turns); empty when the vendor named none.</param>
internal sealed record RoleLaunched(ReviewerLaunch? Launch, ConsultFailure? Refused, IConsultantRuntime Runtime, ReviewerInvocation? Invocation, string Handle);

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

    /// <summary>The settings key naming the CLI of <paramref name="vendor"/>.</summary>
    public static string CliKey(string vendor) => $"COAI_ROLE_{vendor.ToUpperInvariant()}_CLI";

    /// <summary>The vendors a route may name: the three CLIs of the vendor-routing rule.</summary>
    public static bool Knows(string vendor) => vendor is "claude" or "codex" or "agy";

    public async Task<RoleLaunched> LaunchAsync(ModelRef model, string prompt, string repoPath, string outputDir, TimeSpan timeout, CancellationToken ct, string handle = "")
    {
        var (runtime, name) = RuntimeOf(model.Vendor);
        var launcher = new ProcessLauncher(tracker);
        var launch = new ConsultantLaunch(repoPath, prompt, Coder is null ? handle : string.Empty, outputDir, new ReviewerSettings(model.Vendor)
        {
            ExecutablePath = settings(CliKey(model.Vendor)) ?? string.Empty,
            Model = model.Model,
            Timeout = timeout,
            McpServersToSwitchOff = NoMcpServers.CodexConfigured(settings),
        })
        {
            Confinement = Coder is null ? LaunchConfinement.AsShipped : Coder with { Resume = handle.Length == 0 ? CoderHandle.None : CoderHandle.Bind(Coder, handle) },
        };
        Directory.CreateDirectory(outputDir);
        var prepared = await runtime.PrepareAsync(launch, launcher, ct);
        if (prepared is ConsultantPreparation.Refused refused)
        {
            return new RoleLaunched(null, refused.Failure, runtime, null, string.Empty);
        }

        var invocation = runtime.Build(((ConsultantPreparation.Ready)prepared).Launch);
        var finished = await new ReviewerExecutor(launcher).LaunchOnceAsync(invocation, ct);

        return new RoleLaunched(finished, null, runtime, invocation, finished.Process is { } process ? runtime.ReadHandle(process) : string.Empty);
    }

    /// <summary>The consultant runtime of a vendor and the runtime name its install cure is read by.</summary>
    public static (IConsultantRuntime Runtime, string Name) RuntimeOf(string vendor) => vendor switch
    {
        "claude" => (new ClaudeConsultant(new ClaudeRuntime(), "claude"), "claude"),
        "codex" => (new CodexConsultant(new CodexRuntime(), "codex"), "codex"),
        _ => (new AntigravityConsultant(new AntigravityRuntime("agy"), "agy"), "antigravity"),
    };
}
