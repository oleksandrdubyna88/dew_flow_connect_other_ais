using CoaiMcp.Core.Toolchain;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The preflight of the check boundary: a small shell program that runs INSIDE a unit started with exactly the properties and the environment of a check step, and answers one row for each thing the boundary is supposed to do. No model is involved.
/// </summary>
/// <remarks>
/// <para><b>It asks the OS, it does not read the property list.</b> A sandbox option can be accepted and do nothing (R7 of <c>RESULTS_coder_write_boundary.md</c>: the CLI's own sandbox failed OPEN with a stderr warning). Each row
/// is a real attempt — a write that must fail, a program that must not start, a bus that must not answer — and a row that is missing counts as an answer of "no": a script that stopped half way cannot pass.</para>
/// <para>The rows are the six items of the task's <c>d3</c> and more that the same unit lets it prove cheaply: the job's <c>/tmp</c> and <c>/var/tmp</c> are the bound folders, the network has the state the step asked for, no socket or interop folder is
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

    internal const string SubmodulesReadOnly = "submodules-readonly";

    /// <summary>The check preflight's item for the coder's own folder (its CLI's home and config): it must not be there inside a check's unit.</summary>
    internal const string CoderFolderHidden = "coder-folder-hidden";

    /// <summary>The check preflight's item for ANOTHER job's folder (E3 s7 t5, ids K2 of the judgement): a canary the preflight plants in a stand-in folder beside the job's own must not be readable from a check's unit.</summary>
    internal const string OtherJobFolderHidden = "other-job-folder-hidden";

    /// <summary>Every item the script must answer, in the order it asks.</summary>
    internal static readonly IReadOnlyList<string> Items =
        [WorktreeWrite, MainReadOnly, GitReadOnly, NoUserManager, WindowsProgram, HomeIsTheJobs, TempIsTheJobs, CoderFolderHidden, OtherJobFolderHidden, KeysHidden, SignInAbsent, NetworkState, NoSocketsNoInterop, CleanEnvironment];

    /// <summary>The items of a check preflight for <paramref name="job"/>: <see cref="Items"/>, and the submodule row when the job has a mount — a job with none is asked exactly what E2 asked.</summary>
    internal static IReadOnlyList<string> ItemsFor(BoundaryJob job) => job.Mounts.Count == 0 ? Items : [.. Items, SubmodulesReadOnly];

    internal const string CoderRootReadOnly = "coder-root-readonly";

    internal const string SignInReadOnly = "sign-in-readonly";

    internal const string ProgramVisible = "program-visible";

    internal const string SignInAbsent = "sign-in-not-in-unit";

    internal const string KeysHidden = "keys-folder-hidden";

    /// <summary>Every item the coder's script must answer, in the order it asks: the check items that still hold for a launch that needs the network and a home, and three of its own.</summary>
    internal static readonly IReadOnlyList<string> CoderItems =
        [WorktreeWrite, MainReadOnly, CoderRootReadOnly, GitReadOnly, NoUserManager, WindowsProgram, HomeIsTheJobs, SignInReadOnly, ProgramVisible, TempIsTheJobs, NetworkState, NoSocketsNoInterop, CleanEnvironment, SignInAbsent, KeysHidden];

    /// <summary>The items of a coder preflight for <paramref name="job"/>: <see cref="CoderItems"/>, and the submodule row when the job has a mount.</summary>
    internal static IReadOnlyList<string> CoderItemsFor(BoundaryJob job) => job.Mounts.Count == 0 ? CoderItems : [.. CoderItems, SubmodulesReadOnly];

    /// <summary>The name the marker files carry in the job's <c>/tmp</c> and <c>/var/tmp</c>; the script looks for them from inside.</summary>
    internal static string MarkerName(BoundaryJob job) => $".coai-job-{job.JobId}";

    /// <summary>The name of the Windows program's copy in the worktree while the preflight runs. Removed as soon as the unit ends.</summary>
    internal const string ProgramCopyName = ".coai-preflight.exe";

    internal const string Header = """
        wt=$1; main=$2; rh=$3; net=$4; job=$5; exe=$6; res=$7
        row() { printf '%s\t%s\t%s\n' "$1" "$2" "$3"; }
        probe=.coai-preflight-probe
        """;

    internal const string WorktreeRow = """
        if [ ! -e "$wt/.coai-job-$job" ]; then row worktree-write bad "the worktree inside the unit is not the job's worktree"
        elif { true > "$wt/$probe"; } 2>/dev/null; then rm -f "$wt/$probe"; row worktree-write ok "a file was written in the worktree"; else row worktree-write bad "a write in the worktree failed"; fi
        """;

    private const string MainRow = """
        if [ ! -d "$main/.git" ]; then row main-readonly bad "the main checkout is not visible inside the unit, so git could not read its objects"
        elif { true > "$main/$probe"; } 2>/dev/null; then rm -f "$main/$probe"; row main-readonly bad "a file was WRITTEN in the main checkout"; else row main-readonly ok "the main checkout is there and a write in it was refused"; fi
        """;

    internal const string GitRow = """
        if { true >> "$wt/.git"; } 2>/dev/null; then row git-readonly bad "the .git pointer of the worktree can be written"
        elif { true > "$main/.git/$probe"; } 2>/dev/null; then rm -f "$main/.git/$probe"; row git-readonly bad "a file was WRITTEN in the .git folder of the main checkout"
        else row git-readonly ok "writes to the .git pointer and to the main .git folder were refused"; fi
        """;

    internal const string ManagerRow = """
        if systemd-run --user --quiet --wait --collect --unit "coai-check-$job-escape" true >/dev/null 2>&1; then row no-user-manager-inside bad "a new unit was started from inside the unit"; else row no-user-manager-inside ok "systemd-run --user cannot start a unit from inside"; fi
        """;

    internal const string ExeRow = """
        if [ -z "$exe" ]; then row windows-exe ok "this host has no Windows interop, so there was no .exe to try"
        elif "$exe" >/dev/null 2>&1; then row windows-exe bad "a Windows program in the worktree RAN"
        else row windows-exe ok "a Windows program in the worktree did not run"; fi
        """;

    internal const string HomeRow = """
        if [ -L "$HOME" ]; then row home-is-the-jobs bad "HOME is a link, not the job's own folder"
        elif [ "$HOME" = "$rh" ]; then row home-is-the-jobs bad "HOME is the real HOME"
        elif { true > "$rh/$probe"; } 2>/dev/null; then rm -f "$rh/$probe"; row home-is-the-jobs bad "a file was WRITTEN in the real HOME"
        elif ! { true > "$HOME/$probe"; } 2>/dev/null; then row home-is-the-jobs bad "the job HOME cannot be written"
        else rm -f "$HOME/$probe"; row home-is-the-jobs ok "HOME is the job's own folder and the real HOME cannot be written"; fi
        """;

    internal const string TempRow = """
        if [ -e "/tmp/.coai-job-$job" ] && [ -e "/var/tmp/.coai-job-$job" ]; then row tmp-is-the-jobs ok "/tmp and /var/tmp are the job's own folders"; else row tmp-is-the-jobs bad "/tmp or /var/tmp is not the job's own folder"; fi
        """;

    private const string NetworkRow = """
        if [ "$net" = none ]; then
          if [ "$(ls /sys/class/net)" = lo ]; then row network-state ok "the only interface is lo"; else row network-state bad "an interface other than lo is there"; fi
        else row network-state ok "the host network, as the step asked"; fi
        """;

    internal const string SocketsRow = """
        gone=
        [ -n "$(ls -A /run/user 2>/dev/null)" ] && gone="$gone /run/user is not empty;"
        if [ -n "$res" ]; then left=$(find /mnt -mindepth 1 -maxdepth 2 2>/dev/null | grep -vxF -e "$res" -e "$(dirname "$res")"); else left=$(find /mnt -mindepth 1 -maxdepth 2 2>/dev/null); fi
        [ -n "$left" ] && gone="$gone /mnt holds something but the resolver file;"
        ls /run/WSL >/dev/null 2>&1 && gone="$gone /run/WSL is reachable;"
        if [ -z "$gone" ]; then row no-sockets-no-interop ok "/run/user is empty, /mnt holds nothing but the resolver file at most, and /run/WSL is closed"; else row no-sockets-no-interop bad "$gone"; fi
        """;

    private const string EnvironmentRow = """
        if env | grep -Eq '^(SSH_AUTH_SOCK|DBUS_SESSION_BUS_ADDRESS|GPG_AGENT_INFO|XDG_RUNTIME_DIR|COAI_[A-Za-z0-9_]*|[A-Za-z0-9_]*(API_KEY|TOKEN|SECRET))='; then row clean-environment bad "a name a check must never see is in the environment"; else row clean-environment ok "no vendor key, COAI_ name, agent or bus socket is in the environment"; fi
        """;

    /// <summary>The check script reads the coder's folder as its eighth argument, after the seven of <see cref="Header"/>, and the key folder and keys file (one string, space between, empty when none is named) as its ninth.</summary>
    private const string CheckHeader = """
        coderdir=$8; keylist=$9; shift 9; otherjob=$1
        """;

    /// <summary>A check unit holds no key material: the key folder and the keys file (the pool's, at their real paths) cannot be read from it. Not named: nothing to hide.</summary>
    internal const string CheckKeysRow = """
        if [ -z "$keylist" ]; then row keys-folder-hidden ok "no key folder or keys file is named for this job, so there is none to hide"
        else
          seen=
          set -f
          for k in $keylist; do if [ -r "$k" ] && { ls -A "$k" >/dev/null 2>&1 || head -c 1 "$k" >/dev/null 2>&1; }; then seen="$seen $k;"; fi; done
          set +f
          if [ -z "$seen" ]; then row keys-folder-hidden ok "the claude key folder and the keys file cannot be read from inside the check's unit"; else row keys-folder-hidden bad "key material can be read from inside the check's unit:$seen"; fi
        fi
        """;

    /// <summary>The person's sign-in file, at its real path, is not readable from a check unit.</summary>
    internal const string CheckSignInRow = """
        if [ -r "$rh/.claude/.credentials.json" ]; then row sign-in-not-in-unit bad "the person's sign-in file is visible inside the check's unit"
        else row sign-in-not-in-unit ok "the person's sign-in file is not in the check's unit"; fi
        """;

    /// <summary>
    /// The coder's folder holds the coder CLI's home, its config and its transcripts; a check runs code the coder wrote, so it must see neither the folder nor anything in it. The folder is made on the host before the unit starts, so "not there" is an observation and not an absent folder.
    /// </summary>
    private const string CoderFolderRow = """
        if [ -z "$coderdir" ]; then row coder-folder-hidden bad "no coder folder was named to look for, so nothing was observed"
        elif [ -e "$coderdir" ] || ls -A "$coderdir" >/dev/null 2>&1; then row coder-folder-hidden bad "the coder's own folder (its home and config) is visible inside the check's unit"
        else row coder-folder-hidden ok "the coder's own folder is not visible inside the check's unit"; fi
        """;

    /// <summary>
    /// K1 asked once more and K2: the canary the preflight planted in a stand-in folder of ANOTHER job (<c>$10</c>, always made in the folder that really holds the job folders; EMPTY only when none could be made, which is a bad row: nothing was observed) must not be
    /// visible, listable or readable from a check's unit, and the folder beside it must not take a write.
    /// </summary>
    private const string OtherJobRow = """
        if [ -z "$otherjob" ]; then row other-job-folder-hidden bad "no stand-in for another job's folder could be made, so nothing was observed"
        elif [ -e "$otherjob" ] || ls -A "$otherjob" >/dev/null 2>&1 || cat "$otherjob/canary.txt" >/dev/null 2>&1 || { true > "$otherjob/$probe"; } 2>/dev/null; then rm -f "$otherjob/$probe" 2>/dev/null; row other-job-folder-hidden bad "a stand-in folder of another job is visible, readable or writable from inside the check's unit"
        else row other-job-folder-hidden ok "a stand-in folder of another job cannot be seen, read or written from inside the check's unit"; fi
        """;

    private const string CoderHeader = """
        root=$8; cred=$9; shift 9; prog=$1; mode=$2; keys=$3; inunit=$4
        """;

    private const string CoderRootRow = """
        if [ ! -d "$root" ]; then row coder-root-readonly bad "the coder root is not visible inside the unit"
        elif { true > "$root/$probe"; } 2>/dev/null; then rm -f "$root/$probe"; row coder-root-readonly bad "a file was WRITTEN beside the worktree, in the coder root"
        else row coder-root-readonly ok "a write beside the worktree, in the coder root, was refused"; fi
        """;

    private const string SignInRow = """
        if [ -z "$cred" ]; then row sign-in-readonly ok "this launch has no sign-in file to bind: it has a key or nothing"
        elif [ ! -r "$cred" ]; then row sign-in-readonly bad "the sign-in file is not readable inside the unit"
        elif { true >> "$cred"; } 2>/dev/null; then row sign-in-readonly bad "the sign-in file can be WRITTEN"
        else row sign-in-readonly ok "the sign-in file is readable and a write to it was refused"; fi
        """;

    private const string ProgramRow = """
        if [ -x "$prog" ]; then row program-visible ok "the program of the launch is there and can be run"; else row program-visible bad "the program of the launch is not there, or cannot be run, inside the unit"; fi
        """;

    private const string SignInAbsentRow = """
        if [ "$mode" != keyed ]; then row sign-in-not-in-unit ok "this launch has no pool key (the device switch), so the sign-in file is bound on purpose"
        elif [ -e "$inunit" ] || [ -e "$rh/.claude/.credentials.json" ]; then row sign-in-not-in-unit bad "the person's sign-in file is visible inside the unit of a launch that has a pool key"
        else row sign-in-not-in-unit ok "the person's sign-in file is not in the unit"; fi
        """;

    private const string KeysHiddenRow = """
        if [ "$mode" != keyed ] || [ -z "$keys" ]; then row keys-folder-hidden ok "this launch has no pool key to hide the key folder from"
        elif [ -r "$keys" ] || ls -A "$keys" >/dev/null 2>&1; then row keys-folder-hidden bad "the claude key folder can be read from inside the unit"
        else row keys-folder-hidden ok "the claude key folder cannot be read from inside the unit"; fi
        """;

    /// <summary>
    /// The submodule row (epoch 3 E2 s6, the recorded change), present only for a job with a mount: <c>$1</c> after the shift is each mount's target as the unit sees it and then its source. For every mount the target must hold something (the files of the
    /// pinned commit, bound over the empty folder git leaves for a gitlink) and a new file in it must be refused; a write at the source's host path must be refused too, by a bind that is read-only or a path the unit cannot see. Only paths coai made are spelled into the sentence.
    /// </summary>
    internal static string SubmodulesRow(int shift) => $$"""
        shift {{shift}}
        bad=
        while [ "$#" -ge 2 ]; do
          t=$1; s=$2; shift 2
          if [ ! -d "$t" ] || [ -z "$(ls -A "$t" 2>/dev/null)" ]; then bad="$bad $t holds nothing inside the unit;"
          elif { true > "$t/$probe"; } 2>/dev/null; then rm -f "$t/$probe"; bad="$bad $t can be WRITTEN;"
          elif [ -d "$s" ] && { true > "$s/$probe"; } 2>/dev/null; then rm -f "$s/$probe"; bad="$bad the source of $t can be WRITTEN;"
          fi
        done
        if [ -z "$bad" ]; then row submodules-readonly ok "every submodule mount holds files and a write into it was refused"; else row submodules-readonly bad "$bad"; fi
        """;

    internal static string Join(params string[] rows) => string.Join('\n', rows);

    /// <summary>
    /// <c>$1</c> worktree, <c>$2</c> main checkout, <c>$3</c> real home, <c>$4</c> network (<c>none</c>|<c>host</c>), <c>$5</c> job id, <c>$6</c> the Windows program to try (empty: none), <c>$7</c> the resolver file bound back under <c>/mnt</c> for a host-network step (empty: none), <c>$8</c> the coder's folder, which must not be visible, <c>$9</c> the key folder and keys file, which must not be readable.
    /// </summary>
    private static readonly string Script = Join(Header, CheckHeader, WorktreeRow, MainRow, GitRow, ManagerRow, ExeRow, HomeRow, TempRow, CoderFolderRow, OtherJobRow, CheckKeysRow, CheckSignInRow, NetworkRow, SocketsRow, EnvironmentRow);

    /// <summary>
    /// The coder's script: the same rows over the same fragments, plus <c>$8</c> the coder root (a write beside the worktree must be refused), <c>$9</c> the sign-in file inside the unit (empty: none; read-only) and a tenth, the program of the launch (it must be there) — read after a <c>shift 9</c>, because systemd expands <c>${NAME}</c> in a command line, so no script of this family may spell one. After the program come the launch's mode (<c>keyed</c> when it carries a pool key and so binds no sign-in file,
    /// else <c>bound</c>), the key folder to hide and the path the sign-in file would have inside the unit (epoch 3 E2 s7): the two rows <see cref="SignInAbsent"/> and <see cref="KeysHidden"/> prove, for a keyed launch, that the person's file is not in the unit and the key folder cannot be read from it.
    /// </summary>
    private static readonly string CoderScript = Join(Header, CoderHeader, WorktreeRow, MainRow, CoderRootRow, GitRow, ManagerRow, ExeRow, HomeRow, SignInRow, ProgramRow, TempRow, NetworkRow, SocketsRow, EnvironmentRow, SignInAbsentRow, KeysHiddenRow);

    private static string ScriptFor(BoundaryJob job) => job.Mounts.Count == 0 ? Script : Join(Script, SubmodulesRow(1));

    private static string CoderScriptFor(BoundaryJob job) => job.Mounts.Count == 0 ? CoderScript : Join(CoderScript, SubmodulesRow(4));

    private static IReadOnlyList<string> MountArguments(BoundaryJob job) => [.. job.Mounts.SelectMany(mount => new[] { mount.Target, mount.Source })];

    /// <summary>The command of the preflight unit.</summary>
    internal static IReadOnlyList<string> Command(BoundaryJob job, BoundaryHost host, CheckNetwork network, string program, string otherJob = "") =>
        ["/bin/sh", "-c", ScriptFor(job), "coai-preflight", job.Tree.Root, job.Tree.MainCheckout, host.ResolvedHome, network == CheckNetwork.Private ? "none" : "host", job.JobId, program, network == CheckNetwork.Host ? host.ResolverFile : string.Empty, job.CoderFolder, string.Join(' ', new[] { job.KeysFolder, job.KeysFile }.Where(path => path.Length > 0)), otherJob, .. MountArguments(job)];

    /// <summary>
    /// The command of the coder's preflight unit: the program of the launch, the copy of the Windows program to try, the sign-in file as the unit sees it (empty when the launch binds none) and the resolver file, as the launch itself gets them. The network is the host's — the CLI
    /// talks to its vendor — so the network row only says so.
    /// </summary>
    internal static IReadOnlyList<string> CoderCommand(BoundaryJob job, BoundaryHost host, CoderProgram program, string windowsCopy, string signInInUnit) =>
        ["/bin/sh", "-c", CoderScriptFor(job), "coai-preflight", job.Tree.Root, job.Tree.MainCheckout, host.ResolvedHome, "host", job.JobId, windowsCopy, host.ResolverFile, job.CoderRoot, signInInUnit, program.Real, host.BindSignIn ? "bound" : "keyed", host.BindSignIn ? string.Empty : job.KeysFolder, job.CoderSignInFile, .. MountArguments(job)];

    /// <summary>
    /// The rows of a preflight unit that ran: the client's own answer, then the rows the script printed — or, for a unit that did not run to its end, the one row that says why. A unit that did not start, did not finish in time or could not be stopped has no rows worth reading.
    /// </summary>
    internal static IReadOnlyList<PreflightRow> Rows(UnitStart started, bool gone, IReadOnlyList<string> items) =>
        started.Problem.Length > 0
            ? [new PreflightRow("user-manager", false, started.Problem)]
            : started.Result is { TimedOut: false, ExitCode: 0 } && gone
                ? [new PreflightRow("user-manager", true, "systemd-run --user started the preflight unit"), .. Parse(started.Result.StdOut, items)]
                : [new PreflightRow("user-manager", false, Unanswered(started.Result, gone))];

    private static string Unanswered(Processes.ProcessResult result, bool gone) =>
        !gone ? "the preflight unit could not be stopped"
        : result.TimedOut ? "the preflight unit did not finish in time"
        : $"the preflight unit did not start or did not finish (exit {result.ExitCode}): {Short(result.StdErr)} {CoaiMcp.Runners.Detach.DetachedLauncher.ExpandEnvironmentHint(result.StdErr)}".TrimEnd();

    private static string Short(string text)
    {
        var line = text.Trim().ReplaceLineEndings(" ");

        return line.Length <= 300 ? line : line[..300] + "…";
    }

    /// <summary>
    /// The rows the unit printed, one for each item of <paramref name="items"/>, in that order; an item the script did not answer, or answered twice with different words, is a row that did not hold. Lines that are not rows are ignored.
    /// </summary>
    internal static IReadOnlyList<PreflightRow> Parse(string output, IReadOnlyList<string> items)
    {
        var said = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(part => part.Length == 3 && items.Contains(part[0], StringComparer.Ordinal) && part[1] is "ok" or "bad")
            .ToLookup(part => part[0], StringComparer.Ordinal);

        return [.. items.Select(item => RowOf(item, said[item].ToList()))];
    }

    private static PreflightRow RowOf(string item, List<string[]> answers) => answers.Count switch
    {
        0 => new PreflightRow(item, false, "the preflight did not answer this item"),
        1 => new PreflightRow(item, answers[0][1] == "ok", answers[0][2]),
        _ => new PreflightRow(item, false, "the preflight answered this item more than once"),
    };
}
