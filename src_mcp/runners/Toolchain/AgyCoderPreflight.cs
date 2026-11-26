using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CoaiMcp.Runners.Consultation;
using CoaiMcp.Runners.Files;
using CoaiMcp.Runners.Reviewers;

namespace CoaiMcp.Runners.Toolchain;

/// <summary>
/// The preflight of an agy coder job (epoch 3 E3 s6 t4; judgement section 8, V9): a shell program that runs INSIDE the exact launch unit — the property list and the working directory the launch gets — with no model, and answers one row for each id of class <c>pre</c> of the table for agy.
/// The rows that the claude coder's preflight already asks (the worktree, the <c>.git</c> entries, no user manager, no Windows program, the home and the temp folders) are the same fragments of <see cref="BoundaryPreflight"/>; the rest is agy's: the main checkout and the coder root HIDDEN (U2, U3), the environment
/// compared name by name (U7), another job's folder (U8), the program that cannot be written (U9), the working directory (AG-B1), the guard folder that cannot be written (AG-B3), the sign-in copy (AG-R1, AG-R2) and the guard handler answered through the exact command of <c>hooks.json</c> (AG-G1, AG-G2, AG-G3).
/// </summary>
/// <remarks>
/// <para><b>What the rows do not prove</b> (the envelope says it, <see cref="NotProved"/>): that agy calls the hook for every tool, that the agent file limits its tools, that a hook in the worktree does not run and what mode a turn ran in are ids of class <c>release</c> (AG-G4, AG-A1, AG-B2, AG-M4) — measured, never asked
/// before a job. A row for them could only check coai's own half and would prove nothing.</para>
/// <para><b>The probes of the guard write records.</b> The handler records every decision, the preflight's own probes included, in the records file; <see cref="ICoderVendor.BeforeStart"/> puts that file away right before the turn, so the records the turn check reads are the turn's own.</para>
/// </remarks>
internal static class AgyCoderPreflight
{
    /// <summary>The id of each row (an item of <see cref="Items"/>), by the ids of the judgement. K1 and K2 are rows of the CHECK preflight (<see cref="CheckRows"/>).</summary>
    internal static readonly IReadOnlyDictionary<string, string> RowIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BoundaryPreflight.WorktreeWrite] = "U1",
        ["main-hidden"] = "U2",
        ["coder-root-hidden"] = "U3",
        [BoundaryPreflight.GitReadOnly] = "U4",
        [BoundaryPreflight.NoUserManager] = "U5",
        [BoundaryPreflight.WindowsProgram] = "U5",
        [BoundaryPreflight.NoSocketsNoInterop] = "U5",
        [BoundaryPreflight.HomeIsTheJobs] = "U6",
        [BoundaryPreflight.TempIsTheJobs] = "U6",
        ["environment-is-the-allow-list"] = "U7",
        ["other-job-folder-hidden-from-the-coder"] = "U8",
        ["program-cannot-be-written"] = "U9",
        ["cwd-is-the-guard-folder"] = "AG-B1",
        ["guard-folder-readonly"] = "AG-B3",
        ["sign-in-is-the-jobs-copy"] = "AG-R1",
        ["sign-in-copy-has-no-refresh-token-and-the-margin"] = "AG-R2",
        ["guard-answers-each-case"] = "AG-G1",
        ["guard-answers-empty-and-garbage"] = "AG-G2",
        ["guard-files-are-the-expected-bytes"] = "AG-G3",
    };

    /// <summary>
    /// The rows that are no id of the table, and that every other-vendor launch asked of the claude coder's preflight (pass 5, M12): the claude key pool cannot be read and the person's claude sign-in file is not there (the shared fragments of the check preflight), and the folder of another vendor of the job
    /// is not there (SPEC E7: a unit holds only its own vendor's folder). Their properties are in the unit; these rows observe them.
    /// </summary>
    internal static IReadOnlyList<string> ExtraItems { get; } = [BoundaryPreflight.KeysHidden, BoundaryPreflight.SignInAbsent, "other-vendor-folder-hidden"];

    /// <summary>The rows, in the order the script asks them.</summary>
    internal static IReadOnlyList<string> Items => [.. RowIds.Keys, .. ExtraItems];

    /// <summary>The ids of the CHECK preflight that the table lists as <c>pre</c> and that this one does not ask (the check unit has them): K1 and K2, with the row each is.</summary>
    internal static readonly IReadOnlyDictionary<string, string> CheckRows = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["K1"] = BoundaryPreflight.CoderFolderHidden,
        ["K2"] = BoundaryPreflight.OtherJobFolderHidden,
    };

    /// <summary>What the rows do not prove, said in the envelope: the ids of class <c>release</c>, measured and never asked before a job.</summary>
    internal const string NotProved =
        "not proved by the preflight, measured on the release named beside the installed one: that agy calls the hook for every tool (AG-G4), that the agent file limits its tools (AG-A1), that a hook or an agent in the worktree does not run (AG-B2) and what mode a turn ran in (AG-M4)";

    private const string Header = """
        root=$8; guard=$9; shift 9; prog=$1; other=$2; deadline=$3; shaagent=$4; shahooks=$5; shash=$6; shacmd=$7; shaguard=$8; envnames=$9; shift 9; keylist=$1; vendordirs=$2; shift 2
        """;

    private const string MainHiddenRow = """
        left=$(ls -A "$main" 2>/dev/null)
        if [ ! -d "$main/.git" ]; then row main-hidden bad "the main .git is not there inside the unit, so git could not read its objects"
        elif [ "$left" != ".git" ]; then row main-hidden bad "the main checkout shows more than its .git inside the unit: it is not hidden"
        elif { true > "$main/.git/$probe"; } 2>/dev/null; then rm -f "$main/.git/$probe"; row main-hidden bad "a file was WRITTEN in the main .git"
        elif [ "$(stat -f -c %T "$main" 2>/dev/null)" != tmpfs ]; then row main-hidden bad "the main checkout is not an empty tmpfs inside the unit"
        else row main-hidden ok "the main checkout is an empty tmpfs with only its .git bound back, read-only"; fi
        """;

    private const string RootHiddenRow = """
        left=$(ls -A "$root" 2>/dev/null)
        if [ "$left" != "$(basename "$wt")" ]; then row coder-root-hidden bad "the coder root shows more than the worktree inside the unit: it is not hidden"
        elif [ "$(stat -f -c %T "$root" 2>/dev/null)" != tmpfs ]; then row coder-root-hidden bad "the coder root is not an empty tmpfs inside the unit"
        else row coder-root-hidden ok "the coder root is an empty tmpfs with only the job's worktree bound back"; fi
        """;

    private const string EnvironmentRow = """
        seen=$(env | sed -n 's/^\([A-Za-z_][A-Za-z0-9_]*\)=.*/\1/p' | grep -vxE 'PWD|OLDPWD|SHLVL|_' | sort | tr '\n' ' ')
        want=$(printf '%s\n' $envnames | sort | tr '\n' ' ')
        if [ "$seen" = "$want" ]; then row environment-is-the-allow-list ok "the environment holds exactly the names of the vendor's allow-list"; else row environment-is-the-allow-list bad "the environment is not exactly the vendor's allow-list: it holds $seen"; fi
        """;

    private const string OtherJobRow = """
        if [ -z "$other" ]; then row other-job-folder-hidden-from-the-coder bad "no stand-in for another job's folder could be made, so nothing was observed"
        elif [ -e "$other" ] || ls -A "$other" >/dev/null 2>&1 || cat "$other/canary.txt" >/dev/null 2>&1 || { true > "$other/$probe"; } 2>/dev/null; then rm -f "$other/$probe" 2>/dev/null; row other-job-folder-hidden-from-the-coder bad "a stand-in folder of another job is visible, readable or writable from inside the coder's unit"
        else row other-job-folder-hidden-from-the-coder ok "a stand-in folder of another job cannot be seen, read or written from inside the coder's unit"; fi
        """;

    private const string ProgramRow = """
        if [ ! -x "$prog" ]; then row program-cannot-be-written bad "the program of the launch is not there, or cannot be run, inside the unit"
        elif { true >> "$prog"; } 2>/dev/null; then row program-cannot-be-written bad "the program of the launch can be WRITTEN from inside the unit"
        else row program-cannot-be-written ok "the program of the launch is there, can be run and cannot be written"; fi
        """;

    private const string CwdRow = """
        here=$(pwd -P)
        if [ "$here" != "$guard" ]; then row cwd-is-the-guard-folder bad "the working directory of the unit is not the guard folder"
        elif [ "$here" = "$wt" ]; then row cwd-is-the-guard-folder bad "the working directory of the unit is the worktree"
        else row cwd-is-the-guard-folder ok "the unit starts in the guard folder, and the worktree is no working directory"; fi
        """;

    private const string GuardReadOnlyRow = """
        if { true > "$guard/$probe"; } 2>/dev/null; then rm -f "$guard/$probe"; row guard-folder-readonly bad "a file was WRITTEN in the guard folder"
        elif { true > "$guard/.agents/$probe"; } 2>/dev/null; then rm -f "$guard/.agents/$probe"; row guard-folder-readonly bad "a file was WRITTEN in the guard's .agents folder"
        else row guard-folder-readonly ok "a write into the guard folder and its .agents folder was refused"; fi
        """;

    private const string SignInRow = """
        cred=$HOME/.gemini/antigravity-cli/antigravity-oauth-token
        count=$(find "$HOME/.gemini" -type f 2>/dev/null | grep -Eic 'token|oauth|credential')
        if [ ! -f "$cred" ]; then row sign-in-is-the-jobs-copy bad "the job's sign-in copy is not in the unit's home"
        elif [ "$(stat -c %a "$cred")" != 600 ]; then row sign-in-is-the-jobs-copy bad "the sign-in copy is not mode 0600"
        elif [ "$count" != 1 ]; then row sign-in-is-the-jobs-copy bad "another sign-in file is in the unit's home"
        elif [ -e "$rh/.gemini" ]; then row sign-in-is-the-jobs-copy bad "the real ~/.gemini is visible inside the unit"
        else row sign-in-is-the-jobs-copy ok "the sign-in is the job's copy, mode 0600, the only one there, and the real ~/.gemini is not seen"; fi
        """;

    private const string CopyRow = """
        if grep -q 'refresh_token' "$cred" 2>/dev/null; then row sign-in-copy-has-no-refresh-token-and-the-margin bad "the sign-in copy holds a refresh token"
        else
          exp=$(sed -n 's/.*"expiry" *: *"\([^"]*\)".*/\1/p' "$cred" 2>/dev/null | head -n 1)
          at=$(date -d "$exp" +%s 2>/dev/null)
          if [ -z "$at" ]; then row sign-in-copy-has-no-refresh-token-and-the-margin bad "the expiry of the sign-in copy cannot be read"
          elif [ "$at" -lt "$deadline" ]; then row sign-in-copy-has-no-refresh-token-and-the-margin bad "the access token of the copy ends before the unit's end and its margin"
          else row sign-in-copy-has-no-refresh-token-and-the-margin ok "the copy holds no refresh token and its access token outlives the unit by the margin"; fi
        fi
        """;

    private const string VendorsRow = """
        if [ -z "$vendordirs" ]; then row other-vendor-folder-hidden bad "no folder of another vendor of the job was named, so nothing was observed"
        else
          seen=
          for d in $vendordirs; do if [ -e "$d" ] || ls -A "$d" >/dev/null 2>&1; then seen="$seen $d;"; fi; done
          if [ -z "$seen" ]; then row other-vendor-folder-hidden ok "the folders of the other vendors of the job cannot be seen from inside the unit"; else row other-vendor-folder-hidden bad "a folder of another vendor of the job is visible inside the unit:$seen"; fi
        fi
        """;

    /// <summary>The probes of the guard. <c>ask</c> runs the exact command of hooks.json from its folder; each case names a number, never a path.</summary>
    private const string GuardRows = """
        ask() { printf '%s' "$1" | { cd "$guard/.agents" && sh ./coai-hook.sh; } 2>/dev/null; }
        pay() { printf '{"conversationId":"coai-preflight","cwd":"%s","hookEventName":"PreToolUse","stepIdx":%s,"toolCall":{"args":{"Path":"%s"},"id":"call_p","name":"%s"},"workspacePaths":["%s","%s"]}' "$guard" "$1" "$3" "$2" "$guard" "$wt"; }
        nop() { printf '{"conversationId":"coai-preflight","cwd":"%s","hookEventName":"PreToolUse","stepIdx":%s,"toolCall":{"args":{},"id":"call_p","name":"write_file"},"workspacePaths":["%s","%s"]}' "$guard" "$1" "$guard" "$wt"; }
        ln -s "$rh/.coai-outside-file" "$wt/.coai-preflight-file-link" 2>/dev/null
        ln -s "$rh" "$wt/.coai-preflight-folder-link" 2>/dev/null
        wrong=
        n=1
        verdict() { case "$1" in *'"decision":"allow"'*) echo allow;; *'"decision":"deny"'*) echo deny;; *) echo none;; esac; }
        expect() { n=$((n+1)); got=$(verdict "$2"); [ "$got" = "$1" ] || wrong="$wrong $n:$got"; }
        expect allow "$(ask "$(pay 2 write_file "$wt/.coai-preflight-allowed.txt")")"
        expect deny "$(ask "$(pay 3 write_file "$rh/.coai-outside")")"
        expect deny "$(ask "$(pay 4 write_file "$wt/.git")")"
        expect deny "$(ask "$(pay 5 write_file "$wt/.agents/x")")"
        expect deny "$(ask "$(pay 6 write_file "$wt/.codex/x")")"
        expect deny "$(ask "$(pay 7 write_file "$wt/.coai-preflight-file-link")")"
        expect deny "$(ask "$(pay 8 write_file "$wt/.coai-preflight-folder-link/x")")"
        expect deny "$(ask "$(pay 9 read_file "$guard/.agents/hooks.json")")"
        expect deny "$(ask "$(pay 10 read_file "$HOME/.gemini/antigravity-cli/antigravity-oauth-token")")"
        expect deny "$(ask "$(pay 11 run_command "$wt/a")")"
        expect deny "$(ask "$(pay 12 read_url_content "$wt/a")")"
        expect deny "$(ask "$(pay 13 search_web "$wt/a")")"
        expect deny "$(ask "$(pay 14 manage_task "$wt/a")")"
        expect deny "$(ask "$(pay 15 multi_edit_file "$wt/a")")"
        expect deny "$(ask "$(nop 16)")"
        rm -f "$wt/.coai-preflight-file-link" "$wt/.coai-preflight-folder-link"
        if [ -z "$wrong" ]; then row guard-answers-each-case ok "the handler allowed the write inside the worktree and denied every other case, through the exact command of hooks.json"; else row guard-answers-each-case bad "the handler answered wrongly for case$wrong (number:answer)"; fi
        empty=$(verdict "$(ask "")")
        garbage=$(verdict "$(ask "text that is not JSON")")
        if [ "$empty" = deny ] && [ "$garbage" = deny ]; then row guard-answers-empty-and-garbage ok "an empty input and text that is not JSON are each answered with an explicit deny"; else row guard-answers-empty-and-garbage bad "the handler did not answer an empty input and text that is not JSON with deny (empty: $empty, text: $garbage)"; fi
        same=
        [ "$(sha256sum "$guard/.agents/agents/coai-coder.md" 2>/dev/null | cut -d' ' -f1)" = "$shaagent" ] || same="$same agent-file"
        [ "$(sha256sum "$guard/.agents/hooks.json" 2>/dev/null | cut -d' ' -f1)" = "$shahooks" ] || same="$same hooks.json"
        [ "$(sha256sum "$guard/.agents/coai-hook.sh" 2>/dev/null | cut -d' ' -f1)" = "$shash" ] || same="$same hook-script"
        [ "$(sha256sum "$guard/.agents/coai-hook.cmd" 2>/dev/null | cut -d' ' -f1)" = "$shacmd" ] || same="$same hook-cmd"
        [ "$(sha256sum "$guard/.agents/coai-guard.json" 2>/dev/null | cut -d' ' -f1)" = "$shaguard" ] || same="$same guard-file"
        if [ -z "$same" ]; then row guard-files-are-the-expected-bytes ok "hooks.json, the agent file, the hook scripts and the guard's own file are the expected bytes, read back inside the unit"; else row guard-files-are-the-expected-bytes bad "not the expected bytes:$same"; fi
        """;

    private static string Script(BoundaryJob job) =>
        BoundaryPreflight.Join(BoundaryPreflight.Header, Header, BoundaryPreflight.WorktreeRow, MainHiddenRow, RootHiddenRow, BoundaryPreflight.GitRow, BoundaryPreflight.ManagerRow, BoundaryPreflight.ExeRow, BoundaryPreflight.SocketsRow,
            BoundaryPreflight.HomeRow, BoundaryPreflight.TempRow, EnvironmentRow, OtherJobRow, ProgramRow, CwdRow, GuardReadOnlyRow, SignInRow, CopyRow, BoundaryPreflight.CheckKeysRow, BoundaryPreflight.CheckSignInRow, VendorsRow, GuardRows,
            job.Mounts.Count == 0 ? string.Empty : BoundaryPreflight.SubmodulesRow(0));

    /// <summary>Where the stand-in for another job's folder is made: beside the job folder, in the folder that holds the job folders, with a canary in it. Empty when it could not be made.</summary>
    internal static string PlantOtherJob(BoundaryJob job)
    {
        try
        {
            var holding = Path.GetDirectoryName(job.JobRoot.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty;
            var folder = Path.Combine(holding, $"coai-preflight-other-{job.JobId}");
            if (RealPath.Lstat(folder).Kind is not (EntryKind.Missing or EntryKind.Directory))
            {
                return string.Empty;
            }

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "canary.txt"), "another job's file\n");

            return folder;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>The folders of the other vendors of the job, each made when it is not there (and a canary in it), so that "not visible from inside" is an observation and not an absent folder; with the ones that were made here, which are removed after.</summary>
    internal static (IReadOnlyList<string> Folders, IReadOnlyList<string> Made) PlantOtherVendors(BoundaryJob job)
    {
        var folders = CoderVendors.Shipped.Implemented.Select(vendor => vendor.Id).Distinct(StringComparer.Ordinal).Where(id => id != AgyCoderVendor.VendorId).Select(job.CoderVendorFolder).ToList();
        var made = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                if (RealPath.Lstat(folder).Kind == EntryKind.Missing)
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "canary.txt"), "another vendor's file\n");
                    made.Add(folder);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A folder that cannot be made is not named to the row: the row then says nothing was observed.
                return ([], made);
            }
        }

        return (folders, made);
    }

    /// <summary>Removes the stand-in and what a probe may have left in the worktree.</summary>
    internal static void Tidy(BoundaryJob job, string other, IReadOnlyList<string> madeVendorFolders)
    {
        foreach (var folder in madeVendorFolders)
        {
            if (RealPath.Lstat(folder).Kind == EntryKind.Directory)
            {
                GitScratch.DeleteEvenIfReadOnly(folder);
            }
        }

        foreach (var name in new[] { ".coai-preflight-file-link", ".coai-preflight-folder-link", ".coai-preflight-allowed.txt" })
        {
            var path = Path.Combine(job.Tree.Root, name);
            if (RealPath.Lstat(path).Kind is EntryKind.File or EntryKind.Link)
            {
                File.Delete(path);
            }
        }

        if (other.Length > 0 && RealPath.Lstat(other).Kind == EntryKind.Directory)
        {
            GitScratch.DeleteEvenIfReadOnly(other);
        }
    }

    /// <summary>The preflight command of the agy coder's unit for <paramref name="job"/>: the script and the arguments it reads (the places, the stand-in, the deadline the sign-in copy must outlive, the hashes of the guard's files, the allow-list names).</summary>
    internal static IReadOnlyList<string> Command(BoundaryHost host, BoundaryJob job, CoderProgram program, string windowsCopy, string other, IReadOnlyList<string> vendorFolders, HookHandler handler, TimeSpan runtimeMax, DateTime nowUtc)
    {
        var files = AntigravityCoderGuard.Files(handler, job.Tree.Root, AgyCoderUnit.RecordsFile(job)).ToDictionary(file => file.Path, file => file.Content, StringComparer.Ordinal);
        string Sha(string relative) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(files[Path.Combine(".agents", relative)])));

        return
        [
            "/bin/sh", "-c", Script(job), "coai-preflight", job.Tree.Root, job.Tree.MainCheckout, host.ResolvedHome, "host", job.JobId, windowsCopy, host.ResolverFile, job.CoderRoot, AgyCoderUnit.GuardFolder(job),
            program.Real, other, new DateTimeOffset(nowUtc + runtimeMax + TimeSpan.FromMinutes(5)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Sha(Path.Combine("agents", AntigravityCoderGuard.AgentName + ".md")), Sha("hooks.json"), Sha("coai-hook.sh"), Sha("coai-hook.cmd"), Sha(AntigravityCoderGuard.GuardFile),
            string.Join(' ', AgyCoderUnit.Environment(job).Select(pair => pair.Key)),
            string.Join(' ', new[] { job.KeysFolder, job.KeysFile }.Where(path => path.Length > 0)),
            string.Join(' ', vendorFolders),
            .. job.Mounts.SelectMany(mount => new[] { mount.Target, mount.Source }),
        ];
    }
}
