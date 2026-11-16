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

## Not measured

- **agy 1.5.8 or older with the fix.** The 1.5.8 binary is no longer on this machine. The fix keeps `view_file`
  allowed, so a launch that offers it behaves as before.
- **Windows.**
- **Whether `view_file` read `/tmp` on 1.5.8.** So whether the `/tmp` allowance is new in 1.5.9 is unknown.
- **The Team server's agy version.** The server shares `CoaiMcp.Runners`, so the fix reaches it with the next server
  release.
