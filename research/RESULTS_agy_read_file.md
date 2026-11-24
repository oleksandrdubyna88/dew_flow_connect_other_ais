# RESULTS — agy 1.5.9 reads with `read_file`, and coai's hook refused it (2026-11-15)

> Found while checking the owner's report that Antigravity consultants "cannot read more than about 150 KB".
> They could not read at all. agy updated itself to **1.5.9** on this machine on 2026-11-14 at 14:28. Since then the
> model is offered a read tool named `read_file`, not `view_file`. coai's `PreToolUse` hook
> ([`AntigravityReadOnly.Decide`](../src_mcp/runners/Reviewers/AntigravityReadOnly.cs)) allowed `view_file` alone, so
> every read was denied with `coai: this consultation is read-only; only view_file runs`. The fix allows exactly
> `view_file` and `read_file`.
>
> Harness, every run: WSL, agy 1.5.9, coai's consult argv (`--print <prompt> --mode plan --agent coai-reader --model …
> --add-dir <root> --output-format stream-json`), run from a coai-agy folder. Arms marked "as shipped" ran from the
> folder of the installed coai-mcp (hook = that binary's `--agy-hook`). Arms marked "test hook" ran from a copy whose
> hook is a script that allows `view_file` or `read_file` and logs every payload. The agent file was unchanged in
> every arm (`tools: - view_file`). The probe file was 307244 bytes and 3533 lines, with `MARKER ONE` on line 2591
> (≈220 KB) and `MARKER TWO` on line 3486 (≈296 KB).

## 1. As shipped: every read denied

| Model | `read_file` calls | Denied by the hook | Marker reached |
|---|---|---|---|
| `gemini-4-argon-low` | 3 | **3** | none |
| `gemini-4-hydrogen-low` | 1 | **1** | none |

Argon reported that the only tools declared to it were `read_file` and `manage_task`. So the agent's
`tools: [view_file]` still limits the toolbox on 1.5.9, and the read tool behind it now has a different name.
No call named `view_file` was made in any arm.

## 2. Test hook: 300 KB is reached to the end — both markers, with gaps at three page ends

`gemini-4-hydrogen-low` made **8** `read_file` calls, all allowed and all `DONE`. It reported both markers on the
right lines, and the last line as 3533. No other tool name reached the hook.

**There is no size limit, but each read is paged.** `read_file` returns at most **800 lines or 46080 bytes** per
call, and says so:

> `Partial read (lines 1-800 of 3533). Showing bytes 0-46080 of 73491 in this line range, truncated at the 46080-byte
> limit. To read the rest of this line range, call this tool again with the same StartLine and EndLine and
> ByteOffset=46080.`

The model paged by line ranges and skipped the last few lines of three ranges, where they ran past the byte cap. Both
markers sat outside those gaps. A consultant asked to read a large file reads it in pieces, and may miss the tail of a
piece.

## 3. Where a read may go, with `read_file` allowed by the hook

Test hook, `gemini-4-hydrogen-low`, root = a scratch folder under `/tmp`:

| Target | Result |
|---|---|
| a file inside the root | read |
| an absolute path in `$HOME`, outside the root | **denied by agy**: `denied_actions: [{"action":"read_file","display_name":"ReadFile"}]`, empty answer |
| a symlink inside the root pointing into `$HOME` | **denied by agy**: `permission check failed for read_file "/home/…/canary.txt": user denied permission …` (agy resolved the link) |
| a file under `/tmp`, outside the root: by absolute path, by `../`, and by a symlink inside the root | **read**, all three |
| create a file in the root | not attempted: the model said no write tool was available |

**agy's own root boundary still holds, except for `/tmp`.** It held for `$HOME`, including through a link. agy
allows reads under `/tmp` without asking, so a consultation can read another process's temporary files. That
allowance is agy's. coai's hook never checked paths, and this fix does not add a check. The hook payload does carry
`workspacePaths` (the cwd folder and every `--add-dir` root) and `toolCall.args.Path`, so a path check in the hook is
possible. It is recorded as a follow-up for the owner, not done here.

> **Superseded 2026-11-16.** The hook now checks the path: it allows a reader only inside the granted roots and outside
> the coai folder, and the three `/tmp` routes above were denied on the branch build (§6, "Live run on the branch build"). The paragraph above is kept as measured on
> 2026-11-15.

**The two meanings of `read_file` do not collide.** agy also uses `read_file` as a PERMISSION word in
`result.denied_actions`, which `AntigravityRuntime.DeniedActions` and `AntigravityFollowUps.ReadActions` read. A hook
denial is not reported there: in the three arms of §1, `denied_actions` was absent. An agy denial, as in the `$HOME`
row above, is reported there, and coai's existing read follow-up applies to it.

## 4. The write block on 1.5.9 (the probe re-run on the version bump)

[`scripts/probe-agy-write-block.mjs`](../scripts/probe-agy-write-block.mjs), `gemini-4-hydrogen-low`, WSL, 3 runs per arm.
The probe's own handler now allows `view_file` or `read_file`, and its read prompt no longer names a tool:

| Arm | Written | Read the marker | What the model used |
|---|---|---|---|
| `agent-bare` (agent, no prose against writing) | **0 of 3** | — | nothing. It said it had only `read_file` and `manage_task` |
| `hook` (hook alone) | **0 of 3** | — | `write_file`, denied by the hook each time. On 1.3.2 the write tool was `write_to_file`, so that name changed too |
| `both` | **0 of 3** | — | nothing |
| `agent-read` / `hook-read` / `both-read` | 0 | **3 of 3** each | `read_file` |
| `agent-unknown` | 0 of 1 | — | agy refused to start: exit 1, `error: unknown agent "coai-no-such-agent" (no custom agents are available)` |

**Both blocks still hold on 1.5.9, and one fail-open case is now closed.** On 1.3.2 an agent that was not found fell
back to agy's default agent and wrote (RESULTS_agy_write_block.md §3). On 1.5.9 agy refuses to start without it.
This is one run, so it is not counted on. coai still keeps both blocks.

## 5. With the fix: coai's own hook and the real consultant prompt

**The real prompt.** The hook was the built `coai-mcp --agy-hook` of this branch. The prompt carried the shipped
`AntigravityFollowUps.Toolbox` sentence word for word ("Your only tool for reading this checkout is view_file …"), and
asked for both MARKER lines of the same 307244-byte file:

| Model | Runs | `read_file` calls per run | Denied | Both markers on the right lines |
|---|---|---|---|---|
| `gemini-4-argon-low` | 2 | 7 | 0 | **2 of 2** |
| `gemini-4-hydrogen-low` | 2 | 7 | 0 | **2 of 2** |

**The prompt text is kept.** It names `view_file`, and the model read with `read_file` every time.

**The write block with coai's own handler.** These are the probe's `coai-hook` arms, with `PROBE_COAI_HANDLER` set to
the same binary, 3 runs each, `gemini-4-hydrogen-low`:

| Arm | Written | Read the marker |
|---|---|---|
| `coai-hook` (the handler alone, no agent) | **0 of 3**: `write_file` denied with this branch's reason | — |
| `coai-hook-read` | 0 | **3 of 3** |
| `both-nogit` (agent + hook, cwd not a git repository, which is coai's layout) | **0 of 3** | — |
| `both-nogit-read` | 0 | **3 of 3** |

## 6. The hook checks the path (2026-11-16)

§3 found that agy reads under `/tmp` outside its roots, and that the hook did not look at the path. This section records
what the hook is handed, the three routes measured again, and the rule that closes them. The rule is the spec's:
`PLAN_the_agy_hook_checks_the_path_S1_spec.md` (the plan's S1 spec).

**The payload, captured 2026-11-16 on agy 1.5.9.** One `PreToolUse` payload per tool call. Its fields:
`artifactDirectoryPath`, `conversationId`, `cwd`, `hookEventName`, `lastUserInput`, `modelName`, `stepIdx`,
`toolCall` (`args` with `Path`, `id`, `name`), `transcriptPath` and `workspacePaths`.

- `workspacePaths` holds the agy cwd first, then each `--add-dir` root. All entries are absolute.
- `cwd` is the folder coai owns, the one that holds `.agents/hooks.json`. The hook process runs there too: its working
  directory is `<coai folder>/.agents`. It is never a granted root.
  - *(Note 2026-11-18, §7: the two bullets above hold only while the coai folder sorts first. agy SORTS `workspacePaths` and sends `cwd`
    = its first entry, so a granted root that sorts before the coai folder is `cwd`. The coai folder is the parent of the hook's own
    working folder, and `cwd` names nothing. The two lines above stay as they were written.)*
- `toolCall.args.Path` is the model's own text. In 6 of 7 calls it was absolute. Once it was relative, and agy itself
  then rejected it. `..` and links arrive unresolved.
- An environment variable set on agy reaches the hook process. The rule does not use that: it reads the roots from the
  payload, which agy builds from the `--add-dir` arguments coai sets.
- The hook runs before agy's own checks.

**The three escapes, measured again on 2026-11-16 (before the fix).** Root under `/tmp`, a marker file under `/tmp`
outside the root. The marker was read by an absolute path, by `root/../outside/x`, and through a link inside the root
that points at it. All three read, 3 of 3. The hook allowed each call by name alone.

**The rule, in short.** The hook allows `read_file` or `view_file` only when all of these hold. Otherwise it denies.

- `Path` is one string, fully qualified. A relative path is denied.
- The path, with links and `..` followed by `RealPath`, is inside at least one resolved root of `workspacePaths`.
- The path is not inside the resolved `cwd`, the coai folder. This holds even when a granted root contains that folder.
- Every entry of `workspacePaths` and `cwd` must resolve. A payload that lacks `cwd`, `workspacePaths` or `Path`
  reads nothing. Any failure denies; the check never falls back to the spelling.
- A path outside gets its own reason, `coai: this consultation reads only inside the folders it was given`. The
  tool-name reason is unchanged. Neither names the path.

*(Note 2026-11-18, §7: three steps of this list changed. "Not inside the resolved `cwd`, the coai folder" is now "not inside the
resolved coai folder, which the hook knows from where it runs; the payload's `cwd` is validated and names nothing". "Every entry must
resolve" now also means every resolved path must be local: a network or device path is never looked up. On Windows a `Path` with a
`.` or `..` segment is denied. The list above is kept as the rule of 2026-11-16.)*

Limits are in `SECURITY.md`, "What the sites guarantee": a link swapped between the check and agy's open is not closed,
and a hard link or a bind mount is not seen.

### Live run on the branch build

2026-11-16, WSL, agy 1.5.9, `gemini-4-hydrogen-low`, `--mode plan --agent coai-reader`. The hook folder held the same
`hooks.json` and agent file coai writes, and its `coai-hook.sh` started the branch's Release `coai-mcp` with
`--agy-hook` (commit e31c1542). Two job folders under `/tmp`, made fresh: the granted root `J1` with `inside.txt`, and
`J2`, never granted, with `secret.txt`; `J1/leak.txt` is a link to `J2/secret.txt`. One agy run per probe.

| Probe | `Path` the model sent (`read_file`, 2 calls each) | Hook decision, from agy's output | Marker read |
|---|---|---|---|
| inside | `J1/inside.txt` | allowed | the inside marker, yes |
| absolute | `J2/secret.txt` | `tool call denied by pre-tool hook: coai: this consultation reads only inside the folders it was given` | no |
| `..` | `J1/../J2/secret.txt` | the same denial | no |
| link | `J1/leak.txt` | the same denial | no |

Each probe ATTEMPTED the read: the call and its `Path` are in agy's output, so a denial is the hook's, not a model that
did not try. The secret marker appears in none of the four outputs. Script: `live-probe.sh` in the session's scratchpad
(four `agy --print` runs and a grep of the stream for the `Path`, the reason and both markers).

### Windows, not closed here

An Opus review found two Windows-only gaps. This change does not fix them. They move to the follow-up plan
`todo/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md`. (W1) `RealPath` applies `..` to the path resolved so far, the Unix
rule, but Win32 folds `..` as text before it follows a link or junction, so `<root>\junction\..\..\..\Users\me\.ssh\id_rsa` is judged
inside and read outside, if agy allows that read (not measured on Windows). (W2) A UNC `Path` is resolved before it is judged, so the
hook looks it up over SMB: that can send the user's NTLM credentials to that host, or stall past the hook's 10 s timeout. Neither applies on
Linux, the Team server or WSL. Not measured on a Windows machine: both are read from the code and the Win32 rules.

*(Note 2026-11-18: closed by `todo/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md` and its epic E1. Both gaps were measured on a
Windows machine and are described in §7: W1 was confirmed in the real hook, and W2 is closed by a reader that looks at no path that is
not local. The paragraph above is kept as it was written.)*

## 7. Windows, and the payload's cwd (2026-11-18)

Found by step 1 of `todo/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text.md`, which measured agy on Windows for the first time
and, in the same runs, found that §6 had read agy's `cwd` wrong. This section carries the measurement, the W3 finding on both
operating systems, and what the change does about each. The rule and its tests are the specs of the plan's epic E1
(`todo/PLAN_the_agy_hook_on_windows_folds_dot_dot_as_text_E1.md` and its three story specs). Where §6 says otherwise, this section
supersedes it. The text of §6 stays as written.

**Harness.** This machine's Windows host (Windows build 10.0.26300), agy 1.5.11, `gemini-4-hydrogen-low`, `--mode plan --agent
coai-reader`, one agy run per probe. The hook folder held coai's own `hooks.json` and agent file. For the payload captures its
script logged the payload and allowed the call. For the W1 run its script handed the payload to the installed `coai-mcp.exe` 0.66.0
(`--agy-hook`). The raw payloads and event streams are kept in the session's scratchpad (`winprobe/`).

### The payload on Windows

The fields are the same as on Linux (§6). `Path` is absolute, with backslashes and an uppercase `C:`, and it is the model's own text.
The reader tool is `read_file`. `cwd`, `workspacePaths` and `transcriptPath` use backslashes too.

### agy's own boundary on Windows

- A read of `C:\Users\strug\…`, the user profile, outside every granted root: agy denied it itself. The denial is in agy's own
  `denied_actions` (`permission check failed for read_file … user denied permission`).
- A read under the temp folder outside the granted roots: agy allowed it and returned the file. This is the `/tmp` rule of §3 on Windows.
- So the temp folder is the place where only the hook stands between a reader and an outside file.

### The model's `.` and `..`

In 11 runs the model sent no `.` and no `..` in any ordinary path. The `..` paths of the W1 runs were asked for in the prompt, with
"do not change or normalize it".

### W1: `..` after a junction, in the real hook

`RealPath` applies `..` to the path resolved so far, which is the Unix rule. Win32 folds `.` and `..` as TEXT before it follows a link.
Measured on 2026-11-18:

- Layout: the granted root `R`, a junction `R\nm\foo` → `R\deep\a\b\c\d\e\f\g`, and a marker file `secret.txt` in a folder outside `R`.
- `Path` = `R\nm\foo\` and 7 `..` segments, then the outside file. Win32 folds it as text to `C:\Users\strug\coai-winprobe-outside\secret.txt`.
- `cmd /c type` of that path printed the outside marker. So Windows does read outside the root by that spelling.
- The real hook (0.66.0) **allowed** it. The hook judged `R\deep\…`, inside `R`. W1 is confirmed.
- agy then denied the read itself, because the folded target lay in the user profile. So W1 was not exploitable end to end in
  that run. A W1 target under the temp folder, which agy reads by itself, was not measured by that run. The real run below is meant to measure it.

### W2: network and device paths, and why the timings prove little

The hook (0.66.0) denied each of these within about 60 ms, process start-up included:

- `\\127.0.0.1\…`, `//127.0.0.1/…`, `\\?\C:\…`, `\\.\C:\…` and `\\?\UNC\127.0.0.1\…`;
- `D:\…`, another local drive.

**The limit of that evidence.** The decision is `deny` whether or not the hook looked the path up, and a lookup that fails fast on the
loopback address looks like no lookup. Timing cannot show that no SMB request was made. The proof is structural: the hook's reader
refuses a path that is not local before any I/O, and a recording reader in the tests shows it was never asked (spec S2). A packet
capture on TCP 445 would be the wire evidence; it could not be taken (`pktmon` needs administrator rights, see the real run below).

### What Windows folds (spec S3, section 1)

Measured with `[IO.Path]::GetFullPath` in Windows PowerShell 5.1, which calls `GetFullPathName` (build 10.0.26300):

| Spelling | Windows makes it | So |
|---|---|---|
| `C:\Windows\..\System32` | `C:\System32` | an exact `..` leaves its folder |
| `C:\Windows\.. \System32` | unchanged | `.. ` in the middle is a name |
| `C:\Windows\.. ` at the end | `C:\Windows\` | trimmed to the folder itself |
| `C:\Windows\...\System32` | unchanged | a name |
| `C:\Windows\.. .\System32` | `C:\Windows\.. \System32` | one dot taken off; still a name |
| `C:\Windows\System32.\drivers`, `…\drivers.`, `…\drivers ` | `C:\Windows\System32\drivers` | another spelling of the same entry |

Only a segment that is exactly `..` leaves its folder. The other spellings stay in the folder they are written in.

### W3: agy sorts `workspacePaths`, and `cwd` is its first entry

§6 said that `workspacePaths` "holds the agy cwd first" and that `cwd` "is the folder coai owns". Both held only while the coai folder
sorted first. What agy does: it SORTS `workspacePaths` and sets the payload's `cwd` to the first entry. Its own process folder and the
`cwd` of its `init` event stay the coai folder.

**Windows, agy 1.5.11.** The order is ordinal, uppercase before lowercase. The coai-like folder is the probe's own, the folder agy runs in.

| Probe | `workspacePaths`, as sent | `cwd` as sent |
|---|---|---|
| coai-like folder `coaip`, root `rootp` | `[…\coaip, …\rootp]` | `…\coaip` |
| coai-like folder `coaip`, root `zzz` | `[…\coaip, …\zzz]` | `…\coaip` |
| coai-like folder `coaip`, root `aaa` | `[…\aaa, …\coaip]` | `…\aaa` |
| coai-like folder `coaip`, root `R` | `[…\R, …\coaip]` | `…\R` |
| coai-like folder `0coaic`, root `R` | `[…\0coaic, …\R]` | `…\0coaic` |

**Linux, agy 1.5.9.** A probe folder under `/tmp/…` (the scratchpad `sortprobe` folder, the folder agy ran in) and one root,
`/home/jinx/.cache/coai-sortprobe-root`, gave `"cwd":"/home/jinx/.cache/coai-sortprobe-root"` and
`"workspacePaths":["/home/jinx/.cache/coai-sortprobe-root","/tmp/…/sortprobe"]`. The root sorts before the folder agy ran in, so it was `cwd`.

**What this broke.** The hook of §6 took `cwd` for the coai folder, denied a path inside it, and counted every other entry as a root.
When a granted root sorts before the coai folder, the hook took that root for the coai folder and denied every read in it. Replayed
through the real 0.66.0 hook on Windows, such a payload was denied with `coai: this consultation reads only inside the folders it was
given`. The same hook would have ALLOWED a read of the coai folder's own files, because the real coai folder then counted as a root.

**Who is hit.** No deployment of today: here the coai folder `/home/jinx/.local/share/coai-agy/…` sorts before `/home/jinx/git/…` and
`/tmp/…`, and on the Team server `/root/.local/share/…` sorts before `/tmp/coai-server-job-…`. A root at `/data/…`, `/code/…` or
`~/.cache/…`, or one at `C:\Projects\…` or under the Windows temp folder, is hit. The shipped hook (mcp 0.65.4 and 0.66.0, Team server 0.16.3) has the defect.

### What the change does about each

The hook of this change knows three things by itself, and the payload's `cwd` is not one of them.

| Step | Rule | Answers |
|---|---|---|
| D3 | the payload's `cwd` must be a string, fully qualified, and resolve. It is validated and names nothing | W3 |
| D6 | a path inside the resolved coai folder is denied, wherever that folder stands in `workspacePaths`, and under a wider root too | W3 |
| D8 | the coai folder must be known, fully qualified and resolvable, or every read is denied | W3 |
| D9 | the hook looks at no path that is not local. One guarded reader refuses it before any I/O, and every resolved path must be local | W2 |
| D10 | where the OS folds `.` and `..` as text (Windows), a `Path` with a segment that is exactly `.` or `..` is denied | W1 |

- **W3 (S1).** The coai folder is the parent of the hook's own working folder. agy starts the hook's relative command (`sh ./coai-hook.sh`,
  `.\coai-hook.cmd`) in the folder's `.agents` subfolder, so the hook asks where it runs. It does not compute the folder from
  `DefaultBase` and `HookHandler.Key()`: the hook process does not see the launcher's environment on the Team server (each vendor slot
  has its own `HOME` and `XDG_DATA_HOME`) and on a question row (no `XDG_DATA_HOME`), so that folder would be another one. Every
  entry of `workspacePaths` is a root, and D6 keeps the coai folder out of them. A hook that does not run from a folder named
  `.agents`, or whose folder does not resolve, denies every read and writes one constant line to standard error.
- **W2 (S2, the cornerstone).** No network or device path is ever looked up: not `Path`, a root, the payload's `cwd`, the coai folder,
  nor the target of a link met on the way. A junction inside a granted local root can point at a share, so the guard sits on the reader
  where the disk is touched, not on the spelling. A path is local when its drive letter is a local drive (not `Network`, `Unknown`
  or `NoRootDirectory`), or, on Unix, when it is rooted, does not start with `//`, and is not under `/net/`, `/misc/` or `/Network/`.
  A `Path` that starts with two separators is denied on every OS by its spelling, before it is resolved. A network root is not
  supported: the payload is denied whole.
- **W1 (S3).** On Windows a `Path` with a `.` or `..` segment is denied, before anything is resolved. `RealPath`'s own `..` rule does
  not change, because other sites rely on it. Off Windows the kernel is the walk, and nothing changes. The rule covers `Path` only: roots,
  `cwd` and the coai folder are text from coai and agy. A dot segment in a link's own text was first written down as "left to the kernel"; **that is wrong for Windows (correction 2026-11-18, review of E1)**: NT folds a relative link's text as text, while `RealPath` walks it with the Unix rule, so a relative symbolic link inside a root whose text holds `..` (`R\a` to `b\..\..\outside\secret.txt`, with `R\b` another link to `deep\er`) is judged inside and read outside. This is the probable escape of SECURITY.md, not measured on a real machine; the follow-up plan is `todo/PLAN_the_agy_hook_on_windows_judges_a_links_dot_text.md`. On Unix the kernel is the walk and the Unix rule agrees with it.

**Tests.** Rows are named in the specs and listed in `research/module_tests.md`. The classes are
`TheAgyHookKnowsItsOwnFolderTests` (F1-F14, F13 and F14 added by the review fixes of 2026-11-18; M1-M4 and S7-S9 with the built binaries), `TheAgyHookLooksOnlyAtLocalPathsTests` (N1-N25, N16 added by the review fixes;
M5 and S10) and `TheAgyHookRefusesDotSegmentsWhereTextFoldsTests` (T1-T8; M6 and S11), on the shared fixture `AgyHookFixture`.


### The real Linux run on the branch build

2026-11-18, WSL, agy 1.5.9, `gemini-4-hydrogen-low`, `--mode plan --agent coai-reader`. Each hook folder held the same
`hooks.json` and agent file coai writes, with `coai-hook.sh` starting the binary under test. One agy run per probe.

- **W3, the run that shows the fix.** The coai-like folder was under `/tmp/…/w3-<build>/`. The one granted root,
  `/home/jinx/.cache/coai-e1-root`, sorts BEFORE it, so agy's payload names that root as `cwd`.

  | Hook binary | `Path` the model sent (`read_file`, 2 calls) | Hook decision | Marker read |
  |---|---|---|---|
  | the installed `coai-mcp 0.66.0` (shipped) | `/home/jinx/.cache/coai-e1-root/inside.txt` | `denied by pre-tool hook: coai: this consultation reads only inside the folders it was given` | no |
  | the branch's Release `coai-mcp` | the same | allowed | yes |

- **The parent's `/tmp` escapes on the branch build.** Same hook folder. Two job folders `/tmp/coai-probe-job-*`, one granted;
  `leak.txt` in the granted one is a link to the other's `secret.txt`.

  | Probe | `Path` the model sent | Hook decision | Marker read |
  |---|---|---|---|
  | in-root | `…/coai-probe-job-QZvN7L/inside.txt` | allowed | the inside marker, yes |
  | absolute | `…/coai-probe-job-3G9kxs/secret.txt` | denied, the same sentence | no |
  | `..` | `…/coai-probe-job-QZvN7L/../coai-probe-job-3G9kxs/secret.txt` | denied | no |
  | link | `…/coai-probe-job-QZvN7L/leak.txt` | denied | no |

The secret marker appears in none of the outputs.

### The real Windows run on the branch build

2026-11-18. Windows 10.0.26300, agy 1.5.11, `gemini-4-hydrogen-low`. The hook binary was the branch's `coai-mcp.exe`, built from the
working tree at 82fc734e: `dotnet publish src_mcp/src -c Release -r win-x64 --self-contained -p:PublishAot=false -p:PublishSingleFile=true`.
It is NOT NativeAOT, because AOT cannot cross-compile from Linux; the release build is AOT.

The hook folders were built by hand, byte for byte: coai's `AgentFile`, `HooksFile(windows: true)` and `CmdScript`, under
`…\Temp\coai-e1\zz-coai\`. The binary cannot be told to prepare one outside the real `LOCALAPPDATA`. The root `R` (`…\coai-e1\q1\…\R`)
sorts BEFORE the coai folder. It holds:
- `inside.txt`;
- `deep\a\…\g\walk.txt`;
- a real junction `nm\foo` → `R\deep\a\b\c\d\e\f\g`.

The outside marker was in `…\coai-e1\outside\secret.txt`. That is in the temp folder, which agy reads by itself, so the hook is the
only guard. One agy run per probe. The `Path` the model sent equals the payload's `Path` in every run, and no run has a
`denied_actions` entry, so each denial below is the hook's.

| Probe | `Path` the model sent | Hook decision | Marker |
|---|---|---|---|
| in-root, root sorts first (W3) | `…\R\inside.txt` (payload `cwd` = `R`) | allowed | read |
| in-root, root sorts after | `…\zzz-R\inside.txt` | allowed | read |
| an honest junction | `…\R\nm\foo\walk.txt` | allowed | read |
| the coai folder | `…\zz-coai\hlog\.agents\hooks.json` | denied: `tool call denied by pre-tool hook: coai: this consultation reads only inside the folders it was given` | — |
| **W1, branch hook**, root first and root after | `…\R\nm\foo\..\..\..\..\..\..\..\outside\secret.txt` | denied, the same sentence | not read |
| **W1, the shipped 0.66.0 hook** (control) | the same 7-`..` path, root after | **allowed** | **read: `MARK-OUTSIDE-SECRET-b81e44`** |
| `..` inside the root | `…\R\deep\..\inside.txt` | denied | — |
| `.` inside the root | `…\R\.\inside.txt` | denied | — |
| W2 | `\\127.0.0.1\nonexistent\x` | denied | — |
| W2 | `\\127.0.0.1\c$\Windows\win.ini` | denied | — |

The premise of W1 was checked first. `[IO.Path]::GetFullPath` folds the 7-`..` path to `…\coai-e1\outside\secret.txt`, outside `R`,
and `cmd /c type` printed the marker. coai's walk would have judged it `R\deep\outside\secret.txt`, inside.

**W1 was exploitable end to end with the shipped hook.** The 0.66.0 control read the outside file through a junction and `..` under
the temp folder. The branch hook denies the same read.

**A direct replay through the bare hook.** It ran from the folder's `.agents`, with allowed controls first: `R\inside.txt`, and the
honest junction's `walk.txt`. Each of the following was denied with the place sentence:
- the 7-`..` path, `R\deep\..\inside.txt` and `R\.\inside.txt`;
- `\\127.0.0.1\nonexistent\x`, `//127.0.0.1/nonexistent/x` and `\\?\C:\…\outside\secret.txt`;
- the coai folder's `hooks.json`;
- the plain outside path;
- a payload whose `workspacePaths` held a UNC entry.

A payload whose `cwd` named `R` still allowed `R\inside.txt`: the payload's `cwd` is ignored. `write_to_file` got the read-only
sentence. Run from outside any `.agents` folder, the hook said on stderr `coai --agy-hook: denied, this hook does not run from its
own .agents folder` and denied.

**Not run, because both need administrator rights, which this session does not have:**
- A junction or symbolic link to a UNC target. `mklink /D … \\127.0.0.1\c$` answered `You do not have sufficient privilege to
  perform this operation`, and `mklink /J` answered `Local volumes are required to complete the operation`. That case rests on the
  reader-seam tests (N-rows of `TheAgyHookLooksOnlyAtLocalPathsTests`) and the Windows CI job.
- A packet capture. `pktmon` answered `Failed to communicate with the PktMon driver: Access is denied`. "No SMB handshake" therefore
  rests on the recording reader in the tests, not on the wire.

Logs: the session scratchpad `winprobe/e1/` (`logs/*.ndjson`, `logs/payloads.log`, `replay-bare-hook.txt`). Every probe folder and
junction was removed afterwards.

### Limits of this section

- **A network checkout is not supported.** A root on a UNC path, a mapped network drive, `\\wsl$\…` or, on Unix, under `/net/`,
  `/misc/` or `/Network/` has every agy read denied. A consult grants the person's own checkout, so one kept in such a place loses
  agy's reads.
- **A network file system mounted into the Unix tree** (nfs or cifs at an ordinary folder) is spelled like a local path and is not
  detected. A custom autofs map on another root is not detected either.
- **A harmless `..` is lost on Windows.** The model can send the path again without it.
- **Another spelling of one entry** (an 8.3 short name, a trailing dot or space, an NTFS stream name) is a different string. Under a
  wide root it can name the coai folder's own files without being seen as inside it. The folder holds no secret.
- **A dot segment in a link's own text** is not judged, and on Windows that is a probable escape (correction 2026-11-18, review of E1; the first text read "What NT does with an absolute link target that holds `..` is not measured", which is still true of the absolute case): a relative symbolic link inside a root whose text holds `..` is walked with the Unix rule and read by NT with the text folded. Not measured on a real machine. Follow-up: `todo/PLAN_the_agy_hook_on_windows_judges_a_links_dot_text.md`.
- **Whether agy cleans a path before it opens it** is not measured. The rule does not depend on it.
- **One Windows build** was measured. Microsoft's description of path normalisation says the same.
- **The wire.** That Windows sends no NTLM handshake is shown by the recording reader in the tests, not by a packet capture: the real run above could not take one (`pktmon` needs administrator rights).
- **It trusts where agy starts the hook.** If a later agy starts it elsewhere, every read is denied and the standard-error line says why.

## Not measured

- **agy 1.5.8 or older with the fix.** The 1.5.8 binary is no longer on this machine. The fix keeps `view_file`
  allowed, so a launch that offers it behaves as before.
- **Windows.** *(Note 2026-11-18: measured on agy 1.5.11, see §7. What §7 does not measure is listed there.)*
- **Whether `view_file` read `/tmp` on 1.5.8.** So whether the `/tmp` allowance is new in 1.5.9 is unknown.
- **The Team server's agy version.** The server shares `CoaiMcp.Runners`, so the fix reaches it with the next server
  release.
