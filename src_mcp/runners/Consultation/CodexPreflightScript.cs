using CoaiMcp.Runners.Toolchain;

namespace CoaiMcp.Runners.Consultation;

/// <summary>
/// The shell program of the codex coder's preflight (epoch 3 E3 s5 t3): it runs INSIDE the exact launch unit with the vendor's allow-list environment, asks the OS one thing at a time and answers rows of the shape <c>item TAB ok|bad TAB sentence</c>. Three things it cannot judge itself — the tool list, the stand-in
/// project's list and the doctor report — it hands to <see cref="CodexPreflightRows"/> as <c>data</c> lines (base64 of the program's whole answer), so no row ever carries a word a program printed.
/// </summary>
/// <remarks>
/// <para><b>The vendor sandbox is tried, not read.</b> <c>codex sandbox -P coai -- sh -c …</c> runs a probe INSIDE it; the probe prints fixed tokens (<c>sb-…</c>) and this program turns them into rows. A write is judged by what the UNIT then sees (the file is there or it is not), never by an exit code: a sandbox may answer 0 for a write
/// into its own discarded skeleton. Every probe has a positive control from outside the sandbox (the loopback listener coai opened, the canary process, the file the sandbox must not read) so a probe that cannot work is a red row, not a green one.</para>
/// <para>No <c>${NAME}</c> is spelled anywhere (systemd expands it in a command line), no <c>$$</c>, and every value comes in as a positional argument.</para>
/// </remarks>
internal static class CodexPreflightScript
{
    /// <summary>The name of the file the sandbox probe writes and the unit then looks for.</summary>
    internal const string Probe = ".coai-preflight-probe";

    /// <summary>The positional arguments: the worktree, main checkout, real home, job id, the Windows program (or empty), the resolver file, the coder root, the program and its folder; the job's codex folder, its CODEX_HOME, its auth.json, the stand-in of another job, the listener's port, the placeholder, the config coai wrote, the environment coai set (NAME=value lines), the host and port of the refresh override, its URL, the key paths and the flags of the probes (one to a line). Then the submodule mounts.</summary>
    private const string Header = """
        wt=$1; main=$2; rh=$3; job=$4; exe=$5; res=$6; root=$7; prog=$8; progdir=$9; shift 9
        vendor=$1; chome=$2; auth=$3; otherjob=$4; port=$5; placeholder=$6; expconf=$7; expenv=$8; ohost=$9; shift 9
        oport=$1; ourl=$2; keylist=$3; flagtext=$4; shift 4
        net=host
        row() { printf '%s\t%s\t%s\n' "$1" "$2" "$3"; if [ "$2" = bad ]; then anybad=1; fi; }
        probe=.coai-preflight-probe
        tcp() { timeout 3 bash -c "exec 3<>/dev/tcp/$1/$2" >/dev/null 2>&1; }
        """;

    private const string MainHidden = """
        entries=$(ls -A "$main" 2>/dev/null)
        if [ ! -d "$main/.git" ]; then row main-hidden bad "the main checkout has no .git folder inside the unit, so git could not read its objects"
        elif [ "$entries" != ".git" ]; then row main-hidden bad "something besides .git is listed in the main checkout: it is not hidden"
        elif [ "$(stat -f -c %T "$main")" != tmpfs ]; then row main-hidden bad "the main checkout is not covered by an empty tmpfs"
        elif { true > "$main/.git/$probe"; } 2>/dev/null; then rm -f "$main/.git/$probe"; row main-hidden bad "a file was WRITTEN in the .git folder of the main checkout"
        else row main-hidden ok "only .git of the main checkout is there, read-only, over an empty tmpfs"; fi
        """;

    private const string CoderRootHidden = """
        want=$(basename "$wt"); have=$(ls -A "$root" 2>/dev/null)
        if [ "$have" != "$want" ]; then row coder-root-hidden bad "something besides the job's own worktree is listed beside it"
        elif [ "$(stat -f -c %T "$root")" != tmpfs ]; then row coder-root-hidden bad "the folder beside the worktree is not an empty tmpfs: a write there would reach the disk"
        else row coder-root-hidden ok "only the job's own worktree is beside it, over an empty tmpfs"; fi
        """;

    /// <summary>
    /// Written AFTER the two rows above have looked (the rows list the folders): one marker in the main checkout and one beside the worktree. Inside a covered unit they land in the unit's own tmpfs and are gone with it; where a folder is not covered they land on the disk, and the HOST looks for them after the unit has ended
    /// (<see cref="CodexPreflightRows"/>), because the type of the file system a folder is on is no proof that it is the unit's (a tmpfs disk is a tmpfs; pass 6, S40).
    /// </summary>
    private const string CoverMarkers = """
        { printf x > "$main/.coai-preflight-cover"; } 2>/dev/null
        { printf x > "$root/.coai-preflight-cover"; } 2>/dev/null
        """;

    /// <summary>The name of the marker of <see cref="CoverMarkers"/>.</summary>
    internal const string CoverMarker = ".coai-preflight-cover";

    private const string TempRow = """
        if [ -e "/tmp/.coai-job-$job" ]; then row tmp-is-the-jobs bad "the job's temp folder is bound at /tmp, which this unit covers with its own tmpfs"
        elif [ "$(stat -f -c %T /tmp)" != tmpfs ]; then row tmp-is-the-jobs bad "/tmp is not a tmpfs of the unit"
        elif [ ! -e "/var/tmp/.coai-job-$job" ]; then row tmp-is-the-jobs bad "/var/tmp is not the job's own folder"
        else row tmp-is-the-jobs ok "/tmp is the unit's own tmpfs and /var/tmp is the job's own folder"; fi
        """;

    private const string SignInAbsentRow = """
        if head -c 1 "$rh/.claude/.credentials.json" >/dev/null 2>&1 || head -c 1 "$rh/.gemini/antigravity-cli/antigravity-oauth-token" >/dev/null 2>&1; then row sign-in-not-in-unit bad "a real sign-in file of another vendor (claude, agy) can be read inside the codex unit"
        else row sign-in-not-in-unit ok "the real sign-in files of claude and agy cannot be read inside the codex unit"; fi
        """;

    private const string KeysRow = """
        if [ -z "$keylist" ]; then row keys-folder-hidden ok "no key folder or keys file is named for this job, so there is none to hide"
        else
          seen=
          set -f
          for k in $keylist; do if [ -r "$k" ] && { ls -A "$k" >/dev/null 2>&1 || head -c 1 "$k" >/dev/null 2>&1; }; then seen="$seen $k;"; fi; done
          set +f
          if [ -z "$seen" ]; then row keys-folder-hidden ok "the claude key folder and the keys file cannot be read from inside the unit"; else row keys-folder-hidden bad "key material can be read from inside the unit:$seen"; fi
        fi
        """;

    /// <summary>
    /// U7: the names of the unit's environment against the vendor's allow-list, and the values. The unit is the preflight's, started with the allow-list alone: a LAUNCH can hold up to ten names more that cross by name (the proxy, certificate and time-zone names of coai's own environment, <c>CoderUnit.PassableNames</c>), which this row did not see (pass 6, S49).
    /// </summary>
    private const string EnvironmentRow = """
        have=$(env | sed 's/=.*//' | grep -vxE 'PWD|OLDPWD|_|SHLVL' | sort | tr '\n' ' ')
        want=$(printf '%s\n' "$expenv" | sed 's/=.*//' | sort | tr '\n' ' ')
        wrong=
        oldifs=$IFS
        IFS='
        '
        for pair in $expenv; do n=$(printf '%s' "$pair" | cut -d= -f1); v=$(printf '%s' "$pair" | cut -d= -f2-); [ "$(printenv "$n")" = "$v" ] || wrong=1; done
        IFS=$oldifs
        if [ "$have" != "$want" ]; then row environment-exact bad "the names in the unit's environment are not exactly the allow-list of the vendor"
        elif [ -n "$wrong" ]; then row environment-exact bad "a value in the unit's environment is not the one coai set"
        else row environment-exact ok "the environment is exactly the allow-list, name by name and value by value"; fi
        """;

    private const string OtherJobRow = """
        if [ -z "$otherjob" ]; then row coder-other-job-hidden bad "no stand-in for another job's folder could be made, so nothing was observed"
        elif [ -e "$otherjob" ] || ls -A "$otherjob" >/dev/null 2>&1 || cat "$otherjob/canary.txt" >/dev/null 2>&1 || { true > "$otherjob/$probe"; } 2>/dev/null; then rm -f "$otherjob/$probe" 2>/dev/null; row coder-other-job-hidden bad "a stand-in folder of another job is visible, readable or writable from inside the unit"
        else row coder-other-job-hidden ok "a stand-in folder of another job cannot be seen, read or written from inside the unit"; fi
        """;

    private const string ProgramRow = """
        if [ ! -x "$prog" ]; then row program-unwritable bad "the program of the launch is not there, or cannot be run, inside the unit"
        elif { true >> "$prog"; } 2>/dev/null; then row program-unwritable bad "the program file can be WRITTEN from inside the unit"
        elif { true > "$progdir/$probe"; } 2>/dev/null; then rm -f "$progdir/$probe"; row program-unwritable bad "a file can be WRITTEN in the folder of the program"
        else row program-unwritable ok "the program is there, can be run, and neither it nor its folder can be written"; fi
        """;

    /// <summary>The probe that runs INSIDE the vendor sandbox (one string, no single quote in it): it prints fixed tokens and nothing else. The positional arguments are the worktree, the main checkout, the job's codex folder, the auth file, a canary file, the coder root, the listener's port and the pid of the canary process.</summary>
    private const string SandboxProbe = """
        sbscript='wt=$1; main=$2; vendor=$3; auth=$4; cfile=$5; root=$6; port=$7; pid=$8
        echo sb-started
        head -c 1 "$wt/.git" >/dev/null 2>&1 && echo sb-saw-wt-file
        [ -n "$(ls -A "$wt" 2>/dev/null)" ] && echo sb-list-wt
        [ -e /proc/self ] && echo sb-proc-self
        command -v timeout >/dev/null 2>&1 && command -v bash >/dev/null 2>&1 && command -v grep >/dev/null 2>&1 && echo sb-tools
        { true > "$wt/.coai-preflight-probe"; } 2>/dev/null && echo sb-wt-write
        { true > "$vendor/.coai-preflight-probe"; } 2>/dev/null
        { true > "$root/.coai-preflight-probe"; } 2>/dev/null
        head -c 1 "$auth" >/dev/null 2>&1 && echo sb-read-auth
        head -c 1 "$cfile" >/dev/null 2>&1 && echo sb-read-canary
        [ -n "$(ls -A "$vendor" 2>/dev/null)" ] && echo sb-list-vendor
        [ -n "$(ls -A "$main" 2>/dev/null)" ] && echo sb-list-main
        timeout 3 bash -c "exec 3<>/dev/tcp/127.0.0.1/$port" >/dev/null 2>&1 && echo sb-net
        [ -e "/proc/$pid" ] && echo sb-proc-canary
        grep -q COAI_PF_CANARY /proc/[0-9]*/environ >/dev/null 2>&1 && echo sb-env-canary
        printf x >> "$wt/.git" 2>/dev/null
        { true > "$wt/.agents/.coai-preflight-probe"; } 2>/dev/null
        { true > "$wt/.codex/.coai-preflight-probe"; } 2>/dev/null
        echo sb-done'
        """;

    /// <summary>
    /// CX-V1 to CX-V6. The canary process holds a name only this unit knows in its environment; the canary file stands for the job's sign-in and folder. The probe runs in the sandbox; this program, outside it, reads what the UNIT then sees. The folders <c>.agents</c> and <c>.codex</c> of the worktree
    /// are made first when they are not there (the real sandbox makes them as read-only masks, and a write into a folder that is not there proves nothing) and taken away after when they were made here and are empty.
    /// </summary>
    private const string Sandbox = """
        COAI_PF_CANARY=1 sleep 120 &
        canary=$!
        cfile="$chome/.coai-preflight-canary"
        printf 'canary\n' > "$cfile" 2>/dev/null
        madeagents=; madecodex=
        [ -d "$wt/.agents" ] || { mkdir "$wt/.agents" 2>/dev/null && madeagents=1; }
        [ -d "$wt/.codex" ] || { mkdir "$wt/.codex" 2>/dev/null && madecodex=1; }
        gitsum=$(cksum < "$wt/.git" 2>/dev/null)
        """ + "\n" + SandboxProbe + """

        sbout=$(timeout -k 2 40 "$prog" sandbox -P coai -- /bin/sh -c "$sbscript" coai-sandbox "$wt" "$main" "$vendor" "$auth" "$cfile" "$root" "$port" "$canary" 2>/dev/null)
        has() { printf '%s\n' "$sbout" | grep -qx "$1"; }
        ctl_net=; tcp 127.0.0.1 "$port" && ctl_net=1
        ctl_proc=; [ -e "/proc/$canary" ] && grep -q COAI_PF_CANARY /proc/[0-9]*/environ >/dev/null 2>&1 && ctl_proc=1
        ctl_read=; head -c 1 "$cfile" >/dev/null 2>&1 && ctl_read=1
        if has sb-started && has sb-done; then
          row vendor-sandbox-starts ok "the vendor sandbox started with the profile coai inside the unit"
          if ! has sb-wt-write || [ ! -e "$wt/$probe" ]; then row vendor-sandbox-writes bad "a write in the worktree did not work inside the vendor sandbox"
          elif [ -e "$vendor/$probe" ] || [ -e "$root/$probe" ]; then row vendor-sandbox-writes bad "a write of the vendor sandbox into the job folder or beside the worktree reached the disk"
          else row vendor-sandbox-writes ok "a write in the worktree works, and a write into the job folder or beside the worktree did not reach the disk"; fi
          if [ -z "$ctl_read" ]; then row vendor-sandbox-reads bad "the canary file could not be read from the unit itself, so nothing was observed"
          elif ! has sb-tools || ! has sb-saw-wt-file || ! has sb-list-wt; then row vendor-sandbox-reads bad "the probe could not read or list its own worktree inside the vendor sandbox, so nothing was observed"
          elif has sb-read-auth || has sb-read-canary || has sb-list-vendor || has sb-list-main; then row vendor-sandbox-reads bad "the vendor sandbox can read or list the job's sign-in, the job folder or the main checkout"
          else row vendor-sandbox-reads ok "the job's sign-in, the job folder and the main checkout cannot be read or listed from inside the vendor sandbox"; fi
          if [ -z "$ctl_net" ]; then row vendor-sandbox-network bad "the listener of coai on the host loopback could not be reached from the unit itself, so nothing was observed"
          elif ! has sb-tools; then row vendor-sandbox-network bad "the tools of the probe were not found inside the vendor sandbox, so nothing was observed"
          elif has sb-net; then row vendor-sandbox-network bad "the vendor sandbox reached a listener on the host loopback"
          else row vendor-sandbox-network ok "a listener on the host loopback is reached from the unit and not from the vendor sandbox"; fi
          if [ -z "$ctl_proc" ]; then row vendor-sandbox-processes bad "the canary process could not be seen from the unit itself, so nothing was observed"
          elif ! has sb-tools || ! has sb-proc-self; then row vendor-sandbox-processes bad "the probe could not look at its own processes inside the vendor sandbox, so nothing was observed"
          elif has sb-proc-canary || has sb-env-canary; then row vendor-sandbox-processes bad "the vendor sandbox sees a process or an environment of the unit"
          else row vendor-sandbox-processes ok "no process and no environment of the unit is visible inside the vendor sandbox"; fi
          if [ -e "$wt/.agents/$probe" ] || [ -e "$wt/.codex/$probe" ] || [ "$gitsum" != "$(cksum < "$wt/.git" 2>/dev/null)" ]; then row vendor-sandbox-protected-names bad "the vendor sandbox could change .git, .agents or .codex of the worktree"
          else row vendor-sandbox-protected-names ok ".git, .agents and .codex of the worktree were not changed by the vendor sandbox"; fi
        else
          for item in vendor-sandbox-starts vendor-sandbox-writes vendor-sandbox-reads vendor-sandbox-network vendor-sandbox-processes vendor-sandbox-protected-names; do row "$item" bad "the vendor sandbox did not start with the profile coai inside the unit, so this was not observed"; done
        fi
        rm -f "$wt/$probe" "$vendor/$probe" "$root/$probe" "$wt/.agents/$probe" "$wt/.codex/$probe" "$cfile"
        [ -n "$madeagents" ] && rmdir "$wt/.agents" 2>/dev/null
        [ -n "$madecodex" ] && rmdir "$wt/.codex" 2>/dev/null
        kill "$canary" 2>/dev/null
        """;

    private const string Probes = """
        data() { printf 'data\t%s\t%s\n' "$1" "$2"; }
        IFS='
        '
        set -f
        set -- $flagtext
        set +f
        unset IFS
        data tools-worktree "$(cd "$wt" && timeout -k 2 40 "$prog" debug prompt-input "$@" x 2>/dev/null | base64 -w0)"
        sp=/tmp/coai-pf-standin
        rm -rf "$sp"; mkdir -p "$sp/.codex" 2>/dev/null
        printf '[mcp_servers.coai_probe]\ncommand = "/bin/true"\n' > "$sp/.codex/config.toml" 2>/dev/null
        ( cd "$sp" && git init -q 2>/dev/null )
        data tools-standin "$(cd "$sp" && timeout -k 2 40 "$prog" debug prompt-input "$@" x 2>/dev/null | base64 -w0)"
        data doctor "$(cd "$wt" && timeout -k 2 40 "$prog" doctor --json --summary "$@" 2>/dev/null | base64 -w0)"
        rm -rf "$sp"
        """;

    private const string ConfigRow = """
        printf '%s' "$expconf" > /tmp/coai-pf-expected-config
        if [ -e "$chome/environments.toml" ]; then row config-read-back bad "the job's CODEX_HOME holds a file named environments.toml"
        elif cmp -s /tmp/coai-pf-expected-config "$chome/config.toml"; then row config-read-back ok "the config.toml of the job's CODEX_HOME is byte for byte what coai wrote"
        else row config-read-back bad "the config.toml of the job's CODEX_HOME is not what coai wrote"; fi
        rm -f /tmp/coai-pf-expected-config
        """;

    private const string OverrideRow = """
        if [ "$(printenv CODEX_REFRESH_TOKEN_URL_OVERRIDE)" != "$ourl" ]; then row refresh-override bad "the refresh override is not in the unit's environment with the dead local address"
        elif ! tcp 127.0.0.1 "$port"; then row refresh-override bad "a connection from the unit to a listener of coai could not be made, so a refused connection cannot be told from a tool that cannot connect"
        elif tcp "$ohost" "$oport"; then row refresh-override bad "a connection to the address of the refresh override was ANSWERED; it must be refused"
        else row refresh-override ok "the refresh override is set to a dead local address and a connection to it is refused"; fi
        """;

    private const string CopyRow = """
        if [ ! -f "$auth" ] || [ -L "$auth" ]; then row sign-in-is-the-copy bad "the job's sign-in is not a regular file inside the unit"
        elif [ "$(stat -c %a "$auth")" != 600 ] || [ "$(stat -c %h "$auth")" != 1 ]; then row sign-in-is-the-copy bad "the job's sign-in file is not mode 0600 with one name"
        elif head -c 1 "$rh/.codex/auth.json" >/dev/null 2>&1; then row sign-in-is-the-copy bad "the person's real codex sign-in can be read from inside the unit"
        else row sign-in-is-the-copy ok "the sign-in in the unit is the job's own copy, mode 0600, and the real one cannot be read"; fi
        refresh=$(grep -o 'refresh_token' "$auth" 2>/dev/null | wc -l | tr -d ' ')
        if [ "$refresh" = 1 ] && grep -qF "\"refresh_token\":\"$placeholder\"" "$auth" 2>/dev/null; then row copy-has-the-placeholder ok "the copy holds exactly the placeholder coai wrote in the place of the refresh token"
        else row copy-has-the-placeholder bad "the copy does not hold exactly the placeholder in the place of the refresh token"; fi
        """;

    /// <summary>The whole program for a job: with the submodule row when the job has a mount (its pairs are the arguments after the fixed ones).</summary>
    internal static string For(BoundaryJob job) =>
        BoundaryPreflight.Join(Header, BoundaryPreflight.WorktreeRow, MainHidden, CoderRootHidden, CoverMarkers, BoundaryPreflight.GitRow, BoundaryPreflight.ManagerRow, BoundaryPreflight.ExeRow, BoundaryPreflight.SocketsRow,
            BoundaryPreflight.HomeRow, TempRow, SignInAbsentRow, KeysRow, EnvironmentRow, OtherJobRow, ProgramRow, job.Mounts.Count == 0 ? string.Empty : BoundaryPreflight.SubmodulesRow(0), "if [ -z \"$anybad\" ]; then", Sandbox, Probes, "fi", ConfigRow, OverrideRow, CopyRow);
}
