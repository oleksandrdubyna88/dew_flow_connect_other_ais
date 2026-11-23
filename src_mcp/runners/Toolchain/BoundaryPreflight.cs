using CoaiMcp.Core.Toolchain;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The preflight of the check boundary: a small shell program that runs INSIDE a unit started with exactly the properties and the environment of a check step, and answers one row for each thing the boundary is supposed to do. No model is involved.
/// </summary>
/// <remarks>
/// <para><b>It asks the OS, it does not read the property list.</b> A sandbox option can be accepted and do nothing (R7 of <c>RESULTS_coder_write_boundary.md</c>: the CLI's own sandbox failed OPEN with a stderr warning). Each row
/// is a real attempt — a write that must fail, a program that must not start, a bus that must not answer — and a row that is missing counts as an answer of "no": a script that stopped half way cannot pass.</para>
/// <para>The rows are the six items of the task's <c>d3</c> and four more the same unit lets it prove cheaply: the job's <c>/tmp</c> and <c>/var/tmp</c> are the bound folders, the network has the state the step asked for, no socket or interop folder is
/// reachable, and the environment holds none of the names a check must never see.</para>
/// <para>Rows are tab-separated <c>item</c>, <c>ok|bad</c>, one sentence. A sentence never carries the output of a program the preflight ran, so a hostile program cannot write into a row.</para>
/// </remarks>
internal static class BoundaryPreflight
{
    internal const string WorktreeWrite = "worktree-write";

    internal const string MainReadOnly = "main-readonly";

    internal const string GitReadOnly = "git-readonly";

    internal const string NoUserManager = "no-user-manager-inside";

    internal const string WindowsProgram = "windows-exe";

    internal const string HomeIsTheJobs = "home-is-the-jobs";

    internal const string TempIsTheJobs = "tmp-is-the-jobs";

    internal const string NetworkState = "network-state";

    internal const string NoSocketsNoInterop = "no-sockets-no-interop";

    internal const string CleanEnvironment = "clean-environment";

    /// <summary>Every item the script must answer, in the order it asks.</summary>
    internal static readonly IReadOnlyList<string> Items =
        [WorktreeWrite, MainReadOnly, GitReadOnly, NoUserManager, WindowsProgram, HomeIsTheJobs, TempIsTheJobs, NetworkState, NoSocketsNoInterop, CleanEnvironment];

    /// <summary>The name the marker files carry in the job's <c>/tmp</c> and <c>/var/tmp</c>; the script looks for them from inside.</summary>
    internal static string MarkerName(BoundaryJob job) => $".coai-job-{job.JobId}";

    /// <summary>The name of the Windows program's copy in the worktree while the preflight runs. Removed as soon as the unit ends.</summary>
    internal const string ProgramCopyName = ".coai-preflight.exe";

    /// <summary>
    /// <c>$1</c> worktree, <c>$2</c> main checkout, <c>$3</c> real home, <c>$4</c> network (<c>none</c>|<c>host</c>), <c>$5</c> job id, <c>$6</c> the Windows program to try (empty: none).
    /// </summary>
    private const string Script = """
        wt=$1; main=$2; rh=$3; net=$4; job=$5; exe=$6
        row() { printf '%s\t%s\t%s\n' "$1" "$2" "$3"; }
        probe=.coai-preflight-probe
        if [ ! -e "$wt/.coai-job-$job" ]; then row worktree-write bad "the worktree inside the unit is not the job's worktree"
        elif { true > "$wt/$probe"; } 2>/dev/null; then rm -f "$wt/$probe"; row worktree-write ok "a file was written in the worktree"; else row worktree-write bad "a write in the worktree failed"; fi
        if [ ! -d "$main/.git" ]; then row main-readonly bad "the main checkout is not visible inside the unit, so git could not read its objects"
        elif { true > "$main/$probe"; } 2>/dev/null; then rm -f "$main/$probe"; row main-readonly bad "a file was WRITTEN in the main checkout"; else row main-readonly ok "the main checkout is there and a write in it was refused"; fi
        if { true >> "$wt/.git"; } 2>/dev/null; then row git-readonly bad "the .git pointer of the worktree can be written"
        elif { true > "$main/.git/$probe"; } 2>/dev/null; then rm -f "$main/.git/$probe"; row git-readonly bad "a file was WRITTEN in the .git folder of the main checkout"
        else row git-readonly ok "writes to the .git pointer and to the main .git folder were refused"; fi
        if systemd-run --user --quiet --wait --collect --unit "coai-check-$job-escape" true >/dev/null 2>&1; then row no-user-manager-inside bad "a new unit was started from inside the unit"; else row no-user-manager-inside ok "systemd-run --user cannot start a unit from inside"; fi
        if [ -z "$exe" ]; then row windows-exe ok "this host has no Windows interop, so there was no .exe to try"
        elif "$exe" >/dev/null 2>&1; then row windows-exe bad "a Windows program in the worktree RAN"
        else row windows-exe ok "a Windows program in the worktree did not run"; fi
        if [ "$HOME" = "$rh" ]; then row home-is-the-jobs bad "HOME is the real HOME"
        elif { true > "$rh/$probe"; } 2>/dev/null; then rm -f "$rh/$probe"; row home-is-the-jobs bad "a file was WRITTEN in the real HOME"
        elif ! { true > "$HOME/$probe"; } 2>/dev/null; then row home-is-the-jobs bad "the job HOME cannot be written"
        else rm -f "$HOME/$probe"; row home-is-the-jobs ok "HOME is the job's own folder and the real HOME cannot be written"; fi
        if [ -e "/tmp/.coai-job-$job" ] && [ -e "/var/tmp/.coai-job-$job" ]; then row tmp-is-the-jobs ok "/tmp and /var/tmp are the job's own folders"; else row tmp-is-the-jobs bad "/tmp or /var/tmp is not the job's own folder"; fi
        if [ "$net" = none ]; then
          if [ "$(ls /sys/class/net)" = lo ]; then row network-state ok "the only interface is lo"; else row network-state bad "an interface other than lo is there"; fi
        else row network-state ok "the host network, as the step asked"; fi
        gone=
        [ -n "$(ls -A /run/user 2>/dev/null)" ] && gone="$gone /run/user is not empty;"
        [ -n "$(ls -A /mnt 2>/dev/null)" ] && gone="$gone /mnt is not empty;"
        ls /run/WSL >/dev/null 2>&1 && gone="$gone /run/WSL is reachable;"
        if [ -z "$gone" ]; then row no-sockets-no-interop ok "/run/user and /mnt are empty and /run/WSL is closed"; else row no-sockets-no-interop bad "$gone"; fi
        if env | grep -Eq '^(SSH_AUTH_SOCK|DBUS_SESSION_BUS_ADDRESS|GPG_AGENT_INFO|XDG_RUNTIME_DIR|COAI_[A-Za-z0-9_]*|[A-Za-z0-9_]*(API_KEY|TOKEN|SECRET))='; then row clean-environment bad "a name a check must never see is in the environment"; else row clean-environment ok "no vendor key, COAI_ name, agent or bus socket is in the environment"; fi
        """;

    /// <summary>The command of the preflight unit.</summary>
    internal static IReadOnlyList<string> Command(BoundaryJob job, BoundaryHost host, CheckNetwork network, string program) =>
        ["/bin/sh", "-c", Script, "coai-preflight", job.Tree.Root, job.Tree.MainCheckout, host.RealHome, network == CheckNetwork.Private ? "none" : "host", job.JobId, program];

    /// <summary>
    /// The rows the unit printed, one for each item in <see cref="Items"/>, in that order; an item the script did not answer, or answered twice with different words, is a row that did not hold. Lines that are not rows are ignored.
    /// </summary>
    internal static IReadOnlyList<PreflightRow> Parse(string output)
    {
        var said = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(part => part.Length == 3 && Items.Contains(part[0], StringComparer.Ordinal) && part[1] is "ok" or "bad")
            .ToLookup(part => part[0], StringComparer.Ordinal);

        return [.. Items.Select(item => RowOf(item, said[item].ToList()))];
    }

    private static PreflightRow RowOf(string item, List<string[]> answers) => answers.Count switch
    {
        0 => new PreflightRow(item, false, "the preflight did not answer this item"),
        1 => new PreflightRow(item, answers[0][1] == "ok", answers[0][2]),
        _ => new PreflightRow(item, false, "the preflight answered this item more than once"),
    };
}
