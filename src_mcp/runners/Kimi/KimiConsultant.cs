using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Acp;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Kimi;

/// <summary>
/// Kimi Code as a consultant — the stuck consultant's conversation (E5.4 of <c>todo/PLAN_vibe_and_kimi_runtimes_E5.md</c>; S5 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>). Every launch is the reviewer's own shim request, composed from <see cref="KimiRuntime"/> and never
/// built a second time (decision 1): the executor then asks the one launch control (<see cref="KimiLaunch"/>) through that adapter — the host, the
/// tools, the model, the built environment, the sign-in, the home's shape, the gate on the sign-in, the version.
/// </summary>
/// <remarks>
/// <para><b>Never the checkout, and never a home of its own making</b> (decision 2): the cwd is the conversation's ONE stable snapshot
/// (<see cref="ConsultantLaunch.Workdir"/>, D6 — a loaded session resolves paths against the folder it was created in, M-K5), and the home is
/// <c>&lt;folder&gt;\kh-&lt;10 hex&gt;</c> under the coai-owned folder the caller hands in <see cref="ReviewerSettings.OwnedScratch"/> — never that
/// folder itself, whose turn the caller holds (E5.3 decision a), and never TEMP or the answers folder (D19). Both are required.</para>
/// <para><b>Memory is the vendor's</b> (decision 3): turn 1 starts a session, every later turn continues it with the shim's
/// <c>--session &lt;id&gt;</c> (E5.2), the id read off the report's <c>sessionId</c>. <b>Usage is cumulative</b> (decision 4, M-K5b): the service
/// bills each turn its share of the session's running total, guarded against a counter that restarted (decision 12).</para>
/// <para><b>A dropped session is the report's END word</b> (decisions 5 and 11): <c>session_lost</c>, never the agent's text — and the service
/// then REFUSES the turn with its cure; nothing here starts a fresh session in its place.</para>
/// </remarks>
public sealed class KimiConsultant(KimiRuntime inner, string vendor = "kimi") : IConsultantRuntime
{
    public string Vendor => vendor;

    public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

    /// <summary>M-K5b (RESULTS §23), and three turns of one session in M-K16 (d): <c>/usage</c> is the session's running total.</summary>
    public bool UsageIsCumulative => true;

    /// <summary>D6: Kimi loads a project <c>.kimi-code</c>, <c>AGENTS.md</c>, skills and <c>.mcp.json</c> from its working folder.</summary>
    public bool NeedsASanitisedSnapshot => true;

    /// <summary>One coai-owned home per conversation, under <see cref="ConsultantHomes.Kimi"/> (J3), swept with it (D20).</summary>
    public string HomesFolder => ConsultantHomes.Kimi;

    /// <summary>Only what was observed working (decision 7): <c>Read</c> inside the copy (M-K6), <c>Grep</c> and <c>Glob</c> (RESULTS §17) — never claimed bounded.</summary>
    public string Toolbox =>
        "You read files with Read, by a path inside your working folder — a copy coai made of the checkout without its .git, .kimi-code/, "
        + ".agents/ skills and sub-agents, .mcp.json and AGENTS.md; Grep and Glob search files too. Every write and every shell command is refused.";

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);
        MustCarryNoFlags(launch);
        var settings = launch.Settings with { OwnedScratch = OwnedFolder(launch), Model = ModelOf(launch.Settings) };
        var built = inner.Build(ConsultantLaunches.RoleOf(launch), launch.Prompt, Cwd(launch), string.Empty, launch.OutputDir, settings);
        var request = built.Request with { Arguments = [.. built.Request.Arguments, .. SessionOf(launch)] };
        ConsultantLaunches.MustCarryNoLineBreak(request.Arguments);

        return built with { Request = launch.Confinement is LaunchConfinement.Planned ? ConsultantLaunches.ForQuestion(request) : request };
    }

    /// <summary>The row's model, or Kimi's default when it names none — resolved here so the shim and the ledger name the same one.</summary>
    private static string ModelOf(ReviewerSettings settings) => settings.Model.Length > 0 ? settings.Model : KimiModels.Default;

    /// <summary>The session a later turn continues — the shim's own flag (E5.2), last on the argv — or nothing for a first turn.</summary>
    private static string[] SessionOf(ConsultantLaunch launch) =>
        launch.Handle.Length > 0 ? [AcpWords.SessionFlag, launch.Handle] : [];

    /// <summary>The conversation's snapshot, or the planned row's cwd — never the checkout, and never a guess.</summary>
    private static string Cwd(ConsultantLaunch launch) =>
        launch.Confinement is LaunchConfinement.Planned planned ? ConsultantLaunches.Cwd(launch, planned.Plan) : SnapshotOf(launch);

    private static string SnapshotOf(ConsultantLaunch launch) =>
        launch.Workdir.Length > 0
            ? launch.Workdir
            : throw new ArgumentException(
                "a Kimi consultant stands in the conversation's sanitised snapshot (Workdir), and none was made — the checkout is never its working folder (D6)",
                nameof(launch));

    /// <summary>A planned row carries no flag for Kimi — its confinement is the shim's (E5.6 decision 3); a plan that has one was not made by coai for kimi.</summary>
    private static void MustCarryNoFlags(ConsultantLaunch launch)
    {
        if (launch.Confinement is LaunchConfinement.Planned { Plan: var plan } && plan.Leading.Count + plan.Flags.Count + plan.AddDirs.Count > 0)
        {
            throw new ArgumentException("a Kimi row's confinement is the shim's; a plan with flags is not one coai made for kimi", nameof(launch));
        }
    }

    private static string OwnedFolder(ConsultantLaunch launch) =>
        launch.Settings.OwnedScratch.Length > 0
            ? launch.Settings.OwnedScratch
            : throw new ArgumentException(
                "a Kimi consultant keeps its home in a coai-owned folder of the conversation or the row (OwnedScratch), and none was given — never TEMP or the answers folder (D19)",
                nameof(launch));

    /// <summary>The report's <c>sessionId</c> when it is a well-formed handle — the shim names it on every exit that prints a report — or empty.</summary>
    public string ReadHandle(ProcessResult result) =>
        KimiReport.Parse(result.StdOut).SessionId is var id && ConsultantHandle.IsWellFormed(id) ? id : string.Empty;

    /// <summary>The shim's END word says the agent no longer holds the session <c>--session</c> named (E5.2) — read off the report, never its text.</summary>
    public bool DroppedTheConversation(ProcessResult result) => KimiReport.Parse(result.StdOut).End == AcpWords.SessionLostWord;
}
