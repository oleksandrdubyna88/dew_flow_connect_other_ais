using System.Text.Json;
using CoaiMcp.Core.Consultation;
using CoaiMcp.Core.QuestionConsult;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Processes;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Vibe;

/// <summary>
/// Mistral Vibe as a consultant — the stuck consultant's conversation and a question row's one shot (S3 of
/// <c>todo/PLAN_vibe_and_kimi_runtimes.md</c>). Every launch is the reviewer's own shape (<see cref="VibeRuntime"/>) and goes
/// through the one launch control (<see cref="VibeLaunch"/>, asked by the executor before every launch): the host (D18),
/// the built environment (D17), the curated model (D10), the owned home and its turn, the version.
/// </summary>
/// <remarks>
/// <para><b>Never the checkout</b> (D6): Vibe reads only its working folder (M-V11, 6 of 6) and loads its configuration
/// from it, so it stands in a coai-made sanitised snapshot (<see cref="ConsultantLaunch.Workdir"/>). A conversation keeps
/// ONE snapshot path, refreshed in place before each turn — a resumed session is bound to the folder it was created in
/// (research/RESULTS_vibe_confinement.md §22 B, E), and the same path re-filled was read afresh 3 of 3 (§22 D).</para>
/// <para><b>One home per conversation</b> (D17): the caller hands a coai-owned folder in
/// <see cref="ReviewerSettings.OwnedScratch"/> — <c>coai-vibe/&lt;consultation id&gt;</c> for a conversation, the row's own
/// scratch folder for a question row — and the home is <see cref="VibeRuntime.HomeFor"/> inside it, the same name every
/// turn, so <c>--resume</c> finds the session it stored there.</para>
/// <para><b>Memory is the vendor's</b> (D7): <c>--resume &lt;sessionId&gt;</c>, the id read off <c>--output json</c>, whose every
/// element carries it (research/RESULTS_vibe_and_kimi_first_probe.md, Output). <b>Usage is per turn</b> (M-V5, 3 of 3: a
/// resumed export's input is turn one's plus the history, not the sum).</para>
/// <para><b>Not known, and so not claimed:</b> what Vibe says when it is asked to resume a session it no longer holds — no
/// cell measured it, so <see cref="DroppedTheConversation"/> recognises nothing and such a turn fails as the exit it is.</para>
/// </remarks>
public sealed class VibeConsultant(VibeRuntime inner, string vendor = "vibe") : IConsultantRuntime
{
    public string Vendor => vendor;

    public ConsultantMemory Memory => new ConsultantMemory.VendorRemembers();

    /// <summary>M-V5: per turn.</summary>
    public bool UsageIsCumulative => false;

    /// <summary>D6: a copy of the live tree, never the checkout.</summary>
    public bool NeedsASanitisedSnapshot => true;

    /// <summary>D17: one coai-owned home per conversation, under <see cref="ConsultantHomes.Vibe"/>.</summary>
    public string HomesFolder => ConsultantHomes.Vibe;

    /// <summary>The one tool the launch enables, as it was observed working (M-V6: read inside 3 of 3, outside denied 3 of 3).</summary>
    public string Toolbox =>
        "You read files with read_file only, by a path inside your working folder — a copy coai made of the checkout without "
        + "its .git, .vibe/, .agents/ skills and AGENTS.md. Nothing outside that folder can be read, and no other tool is enabled.";

    public ReviewerInvocation Build(ConsultantLaunch launch)
    {
        ConsultantLaunches.MustBeLaunchable(launch);
        var role = ConsultantLaunches.RoleOf(launch);
        var owned = OwnedFolder(launch);
        var model = launch.Settings.Model.Length > 0 ? launch.Settings.Model : VibeModels.Default;
        var argv = (string[])
        [
            .. VibeRuntime.Arguments(Cwd(launch), VibeRuntime.ExportFolder(owned, role, Guid.NewGuid()), launch.Settings.Timeout, ReadOnly(launch)),
            .. launch.Handle.Length > 0 ? (string[])["--resume", launch.Handle] : [],
        ];
        ConsultantLaunches.MustCarryNoLineBreak(argv);
        var request = VibeLaunch.Built(
            new ProcessRequest(Executable(launch.Settings), argv, Cwd(launch)) { StdIn = launch.Prompt, Timeout = launch.Settings.Timeout },
            VibeRuntime.HomeFor(owned, vendor, role),
            model);

        return new ReviewerInvocation(
            vendor, role, launch.Confinement is LaunchConfinement.Planned ? ConsultantLaunches.ForQuestion(request) : request,
            string.Empty, inner, Model: model);
    }

    /// <summary>The conversation's snapshot, or the planned row's cwd — never the checkout, and never a guess.</summary>
    private static string Cwd(ConsultantLaunch launch) =>
        launch.Confinement is LaunchConfinement.Planned planned ? ConsultantLaunches.Cwd(launch, planned.Plan) : SnapshotOf(launch);

    private static string SnapshotOf(ConsultantLaunch launch) =>
        launch.Workdir.Length > 0
            ? launch.Workdir
            : throw new ArgumentException(
                "a Vibe consultant stands in the conversation's sanitised snapshot (Workdir), and none was made — the checkout is never its working folder (D6)",
                nameof(launch));

    /// <summary>D2's pair — from the plan for a question row (the planner is the one place a row's sandbox is decided).</summary>
    private static IReadOnlyList<string> ReadOnly(ConsultantLaunch launch) =>
        launch.Confinement is LaunchConfinement.Planned planned ? planned.Plan.Flags : ConfinementPlanner.VibeReadOnly;

    private static string OwnedFolder(ConsultantLaunch launch) =>
        launch.Settings.OwnedScratch.Length > 0
            ? launch.Settings.OwnedScratch
            : throw new ArgumentException(
                "a Vibe consultant keeps its home in a coai-owned folder of the conversation or the row (OwnedScratch), and none was given — never the person's own ~/.vibe (D17)",
                nameof(launch));

    private string Executable(ReviewerSettings settings) =>
        settings.ExecutablePath.Length > 0 ? settings.ExecutablePath : inner.DefaultExecutable;

    /// <summary>The <c>sessionId</c> the output's elements carry — the first well-formed one — or empty.</summary>
    public string ReadHandle(ProcessResult result)
    {
        try
        {
            using var document = JsonDocument.Parse(result.StdOut);

            return document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().Select(SessionOf).FirstOrDefault(ConsultantHandle.IsWellFormed) ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>An element's <c>sessionId</c> (or <c>session_id</c>, the export's spelling), or empty.</summary>
    private static string SessionOf(JsonElement element) =>
        element.ValueKind != JsonValueKind.Object ? string.Empty
        : VibeExport.Text(element, "sessionId") is { Length: > 0 } id ? id
        : VibeExport.Text(element, "session_id");

    /// <summary>Unmeasured, so nothing is recognised: a resume Vibe could not honour fails as the exit it is.</summary>
    public bool DroppedTheConversation(ProcessResult result) => false;
}
